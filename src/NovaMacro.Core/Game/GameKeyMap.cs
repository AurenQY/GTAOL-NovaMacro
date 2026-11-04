using NovaMacro.Core.Input;

namespace NovaMacro.Core.Game;

/// <summary>Resolved GTA bindings: GTA control file values with user overrides on top.</summary>
public sealed class GameKeyMap
{
    private readonly Dictionary<GameAction, int> _codes = new();

    public GameKeyMap()
    {
        foreach (var info in GameActions.All)
        {
            _codes[info.Action] = info.DefaultCode;
        }
    }

    /// <summary>Where the bindings were read from (empty when defaults are used).</summary>
    public string Source { get; private set; } = string.Empty;

    public int this[GameAction action]
    {
        get => _codes.TryGetValue(action, out var code) ? code : GameActions.Info(action).DefaultCode;
        set => _codes[action] = value;
    }

    public IReadOnlyDictionary<GameAction, int> Codes => _codes;

    public void Apply(IReadOnlyDictionary<GameAction, int> bindings, string source)
    {
        foreach (var (action, code) in bindings)
        {
            if (code != InputCodes.None)
            {
                _codes[action] = code;
            }
        }

        Source = source;
    }

    public string Describe()
    {
        var lines = GameActions.All.Select(a => $"{a.DisplayName} | 键值：{KeyNames.GetName(this[a.Action])}");
        return "读取的游戏主键位：{\r\n" + string.Join("\r\n", lines) + "\r\n}" +
               (Source.Length > 0 ? "\r\n读取于文件夹：" + Source : string.Empty);
    }
}
