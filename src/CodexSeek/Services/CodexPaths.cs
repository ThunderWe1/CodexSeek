using System.IO;

namespace CodexSeek.Services;

/// <summary>
/// Resolves the Codex home directory and all files the app touches.
/// </summary>
public sealed class CodexPaths
{
    public string CodexHome { get; }
    public string ConfigPath => Path.Combine(CodexHome, "config.toml");
    public string CatalogPath => Path.Combine(CodexHome, "models.json");

    public CodexPaths(string? overrideHome = null)
    {
        var home = overrideHome;
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("CODEX_HOME");
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
        }

        CodexHome = Path.GetFullPath(Environment.ExpandEnvironmentVariables(home));
    }
}
