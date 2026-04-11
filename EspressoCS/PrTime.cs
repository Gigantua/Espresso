namespace EspressoCS;

/// <summary>
/// Print time functions for formatting and displaying timing information.
/// Converts milliseconds to human-readable time strings.
/// </summary>
public static class PrTime
{
    /// <summary>
    /// Format a time interval in milliseconds as a string "X.XX sec".
    /// </summary>
    private static string PTime(long milliseconds)
    {
        long seconds = milliseconds / 1000;
        long centiseconds = (milliseconds % 1000) / 10;
        return $"{seconds}.{centiseconds:D2} sec";
    }

    /// <summary>
    /// Print a time value with its associated name.
    /// Output goes to console if not in trace mode.
    /// </summary>
    public static void PrintTimeValue(string name, long milliseconds)
    {
        if (!Globals.Trace)
        {
            Console.WriteLine($"{name}: {PTime(milliseconds)}");
        }
    }

    /// <summary>
    /// Print time elapsed between two measurements.
    /// </summary>
    public static void PrTimeStep(string name, long startTime, long endTime)
    {
        long elapsed = endTime - startTime;
        if (elapsed < 0)
        {
            elapsed = 0;
        }
        
        if (!Globals.Trace)
        {
            Console.WriteLine($"{name}: {PTime(elapsed)}");
        }
    }

    /// <summary>
    /// Print the accumulated time for a specific operation.
    /// </summary>
    public static void PrintTime(int timeIndex)
    {
        if (Globals.TotalName[timeIndex] != null && !Globals.Trace)
        {
            long totalMs = Globals.TotalTime[timeIndex];
            int calls = Globals.TotalCalls[timeIndex];
            
            if (calls > 0)
            {
                Console.WriteLine($"{Globals.TotalName[timeIndex]}: {PTime(totalMs)} ({calls} calls)");
            }
        }
    }

    /// <summary>
    /// Print all accumulated timing data.
    /// </summary>
    public static void PrintTotalTime()
    {
        Console.Error.WriteLine("\nTiming Summary:");
        
        for (int i = 0; i < EspressoConstants.TimeCount; i++)
        {
            if (Globals.TotalName[i] != null)
            {
                long totalMs = Globals.TotalTime[i];
                int calls = Globals.TotalCalls[i];
                
                if (calls > 0)
                {
                    Console.Error.WriteLine($"  {Globals.TotalName[i]}: {PTime(totalMs)} ({calls} calls)");
                }
            }
        }
    }
}
