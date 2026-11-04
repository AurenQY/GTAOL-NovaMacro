using NovaMacro.Core.Features;
using NovaMacro.Core.Input;

namespace NovaMacro.Core.Runtime;

/// <summary>Base class for every automation feature.</summary>
public abstract class MacroFeature
{
    private readonly object _runGate = new();
    private CancellationTokenSource? _cts;
    private Thread? _worker;

    protected MacroFeature(FeatureId id)
        : this(FeatureCatalog.Get(id))
    {
    }

    protected MacroFeature(FeatureDefinition definition)
    {
        Definition = definition;
    }

    public FeatureDefinition Definition { get; }

    /// <summary>Hotkey override for features whose hotkey is not stored in a "…热键" section (custom macros).</summary>
    public virtual Hotkey? CustomHotkey => null;

    /// <summary>Enabled override for features without a [记忆开关] entry (custom macros).</summary>
    public virtual bool? CustomEnabled => null;

    public MacroServices S { get; private set; } = null!;

    internal MacroRuntime Runtime { get; private set; } = null!;

    /// <summary>When true the runtime delivers click/double-click/long-press gestures for the hotkey.</summary>
    public virtual bool UsesGestures => false;

    /// <summary>When true the feature also works while GTA is not the foreground window.</summary>
    public virtual bool WorksInBackground => Definition.Group == FeatureGroup.Tool;

    /// <summary>When true the feature keeps working while macros are locked.</summary>
    public virtual bool BypassLock => false;

    public bool IsRunning
    {
        get
        {
            lock (_runGate)
            {
                return _worker is { IsAlive: true };
            }
        }
    }

    /// <summary>True while the feature hotkey is physically held.</summary>
    public bool HotkeyHeld { get; internal set; }

    internal void Attach(MacroRuntime runtime, MacroServices services)
    {
        Runtime = runtime;
        S = services;
        OnAttached();
    }

    protected virtual void OnAttached()
    {
    }

    public virtual void OnHotkeyDown()
    {
    }

    public virtual void OnHotkeyUp()
    {
    }

    public virtual void OnGesture(PressGesture gesture)
    {
    }

    /// <summary>Raw physical input (hook thread). Return true to swallow the event.</summary>
    public virtual bool OnInput(InputEventArgs e) => false;

    /// <summary>Called roughly every 15 ms on the runtime timer thread.</summary>
    public virtual void OnTick(long now)
    {
    }

    public virtual void OnEnabledChanged(bool enabled)
    {
        if (!enabled)
        {
            Cancel();
        }
    }

    /// <summary>Starts <paramref name="body"/> on a worker thread unless the feature is already running.</summary>
    protected bool Start(Action<MacroContext> body)
    {
        lock (_runGate)
        {
            if (_worker is { IsAlive: true })
            {
                return false;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(Runtime.StopToken);
            _cts = cts;
            var context = new MacroContext(S, Definition, cts.Token);
            _worker = new Thread(() => Execute(body, context, cts))
            {
                IsBackground = true,
                Name = "NovaMacro " + Definition.Name,
                Priority = ThreadPriority.AboveNormal,
            };
            _worker.Start();
            return true;
        }
    }

    /// <summary>Requests cancellation of the running worker.</summary>
    public void Cancel()
    {
        lock (_runGate)
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    protected virtual void OnStopped()
    {
    }

    /// <summary>
    /// Modifiers of the hotkey that started the macro (e.g. Alt of Alt+R) must not be combined with the
    /// injected keys: Alt+Enter toggles full screen and Alt+key produces system-menu beeps. Wait briefly
    /// for the user to release them, then release them for the game if they are still held.
    /// </summary>
    private void ReleaseHotkeyModifiers(MacroContext context)
    {
        var mods = (CustomHotkey ?? S.Settings.GetHotkey(Definition)).Modifiers;
        if (mods == ModifierMask.None || Definition.Group != FeatureGroup.Macro)
        {
            return;
        }

        var flags = new[] { ModifierMask.Ctrl, ModifierMask.Shift, ModifierMask.Alt }.Where(f => mods.HasFlag(f)).ToArray();
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.ElapsedMilliseconds < 600 && flags.Any(f => S.InputState.IsModifierDown(f)))
        {
            context.Wait(-10);
        }

        foreach (var flag in flags.Where(f => S.InputState.IsModifierDown(f)))
        {
            var (left, right) = flag switch
            {
                ModifierMask.Ctrl => (InputCodes.LControl, InputCodes.RControl),
                ModifierMask.Shift => (InputCodes.LShift, InputCodes.RShift),
                _ => (InputCodes.LAlt, InputCodes.RAlt),
            };
            if (flag == ModifierMask.Alt)
            {
                S.Input.Output.SendMenuMask();
            }

            S.Input.Output.Up(left);
            S.Input.Output.Up(right);
        }
    }

    private void Execute(Action<MacroContext> body, MacroContext context, CancellationTokenSource cts)
    {
        S.ExecutionActive?.Invoke(true);
        try
        {
            ReleaseHotkeyModifiers(context);
            body(context);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            S.Notifier.Log($"[{Definition.Name}] 执行出错：{ex.Message}");
        }
        finally
        {
            S.ExecutionActive?.Invoke(false);
            try
            {
                OnStopped();
            }
            catch (Exception ex)
            {
                S.Notifier.Log($"[{Definition.Name}] 结束操作出错：{ex.Message}");
            }

            lock (_runGate)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
            }

            cts.Dispose();
        }
    }
}

public sealed class InputEventArgs
{
    public InputEventArgs(int code, bool isDown, long timestamp, bool repeat)
    {
        Code = code;
        IsDown = isDown;
        Timestamp = timestamp;
        IsRepeat = repeat;
    }

    public int Code { get; }

    public bool IsDown { get; }

    public bool IsRepeat { get; }

    public long Timestamp { get; }
}
