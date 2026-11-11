using System.IO;
using System.Text;

namespace CodexSeek.Services;

/// <summary>
/// Hidden diagnostics mode: CodexSeek.exe --self-test.
/// It exercises only config editing against throw-away CODEX_HOME directories.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        var report = new List<string>();
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexSeek-SelfTest-" + Guid.NewGuid().ToString("N"));
        var succeeded = false;

        try
        {
            Directory.CreateDirectory(root);

            var configPath = Path.Combine(root, "config.toml");
            var catalogPath = Path.Combine(root, "models.json");
            File.WriteAllText(
                configPath,
                """
                model = "deepseek-flash"
                model_reasoning_effort = "high"
                model_provider = "deepseek"
                model_catalog_json = "C:/placeholder/models.json"
                web_search = "disabled"
                preferred_auth_method = "apikey"
                forced_login_method = "api"

                [desktop]
                followUpQueueMode = "steer"

                [model_providers.deepseek]
                name = "deepseek"
                base_url = "https://api.deepseek.com/"
                wire_api = "responses"
                experimental_bearer_token = "sk-oldoldoldoldoldoldoldoldoldold"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(
                catalogPath,
                """{"models":[{"slug":"deepseek-flash"},{"slug":"deepseek-v4-pro"}]}""",
                new UTF8Encoding(false));

            var paths = new CodexPaths(root);
            var service = new CodexConfigService(paths);

            var environment = service.DetectEnvironment();
            Check(report, "environment ready", environment.Ready);
            Check(report, "current mode is deepseek", service.GetCurrentMode() == CodexMode.DeepSeek);

            var savedKey = "sk-1234567890abcdef1234567890abcdef";
            var saveResult = service.SetDeepSeekApiKey(savedKey);
            Check(report, "api key changed", saveResult.Changed);
            Check(report, "api key persisted", service.GetDeepSeekApiKey() == savedKey);
            Check(report, "backup created", File.Exists(configPath + ".bak"));

            var gpt = service.SwitchConfig(CodexMode.Gpt);
            Check(report, "switch to gpt", gpt.Model == CodexConfigService.GptModel);
            var afterGpt = TomlDocument.Load(configPath);
            Check(report, "gpt removes model_provider", afterGpt.GetTopLevelValue("model_provider") is null);
            Check(report, "gpt keeps deepseek section", afterGpt.GetDeepSeekSectionBounds() is not null);

            var deepSeek = service.SwitchConfig(CodexMode.DeepSeek);
            Check(report, "switch to deepseek", deepSeek.Model == CodexConfigService.DeepSeekModel);
            var afterDeepSeek = TomlDocument.Load(configPath);
            Check(report, "deepseek sets provider", afterDeepSeek.GetTopLevelValue("model_provider") == "deepseek");
            Check(report, "deepseek keeps api key", service.GetDeepSeekApiKey() == savedKey);

            Check(report, "key format accepts sk-", CodexConfigService.LooksLikeDeepSeekKey(savedKey));
            Check(report, "key format rejects bad key", !CodexConfigService.LooksLikeDeepSeekKey("not-a-key"));
            Check(
                report,
                "application log is not created",
                !File.Exists(Path.Combine(root, "codex-switch.log")));

            var freshRoot = Path.Combine(root, "fresh");
            var freshService = new CodexConfigService(new CodexPaths(freshRoot));
            var initialized = freshService.InitializeDeepSeekConfig(savedKey);
            Check(report, "initial setup writes deepseek config", initialized.Model == CodexConfigService.DeepSeekModel);
            Check(report, "initial setup creates ready environment", freshService.DetectEnvironment().Ready);
            Check(report, "initial setup persists key", freshService.GetDeepSeekApiKey() == savedKey);
            Check(report, "initial setup sets deepseek mode", freshService.GetCurrentMode() == CodexMode.DeepSeek);

            Check(report, "missing environment not ready", DetectMissing(root));
            succeeded = report.All(line => line.StartsWith("PASS", StringComparison.Ordinal));
        }
        catch (Exception exception)
        {
            report.Add("FAIL exception: " + exception);
        }
        finally
        {
            report.Add(succeeded ? "SELF-TEST PASS" : "SELF-TEST FAIL");
            if (!succeeded)
            {
                try
                {
                    Directory.CreateDirectory(root);
                    File.WriteAllLines(
                        Path.Combine(root, "self-test-report.txt"),
                        report,
                        new UTF8Encoding(false));
                }
                catch
                {
                    // Best effort.
                }
            }
            else
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                    // Best effort.
                }
            }
        }

        return succeeded ? 0 : 1;
    }

    private static bool DetectMissing(string root)
    {
        var missingRoot = Path.Combine(
            root,
            "missing-" + Guid.NewGuid().ToString("N"));
        var environment = new CodexConfigService(new CodexPaths(missingRoot)).DetectEnvironment();
        return !environment.Ready && environment.Missing.Count >= 2;
    }

    private static void Check(List<string> report, string name, bool condition)
    {
        report.Add((condition ? "PASS " : "FAIL ") + name);
    }
}
