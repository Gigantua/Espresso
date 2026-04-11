namespace EspressoCS;

public static class Globals
{
    // Timing index constants (TIME_COUNT slots)
    public const int ReadTime     = 0;
    public const int ComplTime    = 1;
    public const int OnsetTime    = 2;
    public const int EssenTime    = 3;
    public const int ExpandTime   = 4;
    public const int IrredTime    = 5;
    public const int ReduceTime   = 6;
    public const int GexpandTime  = 7;
    public const int GirredTime   = 8;
    public const int GreduceTime  = 9;
    public const int PrimesTime   = 10;
    public const int MincovTime   = 11;
    public const int MvReduceTime = 12;
    public const int RaiseInTime  = 13;
    public const int VerifyTime   = 14;
    public const int WriteTime    = 15;
    public const int FccTime      = 16;
    public const int EtrTime      = 17;
    public const int EtrAuxTime   = 18;
    public const int SigmaTime    = 19;
    public const int UcompTime    = 20;
    public const int BwTime       = 21;

    // Debug
    public static uint   Debug;
    public static bool   VerboseDebug;

    // Timing arrays
    public static string?[] TotalName  = new string?[EspressoConstants.TimeCount];
    public static long[]    TotalTime  = new long[EspressoConstants.TimeCount];
    public static int[]     TotalCalls = new int[EspressoConstants.TimeCount];

    // Boolean flags
    public static bool EchoComments;
    public static bool EchoUnknownCommands;
    public static bool ForceIrredundant;
    public static bool SkipMakeSparse;
    public static bool Kiss;
    public static bool Pos;
    public static bool PrintSolution;
    public static bool RecomputeOnset;
    public static bool RemoveEssential;
    public static bool SingleExpand;
    public static bool Summary;
    public static bool Trace;
    public static bool UnwrapOnset;
    public static bool UseRandomOrder;
    public static bool UseSuperGasp;
    public static string? Filename;
    public static bool DebugExactMinimization;
}
