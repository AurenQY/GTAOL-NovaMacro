using System.Runtime.InteropServices;

namespace NovaMacro.Platform.SystemTools;

/// <summary>
/// Raises the process timer resolution to 1 ms only while at least one macro is executing, so key
/// delays such as 25 ms are not rounded up to the default 15.6 ms timer ticks (31 ms). Since
/// Windows 10 2004 this only affects NovaMacro's own timers.
/// </summary>
public static partial class TimerResolution
{
    private static int _active;

    public static void SetActive(bool active)
    {
        if (active)
        {
            if (Interlocked.Increment(ref _active) == 1)
            {
                TimeBeginPeriod(1);
            }
        }
        else if (Interlocked.Decrement(ref _active) == 0)
        {
            TimeEndPeriod(1);
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);
}
