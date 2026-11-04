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
            created.Set("RegistryBackup", "ACP", "65001");
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
        var value = _document.Get(section, key) ?? _defaults.Get(section, key);
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
        Changed?.Invoke(this, new SettingChangedEventArgs(section, key, value));
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

        return ReadHotkey(feature.HotkeySection, feature.DefaultHotkey);
    }

    public Hotkey ReadHotkey(string section, Hotkey fallback)
    {
        var key = _document.Get(section, "键") ?? _defaults.Get(section, "键");
        if (key is null || !int.TryParse(key, out var code))
        {
            return fallback;
        }

        var modifiers = int.TryParse(_document.Get(section, "功能键") ?? _defaults.Get(section, "功能键"), out var m) ? m : 0;
        var secondary = int.TryParse(_document.Get(section, "副键") ?? _defaults.Get(section, "副键"), out var s) ? s : 0;
        return new Hotkey(code, (ModifierMask)(modifiers & 7), secondary);
    }

    public void SetHotkey(FeatureDefinition feature, Hotkey hotkey)
    {
        if (feature.HotkeySection is null)
        {
            return;
        }

        Set(feature.HotkeySection, "键", hotkey.Key.ToString(CultureInfo.InvariantCulture));
        Set(feature.HotkeySection, "功能键", ((int)hotkey.Modifiers).ToString(CultureInfo.InvariantCulture));
        Set(feature.HotkeySection, "副键", hotkey.Secondary.ToString(CultureInfo.InvariantCulture));
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
