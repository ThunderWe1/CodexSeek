using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexSeek.Services;

public enum CodexMode
{
    Unknown,
    Gpt,
    DeepSeek
}

public sealed record DeepSeekEnvironment(
    bool ApiKeyPresent,
    string BaseUrl,
    bool Ready,
    IReadOnlyList<string> Missing,
    string ConfigPath,
    string CatalogPath);

public sealed record SwitchResult(
    CodexMode Target,
    string Model,
    string? BackupPath)
{
    public bool AppWasRunning { get; init; }
    public bool AppRestarted { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record ApiKeySaveResult(bool Changed, string? BackupPath)
{
    public bool AppWasRunning { get; init; }
    public bool AppRestarted { get; init; }
}

public sealed class CodexConfigService
{
    private const string CatalogResourceName = "CodexSeek.Assets.deepseek-models.json";

    public const string GptModel = "gpt-6.1-sol";
    public const string DeepSeekModel = "deepseek-flash";
    public const string ApiKeyName = "experimental_bearer_token";
    public const string DefaultDeepSeekBaseUrl = "https://api.deepseek.com/";

    private static readonly JsonSerializerOptions CatalogJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly CodexPaths _paths;

    public CodexConfigService(CodexPaths paths)
    {
        _paths = paths;
    }

    public string? GetCurrentModel()
    {
        try
        {
            return File.Exists(_paths.ConfigPath)
                ? TomlDocument.Load(_paths.ConfigPath).GetTopLevelValue("model")
                : null;
        }
        catch
        {
            return null;
        }
    }

    public CodexMode GetCurrentMode()
    {
        var model = GetCurrentModel();
        if (string.Equals(model, GptModel, StringComparison.OrdinalIgnoreCase))
        {
            return CodexMode.Gpt;
        }

        return !string.IsNullOrWhiteSpace(model) &&
               model.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase)
            ? CodexMode.DeepSeek
            : CodexMode.Unknown;
    }

    public string? GetDeepSeekApiKey()
    {
        try
        {
            return File.Exists(_paths.ConfigPath)
                ? TomlDocument.Load(_paths.ConfigPath).GetDeepSeekValue(ApiKeyName)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public DeepSeekEnvironment DetectEnvironment()
    {
        var configExists = File.Exists(_paths.ConfigPath);
        var providerExists = false;
        string? baseUrl = null;
        var apiKeyPresent = false;

        if (configExists)
        {
            try
            {
                var document = TomlDocument.Load(_paths.ConfigPath);
                providerExists = document.GetDeepSeekSectionBounds() is not null;
                if (providerExists)
                {
                    baseUrl = document.GetDeepSeekValue("base_url");
                    apiKeyPresent = !string.IsNullOrWhiteSpace(document.GetDeepSeekValue(ApiKeyName));
                }
            }
            catch
            {
                providerExists = false;
            }
        }

        var catalogReady = false;
        if (File.Exists(_paths.CatalogPath))
        {
            try
            {
                using var json = JsonDocument.Parse(
                    File.ReadAllText(_paths.CatalogPath, Encoding.UTF8));
                catalogReady =
                    json.RootElement.TryGetProperty("models", out var models) &&
                    models.ValueKind == JsonValueKind.Array &&
                    models.EnumerateArray().Any(model =>
                        model.TryGetProperty("slug", out var slug) &&
                        slug.ValueKind == JsonValueKind.String &&
                        string.Equals(
                            slug.GetString(),
                            DeepSeekModel,
                            StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                catalogReady = false;
            }
        }

        var missing = new List<string>();
        if (!configExists)
        {
            missing.Add("config.toml 不存在");
        }
        else if (!providerExists)
        {
            missing.Add("DeepSeek 提供商配置不存在");
        }
        else if (string.IsNullOrWhiteSpace(baseUrl))
        {
            missing.Add("DeepSeek API 地址未设置");
        }

        if (!catalogReady)
        {
            missing.Add("DeepSeek 模型目录未就绪");
        }

        return new DeepSeekEnvironment(
            apiKeyPresent,
            string.IsNullOrWhiteSpace(baseUrl) ? DefaultDeepSeekBaseUrl : baseUrl,
            missing.Count == 0,
            missing,
            _paths.ConfigPath,
            _paths.CatalogPath);
    }

    public async Task<SwitchResult> InitializeDeepSeekAsync(
        string apiKey,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var change = await ChangeWithCodexRestartAsync(
            () => InitializeDeepSeekConfig(apiKey),
            progress,
            cancellationToken);

        return change.Value with
        {
            AppWasRunning = change.AppWasRunning,
            AppRestarted = change.AppRestarted
        };
    }

    public async Task<SwitchResult> SwitchAsync(
        CodexMode target,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var change = await ChangeWithCodexRestartAsync(
            () => SwitchConfig(target),
            progress,
            cancellationToken);

        return change.Value with
        {
            AppWasRunning = change.AppWasRunning,
            AppRestarted = change.AppRestarted
        };
    }

    public async Task<ApiKeySaveResult> SaveDeepSeekApiKeyAsync(
        string apiKey,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeAndValidateKey(apiKey);
        if (string.Equals(GetDeepSeekApiKey(), key, StringComparison.Ordinal))
        {
            return new ApiKeySaveResult(false, null);
        }

        var change = await ChangeWithCodexRestartAsync(
            () => SetDeepSeekApiKey(key),
            progress,
            cancellationToken);

        return change.Value with
        {
            AppWasRunning = change.AppWasRunning,
            AppRestarted = change.AppRestarted
        };
    }

    internal SwitchResult InitializeDeepSeekConfig(string apiKey)
    {
        var key = NormalizeAndValidateKey(apiKey);
        Directory.CreateDirectory(_paths.CodexHome);

        var warnings = new List<string>();
        var catalogWarning = EnsureModelCatalog();
        if (catalogWarning is not null)
        {
            warnings.Add(catalogWarning);
        }

        var configExists = File.Exists(_paths.ConfigPath);
        var backupPath = configExists ? BackupConfig() : null;
        var document = configExists
            ? TomlDocument.Load(_paths.ConfigPath)
            : TomlDocument.Parse(string.Empty);

        document.EnsureDeepSeekSection();
        if (string.IsNullOrWhiteSpace(document.GetDeepSeekValue("name")))
        {
            document.SetDeepSeekKey("name", TomlDocument.Quote("deepseek"));
        }

        if (string.IsNullOrWhiteSpace(document.GetDeepSeekValue("base_url")))
        {
            document.SetDeepSeekKey(
                "base_url",
                TomlDocument.Quote(DefaultDeepSeekBaseUrl));
        }

        if (string.IsNullOrWhiteSpace(document.GetDeepSeekValue("wire_api")))
        {
            document.SetDeepSeekKey("wire_api", TomlDocument.Quote("responses"));
        }

        document.SetDeepSeekKey(ApiKeyName, TomlDocument.Quote(key));
        ApplyDeepSeekMode(document);
        document.SaveAtomic(_paths.ConfigPath);

        return new SwitchResult(CodexMode.DeepSeek, DeepSeekModel, backupPath)
        {
            Warnings = warnings
        };
    }

    internal SwitchResult SwitchConfig(CodexMode target)
    {
        if (target is not (CodexMode.Gpt or CodexMode.DeepSeek))
        {
            throw new ArgumentOutOfRangeException(nameof(target), "切换目标无效。");
        }

        if (!File.Exists(_paths.ConfigPath))
        {
            throw new FileNotFoundException($"找不到配置文件：{_paths.ConfigPath}");
        }

        if (target == CodexMode.DeepSeek && !DetectEnvironment().Ready)
        {
            throw new InvalidOperationException("DeepSeek 环境尚未初始化。");
        }

        var backupPath = BackupConfig();
        var document = TomlDocument.Load(_paths.ConfigPath);
        if (target == CodexMode.Gpt)
        {
            ApplyGptMode(document);
        }
        else
        {
            ApplyDeepSeekMode(document);
        }

        document.SaveAtomic(_paths.ConfigPath);
        return new SwitchResult(
            target,
            target == CodexMode.Gpt ? GptModel : DeepSeekModel,
            backupPath);
    }

    internal ApiKeySaveResult SetDeepSeekApiKey(string rawKey)
    {
        var key = NormalizeAndValidateKey(rawKey);
        if (!File.Exists(_paths.ConfigPath))
        {
            throw new FileNotFoundException($"找不到配置文件：{_paths.ConfigPath}");
        }

        var document = TomlDocument.Load(_paths.ConfigPath);
        if (document.GetDeepSeekSectionBounds() is null)
        {
            throw new InvalidOperationException("DeepSeek 环境尚未初始化。");
        }

        if (string.Equals(document.GetDeepSeekValue(ApiKeyName), key, StringComparison.Ordinal))
        {
            return new ApiKeySaveResult(false, null);
        }

        var backupPath = BackupConfig();
        document.SetDeepSeekKey(ApiKeyName, TomlDocument.Quote(key));
        document.SaveAtomic(_paths.ConfigPath);
        return new ApiKeySaveResult(true, backupPath);
    }

    private async Task<(T Value, bool AppWasRunning, bool AppRestarted)> ChangeWithCodexRestartAsync<T>(
        Func<T> change,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var appWasRunning = CodexDesktopService.IsRunning();
        if (appWasRunning)
        {
            progress?.Report("正在关闭 Codex 桌面 App...");
            await CodexDesktopService.StopAsync(progress, cancellationToken).ConfigureAwait(false);
            if (CodexDesktopService.IsRunning())
            {
                throw new InvalidOperationException(
                    "无法关闭 Codex 桌面 App，请手动退出 Codex 后重试。");
            }
        }

        T value;
        try
        {
            value = change();
        }
        catch
        {
            if (appWasRunning)
            {
                await StartAndWaitAsync(progress, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        var appRestarted = appWasRunning &&
                           await StartAndWaitAsync(progress, cancellationToken).ConfigureAwait(false);
        return (value, appWasRunning, appRestarted);
    }

    private static async Task<bool> StartAndWaitAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            progress?.Report("正在启动 Codex 桌面 App...");
            CodexDesktopService.Start();
            return await CodexDesktopService
                .WaitForStartAsync(TimeSpan.FromSeconds(20), progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private void ApplyGptMode(TomlDocument document)
    {
        document.RemoveTopLevelKey("model_catalog_json");
        document.RemoveTopLevelKey("model_provider");
        document.RemoveTopLevelKey("preferred_auth_method");
        document.RemoveTopLevelKey("forced_login_method");
        document.RemoveTopLevelKey("web_search");

        document.SetTopLevelKey("model", TomlDocument.Quote(GptModel));
        document.SetTopLevelKey("model_reasoning_effort", TomlDocument.Quote("xhigh"));
        document.SetTopLevelKey("service_tier", TomlDocument.Quote("default"));
    }

    private void ApplyDeepSeekMode(TomlDocument document)
    {
        document.RemoveTopLevelKey("service_tier");

        document.SetTopLevelKey("model", TomlDocument.Quote(DeepSeekModel));
        document.SetTopLevelKey("model_reasoning_effort", TomlDocument.Quote("high"));
        document.SetTopLevelKey("model_provider", TomlDocument.Quote("deepseek"));
        document.SetTopLevelKey(
            "model_catalog_json",
            TomlDocument.Quote(_paths.CatalogPath.Replace('\\', '/')));
        document.SetTopLevelKey("web_search", TomlDocument.Quote("disabled"));
        document.SetTopLevelKey("preferred_auth_method", TomlDocument.Quote("apikey"));
        document.SetTopLevelKey("forced_login_method", TomlDocument.Quote("api"));
    }

    private string? EnsureModelCatalog()
    {
        var template = LoadCatalogTemplate();
        if (!File.Exists(_paths.CatalogPath))
        {
            WriteCatalog(template);
            return null;
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(_paths.CatalogPath, Encoding.UTF8))
                       as JsonObject
                   ?? throw new JsonException("模型目录不是 JSON 对象。");
        }
        catch
        {
            BackupCatalog();
            WriteCatalog(template);
            return "原模型目录无法解析，已备份为 models.json.bak 并重新生成。";
        }

        if (root["models"] is not JsonArray models)
        {
            BackupCatalog();
            root["models"] = template["models"]?.DeepClone();
            WriteCatalog(root);
            return "原模型目录格式无效，已备份为 models.json.bak 并重新生成。";
        }

        var slugs = models
            .Select(GetCatalogSlug)
            .Where(slug => slug is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingModels = ((JsonArray)template["models"]!)
            .Where(model => !slugs.Contains(GetCatalogSlug(model) ?? string.Empty))
            .Select(model => model!.DeepClone())
            .ToList();
        if (missingModels.Count == 0)
        {
            return null;
        }

        BackupCatalog();
        foreach (var model in missingModels)
        {
            models.Add(model);
        }

        WriteCatalog(root);
        return null;
    }

    private static string? GetCatalogSlug(JsonNode? model)
    {
        return model?["slug"] is JsonValue value &&
               value.TryGetValue<string>(out var slug)
            ? slug
            : null;
    }

    private static JsonObject LoadCatalogTemplate()
    {
        using var stream = typeof(CodexConfigService).Assembly
            .GetManifestResourceStream(CatalogResourceName)
            ?? throw new InvalidOperationException("内置 DeepSeek 模型目录缺失。");
        return JsonNode.Parse(stream) as JsonObject
               ?? throw new InvalidOperationException("内置 DeepSeek 模型目录无效。");
    }

    private void WriteCatalog(JsonObject root)
    {
        var temporaryPath = _paths.CatalogPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            root.ToJsonString(CatalogJsonOptions),
            new UTF8Encoding(false));
        File.Move(temporaryPath, _paths.CatalogPath, overwrite: true);
    }

    private void BackupCatalog()
    {
        File.Copy(_paths.CatalogPath, _paths.CatalogPath + ".bak", overwrite: true);
    }

    private string BackupConfig()
    {
        var backup = _paths.ConfigPath + ".bak";
        File.Copy(_paths.ConfigPath, backup, overwrite: true);

        var stamped = _paths.ConfigPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Copy(_paths.ConfigPath, stamped, overwrite: true);

        try
        {
            var directory = Path.GetDirectoryName(_paths.ConfigPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                foreach (var old in Directory
                             .GetFiles(directory, "config.toml.bak-*")
                             .OrderByDescending(File.GetLastWriteTimeUtc)
                             .Skip(10))
                {
                    File.Delete(old);
                }
            }
        }
        catch
        {
            // Backup rotation is best effort.
        }

        return backup;
    }

    public static string NormalizeAndValidateKey(string rawKey)
    {
        var key = (rawKey ?? string.Empty).Trim();
        if (key.Length >= 2 &&
            ((key[0] == '"' && key[^1] == '"') || (key[0] == '\'' && key[^1] == '\'')))
        {
            key = key[1..^1].Trim();
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("API Key 不能为空。");
        }

        if (key.Any(character =>
                char.IsControl(character) || character == '"' || character == '\\'))
        {
            throw new ArgumentException("API Key 含有不允许的字符。");
        }

        return key;
    }

    public static bool LooksLikeDeepSeekKey(string key)
    {
        return Regex.IsMatch(
            key ?? string.Empty,
            @"^sk-[A-Za-z0-9_-]{10,}$");
    }
}
