using System.Diagnostics;

namespace EspressoCS;

/// <summary>
/// CPU timing utilities for measuring elapsed processor time.
/// Platform-agnostic implementation using .NET APIs.
/// </summary>
public static class CpuTime
{
    private static long _startTime;

    /// <summary>
    /// Get elapsed processor time in milliseconds since some constant reference.
    /// Uses Environment.TickCount which is available on all platforms.
    /// </summary>
    public static long GetCpuTime()
    {
        // Environment.TickCount: milliseconds elapsed since system startup
        // Platform-independent replacement for getrusage() on Unix and clock() on Windows
        return Environment.TickCount & int.MaxValue;
    }

    /// <summary>
    /// Get current time in milliseconds.
    /// </summary>
    public static long GetMilliSeconds()
    {
        return Environment.TickCount & int.MaxValue;
    }

    /// <summary>
    /// Get total CPU time accumulated (placeholder for compatibility).
    /// </summary>
    public static long GetTotalCpuTime()
    {
        return Environment.TickCount & int.MaxValue;
    }

    /// <summary>
    /// Calculate elapsed time in milliseconds from a reference start time.
    /// </summary>
    public static long CpuTimeElapsed(long startTime)
    {
        long elapsed = (Environment.TickCount & int.MaxValue) - startTime;
        return elapsed >= 0 ? elapsed : elapsed + int.MaxValue;
    }

    /// <summary>
    /// Reset the timing reference point.
    /// </summary>
    public static void ResetTime()
    {
        _startTime = Environment.TickCount & int.MaxValue;
    }
}
