using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using NovaMacro.Core.Features;
using NovaMacro.Core.Ini;
using NovaMacro.Core.Input;

namespace NovaMacro.Core.Settings;

/// <summary>
/// Typed, thread-safe access to Settings.ini. Missing values fall back to the bundled
/// defaults (the user's own Settings.ini) and then to the schema defaults.
/// </summary>
public sealed class SettingsStore
{
    private readonly IniDocument _document;
    private readonly IniDocument _defaults;
    private readonly string? _path;
    private readonly object _saveGate = new();

    // Resolved (file → bundled defaults) raw values. Settings are read on the input-hook thread for
    // every key press, so lookups must be lock-free and O(1). Invalidated on every write.
    private readonly ConcurrentDictionary<(string Section, string Key), string?> _cache = new();

    public SettingsStore(IniDocument document, string? path = null, IniDocument? defaults = null)
    {
        _document = document;
        _path = path;
        _defaults = defaults ?? LoadBundledDefaults();
    }

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public string? FilePath => _path;

    public IniDocument Document => _document;

    /// <summary>Loads the settings file, creating it from the bundled defaults when missing.</summary>
    public static SettingsStore Open(string path)
    {
        var defaults = LoadBundledDefaults();
        if (!File.Exists(path))
        {
            var created = defaults.Clone();
            var store = new SettingsStore(created, path, defaults);
            store.Save();
            return store;
        }

        return new SettingsStore(IniDocument.Load(path), path, defaults);
    }

    public static IniDocument LoadBundledDefaults()
    {
        using var stream = typeof(SettingsStore).Assembly.GetManifestResourceStream("NovaMacro.DefaultSettings.ini");
        if (stream is null)
        {
            return new IniDocument();
        }

        using var reader = new StreamReader(stream);
        return IniDocument.Parse(reader.ReadToEnd());
    }

    public string GetString(string section, string key, string? fallback = null)
    {
        var value = _cache.GetOrAdd((section, key), k => _document.Get(k.Section, k.Key) ?? _defaults.Get(k.Section, k.Key));
        if (value is not null)
        {
            return value;
        }

        return fallback ?? SettingsSchema.Find(section, key)?.Default ?? string.Empty;
    }

    public bool GetBool(string section, string key, bool? fallback = null)
    {
        var schemaDefault = SettingsSchema.Find(section, key)?.Default;
        var defaultValue = fallback ?? SettingValues.ToBool(schemaDefault);
        return SettingValues.ToBool(GetString(section, key, fallback.HasValue ? SettingValues.FromBool(fallback.Value) : null), defaultValue);
    }

    public int GetInt(string section, string key, int? fallback = null)
    {
        var descriptor = SettingsSchema.Find(section, key);
        var raw = GetString(section, key, fallback?.ToString(CultureInfo.InvariantCulture));
        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            value = fallback ?? (int.TryParse(descriptor?.Default, out var d) ? d : 0);
        }

        if (descriptor is not null)
        {
            value = Math.Clamp(value, descriptor.Min, descriptor.Max);
        }

        return value;
    }

    public void Set(string section, string key, string value)
    {
        var old = _document.Get(section, key);
        if (old == value)
        {
            return;
        }

        _document.Set(section, key, value);
        _cache.TryRemove((section, key), out _);
        Changed?.Invoke(this, new SettingChangedEventArgs(section, key, value));
    }

    public void Remove(string section, string key)
    {
        if (_document.Remove(section, key))
        {
            _cache.TryRemove((section, key), out _);
            Changed?.Invoke(this, new SettingChangedEventArgs(section, key, string.Empty));
        }
    }

    public void SetBool(string section, string key, bool value) => Set(section, key, SettingValues.FromBool(value));

    public void SetInt(string section, string key, int value) => Set(section, key, value.ToString(CultureInfo.InvariantCulture));

    public bool IsFeatureOn(FeatureDefinition feature)
    {
        if (feature.Switch is null)
        {
            return true;
        }

        return GetBool(Sec.Switches, feature.Switch, feature.DefaultOn);
    }

    public void SetFeatureOn(FeatureDefinition feature, bool on)
    {
        if (feature.Switch is not null)
        {
            SetBool(Sec.Switches, feature.Switch, on);
        }
    }

    public Hotkey GetHotkey(FeatureDefinition feature)
    {
        if (feature.HotkeySection is null)
        {
            return feature.DefaultHotkey;
        }

        if (feature.HotkeyKey is { } prefix)
        {
            // Original layout: [小工具配置] 结束进程热键=… / 结束进程热键功能键=… / 结束进程热键副键=…
            // (falls back to the section layout used by NovaMacro 2.0 builds before this change).
            return ReadHotkey(feature.HotkeySection, null, prefix) ?? ReadHotkey(prefix, feature.DefaultHotkey);
        }

        return ReadHotkey(feature.HotkeySection, feature.DefaultHotkey);
    }

    public Hotkey ReadHotkey(string section, Hotkey fallback) => ReadHotkey(section, (Hotkey?)fallback, null) ?? fallback;

    private Hotkey? ReadHotkey(string section, Hotkey? fallback, string? prefix)
    {
        var keyName = prefix ?? "键";
        var key = _document.Get(section, keyName) ?? _defaults.Get(section, keyName);
        if (key is null || !int.TryParse(key, out var code))
        {
            return fallback;
        }

        var p = prefix ?? string.Empty;
        var modifiers = int.TryParse(_document.Get(section, p + "功能键") ?? _defaults.Get(section, p + "功能键"), out var m) ? m : 0;
        var secondary = int.TryParse(_document.Get(section, p + "副键") ?? _defaults.Get(section, p + "副键"), out var s) ? s : 0;
        return new Hotkey(code, (ModifierMask)(modifiers & 7), secondary);
    }

    public void SetHotkey(FeatureDefinition feature, Hotkey hotkey)
    {
        if (feature.HotkeySection is null)
        {
            return;
        }

        var p = feature.HotkeyKey ?? string.Empty;
        Set(feature.HotkeySection, feature.HotkeyKey ?? "键", hotkey.Key.ToString(CultureInfo.InvariantCulture));
        Set(feature.HotkeySection, p + "功能键", ((int)hotkey.Modifiers).ToString(CultureInfo.InvariantCulture));
        Set(feature.HotkeySection, p + "副键", hotkey.Secondary.ToString(CultureInfo.InvariantCulture));
    }

    public void ResetSection(string section)
    {
        var existing = _document.Find(section);
        if (existing is null)
        {
            return;
        }

        foreach (var (key, _) in existing.Entries)
        {
            _document.Remove(section, key);
        }

        _cache.Clear();
        Changed?.Invoke(this, new SettingChangedEventArgs(section, string.Empty, string.Empty));
    }

    public void Save()
    {
        if (_path is null)
        {
            return;
        }

        lock (_saveGate)
        {
            _document.Save(_path);
        }
    }
}

public sealed class SettingChangedEventArgs(string section, string key, string value) : EventArgs
{
    public string Section { get; } = section;

    public string Key { get; } = key;

    public string Value { get; } = value;
}
