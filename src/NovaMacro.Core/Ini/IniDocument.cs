using System.Text;

namespace NovaMacro.Core.Ini;

/// <summary>
/// Order-preserving INI document compatible with the Settings.ini layout used by
/// the original GTAOL macro tool ("[节]" / "键=值", values stored verbatim).
/// </summary>
public sealed class IniDocument
{
    private readonly List<IniSection> _sections = new();
    private readonly object _gate = new();

    public IReadOnlyList<IniSection> Sections
    {
        get { lock (_gate) { return _sections.ToArray(); } }
    }

    public static IniDocument Parse(string text)
    {
        var doc = new IniDocument();
        IniSection? current = null;
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                current = doc.GetOrAddSection(trimmed[1..^1].Trim());
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            current ??= doc.GetOrAddSection(string.Empty);
            current.Set(line[..eq].Trim(), line[(eq + 1)..]);
        }

        return doc;
    }

    public static IniDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            return new IniDocument();
        }

        return Parse(IniEncoding.Decode(File.ReadAllBytes(path)));
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.Move(temp, path, overwrite: true);
    }

    public string? Get(string section, string key)
    {
        lock (_gate)
        {
            return Find(section)?.Get(key);
        }
    }

    public void Set(string section, string key, string value)
    {
        lock (_gate)
        {
            GetOrAddSectionUnlocked(section).Set(key, value);
        }
    }

    public bool Remove(string section, string key)
    {
        lock (_gate)
        {
            return Find(section)?.Remove(key) ?? false;
        }
    }

    public IniSection GetOrAddSection(string name)
    {
        lock (_gate)
        {
            return GetOrAddSectionUnlocked(name);
        }
    }

    public IniSection? Find(string name)
    {
        lock (_gate)
        {
            return _sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        }
    }

    public IniDocument Clone() => Parse(ToString());

    /// <summary>Copies every value from <paramref name="other"/> over this document.</summary>
    public void MergeFrom(IniDocument other)
    {
        foreach (var section in other.Sections)
        {
            foreach (var (key, value) in section.Entries)
            {
                Set(section.Name, key, value);
            }
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        lock (_gate)
        {
            foreach (var section in _sections)
            {
                if (section.Name.Length > 0 || sb.Length > 0)
                {
                    sb.Append('[').Append(section.Name).Append(']').Append("\r\n");
                }

                foreach (var (key, value) in section.Entries)
                {
                    sb.Append(key).Append('=').Append(value).Append("\r\n");
                }
            }
        }

        return sb.ToString();
    }

    private IniSection GetOrAddSectionUnlocked(string name)
    {
        var existing = _sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var created = new IniSection(name);
        _sections.Add(created);
        return created;
    }
}

public sealed class IniSection
{
    private readonly List<KeyValuePair<string, string>> _entries = new();

    internal IniSection(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries.ToArray();

    public string? Get(string key)
    {
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    public void Set(string key, string value)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal))
            {
                _entries[i] = new KeyValuePair<string, string>(key, value);
                return;
            }
        }

        _entries.Add(new KeyValuePair<string, string>(key, value));
    }

    public bool Remove(string key)
    {
        var index = _entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        _entries.RemoveAt(index);
        return true;
    }
}
