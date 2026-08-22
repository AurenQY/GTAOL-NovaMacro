namespace NovaMacro.Core;

public sealed class MacroRunner
{
    private readonly IKeyboardOutput _keyboardOutput;
    private readonly IDelay _delay;
    private int _isRunning;

    public MacroRunner(IKeyboardOutput keyboardOutput, IDelay delay)
    {
        ArgumentNullException.ThrowIfNull(keyboardOutput);
        ArgumentNullException.ThrowIfNull(delay);

        _keyboardOutput = keyboardOutput;
        _delay = delay;
    }

    public async Task<MacroRunResult> RunAsync(
        MacroSequence sequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return MacroRunResult.AlreadyRunning;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (MacroStep step in sequence.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (step)
                {
                    case KeyTapStep keyTap:
                        TapKey(keyTap.Key);
                        break;

                    case DelayStep delay:
                        await _delay
                            .WaitAsync(delay.Duration, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    default:
                        throw new NotSupportedException(
                            $"Macro step type '{step.GetType().FullName}' is not supported.");
                }
            }

            return MacroRunResult.Completed;
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private void TapKey(KeyboardKey key)
    {
        bool keyDownSucceeded = false;

        try
        {
            _keyboardOutput.KeyDown(key);
            keyDownSucceeded = true;
        }
        finally
        {
            if (keyDownSucceeded)
            {
                _keyboardOutput.KeyUp(key);
            }
        }
    }
}
