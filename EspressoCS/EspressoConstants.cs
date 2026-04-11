namespace EspressoCS;

public static class EspressoConstants
{
    public const int    TimeCount = 22;
    public const int    CubeTemp  = 10;   // CUBE_TEMP
    public const string Version   = "UC Berkeley, Espresso Version #2.3, Release date 01/31/88";

    // Debug flags
    public const uint Compl   = 0x0001u;
    public const uint Essen   = 0x0002u;
    public const uint Expand  = 0x0004u;
    public const uint Expand1 = 0x0008u;
    public const uint Gasp    = 0x0010u;
    public const uint Irred   = 0x0020u;
    public const uint Reduce  = 0x0040u;
    public const uint Reduce1 = 0x0080u;
    public const uint Sparse  = 0x0100u;
    public const uint Taut    = 0x0200u;
    public const uint Exact   = 0x0400u;
    public const uint Mincov  = 0x0800u;
    public const uint Mincov1 = 0x1000u;
    public const uint Sharp   = 0x2000u;
    public const uint Irred1  = 0x4000u;
}
