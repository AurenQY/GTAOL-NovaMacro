using System.Globalization;
using System.Text;
using System.Xml.Linq;
using NovaMacro.Core.Input;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Scripting;

public enum CustomTrigger
{
    /// <summary>点击 — run once per press.</summary>
    Press,

    /// <summary>按住 — repeat while held (loop) or run once.</summary>
    Hold,

    /// <summary>开关 — first press starts looping, next press stops.</summary>
    Toggle,
}

/// <summary>A custom macro stored as XML under CustomMacro\&lt;分类&gt;\&lt;名称&gt;.xml.</summary>
public sealed class CustomMacroDefinition
{
    public string Name { get; set; } = "新的自定义宏";

    /// <summary>Category folder relative to CustomMacro (empty = 根目录).</summary>
    public string Category { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public Hotkey Hotkey { get; set; } = Hotkey.None;

    public bool Loop { get; set; }

    /// <summary>Still works while macros are locked (宏锁定).</summary>
    public bool Unlocked { get; set; }

    public CustomTrigger Trigger { get; set; } = CustomTrigger.Press;

    public string Script { get; set; } = string.Empty;

    public static string RootFolder(string dataFolder) => Path.Combine(dataFolder, "CustomMacro");

    public string FilePath(string dataFolder) =>
        Path.Combine(RootFolder(dataFolder), Category, SanitizeFileName(Name) + ".xml");

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '[' }).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "未命名" : cleaned;
    }

    public XDocument ToXml() => new(
        new XElement("macro",
            new XElement("enable", SettingValues.FromBool(Enabled)),
            new XElement("hotkey", string.Create(CultureInfo.InvariantCulture, $"{Hotkey.Key},{(int)Hotkey.Modifiers},{Hotkey.Secondary}")),
            new XElement("loop", SettingValues.FromBool(Loop)),
            new XElement("unlocked", SettingValues.FromBool(Unlocked)),
            new XElement("trigger", Trigger switch { CustomTrigger.Hold => "按住", CustomTrigger.Toggle => "开关", _ => "点击" }),
            new XElement("script", new XCData(Script))));

    public static CustomMacroDefinition FromXml(XDocument doc, string name, string category)
    {
        var root = doc.Root ?? throw new FormatException("空的自定义宏文件");
        var def = new CustomMacroDefinition
        {
            Name = name,
            Category = category,
            Enabled = SettingValues.ToBool(root.Element("enable")?.Value, true),
            Loop = SettingValues.ToBool(root.Element("loop")?.Value),
            Unlocked = SettingValues.ToBool(root.Element("unlocked")?.Value),
            Script = root.Element("script")?.Value ?? string.Empty,
            Trigger = (root.Element("trigger")?.Value ?? string.Empty).Trim() switch
            {
                "按住" or "Hold" => CustomTrigger.Hold,
                "开关" or "Toggle" => CustomTrigger.Toggle,
                _ => CustomTrigger.Press,
            },
        };

        var hk = (root.Element("hotkey")?.Value ?? string.Empty).Split(',');
        if (hk.Length >= 1 && int.TryParse(hk[0], out var key))
        {
            var mods = hk.Length > 1 && int.TryParse(hk[1], out var m) ? m : 0;
            var sec = hk.Length > 2 && int.TryParse(hk[2], out var s) ? s : 0;
            def.Hotkey = new Hotkey(key, (ModifierMask)(mods & 7), sec);
        }

        return def;
    }

    public void Save(string dataFolder)
    {
        var path = FilePath(dataFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        ToXml().Save(writer);
    }

    public static IReadOnlyList<CustomMacroDefinition> LoadAll(string dataFolder, Action<string>? onError = null)
    {
        var root = RootFolder(dataFolder);
        var list = new List<CustomMacroDefinition>();
        if (!Directory.Exists(root))
        {
            return list;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var category = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                if (category == ".")
                {
                    category = string.Empty;
                }

                list.Add(FromXml(XDocument.Load(file), Path.GetFileNameWithoutExtension(file), category));
            }
            catch (Exception ex) when (ex is IOException or FormatException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                onError?.Invoke($"[自定义宏] 读取失败：{file} | {ex.Message}");
            }
        }

        return list;
    }

    /// <summary>The user's previous NovaMacro presets (CEO abilities) shipped as starter custom macros.</summary>
    public static IReadOnlyList<CustomMacroDefinition> Starters { get; } = new[]
    {
        new CustomMacroDefinition
        {
            Name = "丢牛鲨睾酮",
            Category = "CEO技能",
            Enabled = false,
            Hotkey = new Hotkey(InputCodes.Letter('E'), ModifierMask.Alt),
            Script = "// 老大技能 → 牛鲨睾酮\n路径 M,等待200,确认,↓4,确认,↓,确认\n",
        },
        new CustomMacroDefinition
        {
            Name = "呼叫麻雀直升机",
            Category = "载具",
            Enabled = false,
            Hotkey = new Hotkey(InputCodes.Letter('H'), ModifierMask.Ctrl | ModifierMask.Alt),
            Script = "// 服务 → 虎鲸服务 → 麻雀\n路径 M,等待200,↓3,确认,↓4,确认,↓,确认\n",
        },
        new CustomMacroDefinition
        {
            Name = "示例-连按E",
            Category = string.Empty,
            Enabled = false,
            Hotkey = new Hotkey(InputCodes.Letter('E'), ModifierMask.Ctrl),
            Trigger = CustomTrigger.Hold,
            Loop = true,
            Script = "键 E键 点击 间隔=30 延迟=30\n等待 20\n",
        },
    };
}
