using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexSeek.Services;

/// <summary>
/// Line-based editor for the small subset of TOML Codex writes to config.toml.
/// It intentionally preserves the original line endings, blank lines and trailing newline.
/// </summary>
public sealed class TomlDocument
{
    private static readonly Regex SectionHeaderPattern = new(@"^\s*\[", RegexOptions.Compiled);
    private static readonly Regex DeepSeekSectionPattern = new(
        @"^\s*\[\s*model_providers\s*\.\s*(?:""deepseek""|'deepseek'|deepseek)\s*\]\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly List<string> _lines;
    private readonly string _newLine;
    private readonly bool _trailingNewLine;

    private TomlDocument(List<string> lines, string newLine, bool trailingNewLine)
    {
        _lines = lines;
        _newLine = newLine;
        _trailingNewLine = trailingNewLine;
    }

    public IReadOnlyList<string> Lines => _lines;

    public static TomlDocument Load(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return Parse(text);
    }

    public static TomlDocument Parse(string text)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var trailing = text.EndsWith("\n", StringComparison.Ordinal);

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var parts = normalized.Split('\n').ToList();

        if (trailing && parts.Count > 0 && parts[^1].Length == 0)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return new TomlDocument(parts, newLine, trailing);
    }

    public string Serialize()
    {
        var body = string.Join(_newLine, _lines);
        if (_trailingNewLine)
        {
            body += _newLine;
        }

        return body;
    }

    public void SaveAtomic(string path)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    // ---------------------------------------------------------------- top-level

    public int GetTopLevelKeyIndex(string key)
    {
        var pattern = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.Compiled);
        for (var i = 0; i < _lines.Count; i++)
        {
            if (SectionHeaderPattern.IsMatch(_lines[i]))
            {
                return -1;
            }

            if (pattern.IsMatch(_lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public int GetTopLevelInsertIndex()
    {
        var sectionIndex = _lines.Count;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (SectionHeaderPattern.IsMatch(_lines[i]))
            {
                sectionIndex = i;
                break;
            }
        }

        while (sectionIndex > 0 && string.IsNullOrWhiteSpace(_lines[sectionIndex - 1]))
        {
            sectionIndex--;
        }

        return sectionIndex;
    }

    public void SetTopLevelKey(string key, string tomlValue)
    {
        var index = GetTopLevelKeyIndex(key);
        if (index >= 0)
        {
            _lines[index] = $"{key} = {tomlValue}";
            return;
        }

        _lines.Insert(GetTopLevelInsertIndex(), $"{key} = {tomlValue}");
    }

    public void RemoveTopLevelKey(string key)
    {
        var index = GetTopLevelKeyIndex(key);
        if (index >= 0)
        {
            _lines.RemoveAt(index);
        }
    }

    public string? GetTopLevelValue(string key)
    {
        var index = GetTopLevelKeyIndex(key);
        return index < 0 ? null : GetValueFromLine(_lines[index]);
    }

    // ---------------------------------------------------------------- deepseek section

    public (int Start, int End)? GetDeepSeekSectionBounds()
    {
        var start = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (DeepSeekSectionPattern.IsMatch(_lines[i]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var end = _lines.Count;
        for (var i = start + 1; i < _lines.Count; i++)
        {
            if (SectionHeaderPattern.IsMatch(_lines[i]))
            {
                end = i;
                break;
            }
        }

        return (start, end);
    }

    public void EnsureDeepSeekSection()
    {
        if (GetDeepSeekSectionBounds() is not null)
        {
            return;
        }

        if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
        {
            _lines.Add(string.Empty);
        }

        _lines.Add("[model_providers.deepseek]");
    }

    private int GetSectionKeyIndex(string key, int start, int end)
    {
        var pattern = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.Compiled);
        for (var i = start + 1; i < end; i++)
        {
            if (pattern.IsMatch(_lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public string? GetDeepSeekValue(string key)
    {
        if (GetDeepSeekSectionBounds() is not { } bounds)
        {
            return null;
        }

        var index = GetSectionKeyIndex(key, bounds.Start, bounds.End);
        return index < 0 ? null : GetValueFromLine(_lines[index]);
    }

    public void SetDeepSeekKey(string key, string tomlValue)
    {
        if (GetDeepSeekSectionBounds() is not { } bounds)
        {
            throw new InvalidOperationException(
                "config.toml 里没有 [model_providers.deepseek] 段。请先运行 DeepSeek 官方导入脚本。");
        }

        var index = GetSectionKeyIndex(key, bounds.Start, bounds.End);
        if (index >= 0)
        {
            _lines[index] = $"{key} = {tomlValue}";
            return;
        }

        var insertAt = bounds.End;
        while (insertAt > bounds.Start + 1 && string.IsNullOrWhiteSpace(_lines[insertAt - 1]))
        {
            insertAt--;
        }

        _lines.Insert(insertAt, $"{key} = {tomlValue}");
    }

    // ---------------------------------------------------------------- values

    public static string? GetValueFromLine(string line)
    {
        var doubleQuoted = Regex.Match(line, "=\\s*\"(.*)\"\\s*$");
        if (doubleQuoted.Success)
        {
            return doubleQuoted.Groups[1].Value;
        }

        var singleQuoted = Regex.Match(line, "=\\s*'(.*)'\\s*$");
        if (singleQuoted.Success)
        {
            return singleQuoted.Groups[1].Value;
        }

        var trimmed = Regex.Replace(line, "^[^=]*=\\s*", string.Empty).TrimEnd();
        return trimmed;
    }

    public static string Quote(string value)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }
}
