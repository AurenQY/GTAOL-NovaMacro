using System.Diagnostics;
using NovaMacro.Core.Features;
using NovaMacro.Core.Input;
using NovaMacro.Core.Settings;

namespace NovaMacro.Core.Runtime;

/// <summary>
/// Routes physical input to features: hotkey chord matching, gestures, foreground/lock gating,
/// global stop (一键停止) and lock (长按锁定).
/// </summary>
public sealed class MacroRuntime
{
    private readonly object _gate = new();
    private readonly MacroServices _services;
    private readonly List<MacroFeature> _features = new();
    private readonly Dictionary<MacroFeature, ClickClassifier> _classifiers = new();
    private readonly HashSet<int> _suppressedDown = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ClickClassifier _stopClassifier = new(250, 800);
    private CancellationTokenSource _stopCts = new();
    private Snapshot[] _snapshot = Array.Empty<Snapshot>();
    private Hotkey _stopHotkey;
    private bool _locked;

    public MacroRuntime(MacroServices services, IEnumerable<MacroFeature> features)
    {
        _services = services;
        foreach (var feature in features)
        {
            AddFeatureUnlocked(feature);
        }

        services.Settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public event EventHandler? LockChanged;

    public event EventHandler? Stopped;

    public IReadOnlyList<MacroFeature> Features
    {
        get
        {
            lock (_gate)
            {
                return _features.ToArray();
            }
        }
    }

    public bool IsLocked
    {
        get
        {
            lock (_gate)
            {
                return _locked;
            }
        }
    }

    public CancellationToken StopToken
    {
        get
        {
            lock (_gate)
            {
                return _stopCts.Token;
            }
        }
    }

    public long Now => _clock.ElapsedMilliseconds;

    public T? Get<T>()
        where T : MacroFeature
    {
        lock (_gate)
        {
            return _features.OfType<T>().FirstOrDefault();
        }
    }

    public void ReplaceFeatures(Func<MacroFeature, bool> remove, IEnumerable<MacroFeature> add)
    {
        lock (_gate)
        {
            foreach (var feature in _features.Where(remove).ToArray())
            {
                feature.Cancel();
                _features.Remove(feature);
                _classifiers.Remove(feature);
            }

            foreach (var feature in add)
            {
                AddFeatureUnlocked(feature);
            }
        }

        Refresh();
    }

    /// <summary>Re-reads hotkeys and enabled states from settings.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _stopHotkey = _services.Settings.GetHotkey(FeatureCatalog.Get(FeatureId.StopAll));
            var previous = _snapshot.ToDictionary(s => s.Feature, s => s.Enabled);
            _snapshot = _features.Select(f => new Snapshot(
                f,
                f.CustomHotkey ?? _services.Settings.GetHotkey(f.Definition),
                f.CustomEnabled ?? IsEnabled(f.Definition))).ToArray();
            foreach (var s in _snapshot)
            {
                if (previous.TryGetValue(s.Feature, out var was) && was != s.Enabled)
                {
                    s.Feature.OnEnabledChanged(s.Enabled);
                }
            }
        }
    }

    public bool IsEnabled(FeatureDefinition definition)
    {
        if (definition.Group == FeatureGroup.Tool)
        {
            return _services.Settings.GetBool(Sec.Tools, "启用" + definition.Name + "热键", false);
        }

        return _services.Settings.IsFeatureOn(definition);
    }

    /// <summary>Handles one physical input event. Returns true when the event should be swallowed.</summary>
    public bool HandleInput(int code, bool isDown, bool isRepeat = false)
    {
        var now = Now;
        var args = new InputEventArgs(code, isDown, now, isRepeat);
        Snapshot[] snapshot;
        bool locked;
        lock (_gate)
        {
            snapshot = _snapshot;
            locked = _locked;
        }

        var suppress = false;

        // 一键停止 / 锁定
        if (!_stopHotkey.IsEmpty && code == _stopHotkey.Key && !isRepeat)
        {
            if (isDown && ChordHeld(_stopHotkey))
            {
                lock (_gate)
                {
                    _stopClassifier.Down(now);
                }
            }
            else if (!isDown)
            {
                IEnumerable<PressGesture> gestures;
                lock (_gate)
                {
                    gestures = _stopClassifier.Up(now).ToArray();
                }

                _ = gestures;
            }
        }

        var gameForeground = _services.Game.IsForeground;
        var imeBlocked = _services.Settings.GetBool(Sec.Switches, "输入法检测防误触") && gameForeground && !_services.Game.IsUsKeyboardLayout;
        var whitelist = _services.Settings.GetString(Sec.StopMacro, "宏锁定白名单");

        foreach (var s in snapshot)
        {
            if (!s.Enabled)
            {
                continue;
            }

            var feature = s.Feature;
            var allowed = (gameForeground || feature.WorksInBackground) && !imeBlocked &&
                          (!locked || feature.BypassLock || IsWhitelisted(whitelist, feature.Definition.Name) || feature.Definition.Group == FeatureGroup.Tool);
            if (!allowed)
            {
                if (!isDown && feature.HotkeyHeld && code == s.Hotkey.Key)
                {
                    feature.HotkeyHeld = false;
                    SafeInvoke(feature, feature.OnHotkeyUp);
                }

                continue;
            }

            try
            {
                suppress |= feature.OnInput(args);
            }
            catch (Exception ex)
            {
                _services.Notifier.Log($"[{feature.Definition.Name}] 输入处理出错：{ex.Message}");
            }
        }

        // Hotkey chords: the most specific (most modifiers) match wins.
        if (!isRepeat)
        {
            if (isDown)
            {
                var candidates = snapshot.Where(s => s.Enabled && !s.Hotkey.IsEmpty && s.Hotkey.Key == code && ChordHeld(s.Hotkey)).ToArray();
                if (candidates.Length > 0)
                {
                    var best = candidates.Max(c => Specificity(c.Hotkey));
                    foreach (var s in candidates.Where(c => Specificity(c.Hotkey) == best))
                    {
                        var feature = s.Feature;
                        if (!((gameForeground || feature.WorksInBackground) && !imeBlocked &&
                              (!locked || feature.BypassLock || IsWhitelisted(whitelist, feature.Definition.Name) || feature.Definition.Group == FeatureGroup.Tool)))
                        {
                            continue;
                        }

                        feature.HotkeyHeld = true;
                        if (s.Hotkey.Modifiers != ModifierMask.None)
                        {
                            suppress = true;
                            lock (_gate)
                            {
                                _suppressedDown.Add(code);
                            }
                        }

                        if (feature.UsesGestures)
                        {
                            lock (_gate)
                            {
                                Classifier(feature).Down(now);
                            }
                        }

                        SafeInvoke(feature, feature.OnHotkeyDown);
                    }
                }
            }
            else
            {
                foreach (var s in snapshot.Where(s => s.Hotkey.Key == code && s.Feature.HotkeyHeld))
                {
                    var feature = s.Feature;
                    feature.HotkeyHeld = false;
                    if (feature.UsesGestures)
                    {
                        PressGesture[] gestures;
                        lock (_gate)
                        {
                            gestures = Classifier(feature).Up(now).ToArray();
                        }

                        foreach (var g in gestures)
                        {
                            var gesture = g;
                            SafeInvoke(feature, () => feature.OnGesture(gesture));
                        }
                    }

                    SafeInvoke(feature, feature.OnHotkeyUp);
                }

                lock (_gate)
                {
                    if (_suppressedDown.Remove(code))
                    {
                        suppress = true;
                    }
                }
            }
        }
        else
        {
            lock (_gate)
            {
                if (_suppressedDown.Contains(code))
                {
                    suppress = true;
                }
            }
        }

        return suppress;
    }

    /// <summary>Periodic processing (gestures, AFK timers, stop-key gestures).</summary>
    public void Tick()
    {
        var now = Now;
        Snapshot[] snapshot;
        PressGesture[] stopGestures;
        lock (_gate)
        {
            snapshot = _snapshot;
            stopGestures = _stopClassifier.Poll(now).ToArray();
        }

        foreach (var g in stopGestures)
        {
            if (g == PressGesture.LongPress)
            {
                ToggleLock();
            }
            else
            {
                StopAll();
            }
        }

        foreach (var s in snapshot)
        {
            if (s.Feature.UsesGestures)
            {
                PressGesture[] gestures;
                lock (_gate)
                {
                    gestures = Classifier(s.Feature).Poll(now).ToArray();
                }

                foreach (var g in gestures)
                {
                    var gesture = g;
                    SafeInvoke(s.Feature, () => s.Feature.OnGesture(gesture));
                }
            }

            if (s.Enabled)
            {
                SafeInvoke(s.Feature, () => s.Feature.OnTick(now));
            }
        }
    }

    /// <summary>Cancels every running macro and releases all synthesized input.</summary>
    public void StopAll()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            old = _stopCts;
            _stopCts = new CancellationTokenSource();
        }

        old.Cancel();
        foreach (var feature in Features)
        {
            feature.Cancel();
        }

        _services.Input.Output.ReleaseAll();
        old.Dispose();
        Notify("你停止了按键宏操作！", on: false);
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleLock() => SetLocked(!IsLocked);

    public void SetLocked(bool locked)
    {
        lock (_gate)
        {
            if (_locked == locked)
            {
                return;
            }

            _locked = locked;
        }

        Notify(locked ? "你锁定了按键宏！" : "你解锁了按键宏！", on: !locked);
        LockChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsWhitelisted(string whitelist, string name) =>
        whitelist.Split(',', '，', '|').Any(w => w.Trim() == name);

    private static int Specificity(Hotkey h) =>
        (h.Modifiers.HasFlag(ModifierMask.Ctrl) ? 1 : 0) + (h.Modifiers.HasFlag(ModifierMask.Shift) ? 1 : 0) +
        (h.Modifiers.HasFlag(ModifierMask.Alt) ? 1 : 0) + (h.Secondary != InputCodes.None ? 1 : 0);

    private bool ChordHeld(Hotkey h)
    {
        var state = _services.InputState;
        if (h.Secondary != InputCodes.None && !state.IsDown(h.Secondary))
        {
            return false;
        }

        if (h.Modifiers == ModifierMask.None)
        {
            // Unmodified hotkeys still fire while Shift is held (sprinting), but not with Ctrl/Alt chords.
            return !state.IsModifierDown(ModifierMask.Ctrl) && !state.IsModifierDown(ModifierMask.Alt) ||
                   InputCodes.IsMouseButton(h.Key) || InputCodes.IsGamepad(h.Key);
        }

        foreach (var flag in new[] { ModifierMask.Ctrl, ModifierMask.Shift, ModifierMask.Alt })
        {
            var required = h.Modifiers.HasFlag(flag);
            if (required != state.IsModifierDown(flag) && (required || flag != ModifierMask.Shift))
            {
                return false;
            }
        }

        return true;
    }

    private void Notify(string message, bool on)
    {
        _services.Notifier.Log(message);
        if (_services.Settings.GetBool(Sec.StopMacro, "改为蜂鸣提示"))
        {
            _services.Notifier.Beep(on);
        }
        else
        {
            _services.Notifier.Tip(message);
        }
    }

    private ClickClassifier Classifier(MacroFeature feature)
    {
        if (!_classifiers.TryGetValue(feature, out var classifier))
        {
            classifier = new ClickClassifier();
            _classifiers[feature] = classifier;
        }

        return classifier;
    }

    private void SafeInvoke(MacroFeature feature, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _services.Notifier.Log($"[{feature.Definition.Name}] 出错：{ex.Message}");
        }
    }

    private void AddFeatureUnlocked(MacroFeature feature)
    {
        _features.Add(feature);
        feature.Attach(this, _services);
    }

    private sealed record Snapshot(MacroFeature Feature, Hotkey Hotkey, bool Enabled);
}
