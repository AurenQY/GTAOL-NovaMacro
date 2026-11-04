using NovaMacro.Core.Input;
using NovaMacro.Core.Macros;
using NovaMacro.Core.Runtime;
using NovaMacro.Core.Scripting;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Features;

/// <summary>Runs one user-defined script macro (自定义宏).</summary>
public sealed class CustomMacroFeature : MacroFeature
{
    private readonly IReadOnlyList<ScriptCommand> _commands;
    private volatile bool _toggled;

    public CustomMacroFeature(CustomMacroDefinition definition)
        : base(new FeatureDefinition(FeatureId.CustomMacro, definition.Name, FeatureGroup.Macro, null, definition.Enabled, null, definition.Hotkey, "自定义宏"))
    {
        Macro = definition;
        try
        {
            _commands = MacroScript.Parse(definition.Script);
        }
        catch (ScriptParseException ex)
        {
            ParseError = ex.Message;
            _commands = Array.Empty<ScriptCommand>();
        }
    }

    public CustomMacroDefinition Macro { get; }

    public string? ParseError { get; }

    public override Hotkey? CustomHotkey => Macro.Hotkey;

    public override bool? CustomEnabled => Macro.Enabled && ParseError is null;

    public override bool BypassLock => Macro.Unlocked;

    public override void OnHotkeyDown()
    {
        if (Macro.Trigger == CustomTrigger.Toggle)
        {
            if (_toggled)
            {
                _toggled = false;
                Cancel();
                S.Notifier.Tip($"[自定义宏] “{Macro.Name}”已停止");
                return;
            }

            _toggled = true;
        }

        Start(ctx =>
        {
            ctx.Services.Notifier.Log($"[自定义宏] 正在执行“{Macro.Name}”");
            do
            {
                Execute(ctx, _commands);
            }
            while (Macro.Loop && ((Macro.Trigger == CustomTrigger.Hold && HotkeyHeld) || (Macro.Trigger == CustomTrigger.Toggle && _toggled)));

            ctx.Services.Notifier.Log($"[自定义宏] “{Macro.Name}”执行完毕");
        });
    }

    protected override void OnStopped() => _toggled = false;

    public static void Execute(MacroContext ctx, IReadOnlyList<ScriptCommand> commands)
    {
        foreach (var c in commands)
        {
            ctx.Token.ThrowIfCancellationRequested();
            switch (c.Kind)
            {
                case ScriptCommandKind.Key:
                case ScriptCommandKind.Mouse:
                case ScriptCommandKind.GameKey:
                    var code = c.Kind == ScriptCommandKind.GameKey ? ctx.Input.Code(c.Action) : c.Code;
                    for (var i = 0; i < c.Times; i++)
                    {
                        switch (c.KeyAction)
                        {
                            case KeyAction.Down:
                                ctx.Input.DownCode(code);
                                ctx.Wait(c.Delay);
                                break;
                            case KeyAction.Up:
                                ctx.Input.UpCode(code);
                                ctx.Wait(c.Delay);
                                break;
                            default:
                                ctx.Tap(code, new KeyTiming(c.Interval, c.Delay));
                                break;
                        }
                    }

                    break;
                case ScriptCommandKind.Move:
                    var b = ctx.Services.Game.ClientBounds;
                    ctx.Input.Output.MoveTo(b.X + c.X, b.Y + c.Y);
                    break;
                case ScriptCommandKind.Wait:
                    ctx.Wait(c.Milliseconds);
                    break;
                case ScriptCommandKind.Path:
                    MenuRunner.Run(ctx, MenuPath.Parse(c.Text ?? string.Empty, n => MenuRunner.Placeholder(ctx.Settings, n)));
                    break;
                case ScriptCommandKind.Position:
                    MenuRunner.Run(ctx, MenuPath.Parse("M,↓{生命值与弹药位置}", n => MenuRunner.Placeholder(ctx.Settings, n)));
                    break;
            }
        }
    }
}
