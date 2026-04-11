namespace EspressoCS;

using static SetOps;
using static SetFamily;
using static CubeContext;

/// <summary>
/// Map — mapping and permutation utilities for Karnaugh map display and symbolic I/O.
/// Translated from map.c
/// </summary>
public static class Map
{
    // -----------------------------------------------------------------------
    // minterms — convert a cover to a minterm set
    // -----------------------------------------------------------------------

    private static PSet Gminterms;
    private static PSet GcubeStatic;

    /// <summary>
    /// Minterms — expand a set of cubes into explicit minterms.
    /// Returns a PSet where each element represents a minterm.
    /// </summary>
    public static PSet Minterms(SetFamily T)
    {
        int size = 1;
        for (int var = 0; var < NumVars; var++)
            size *= PartSize![var];

        Gminterms = new PSet(size);
        SetClear(Gminterms, size);

        for (int i = 0; i < T.Count; i++)
        {
            GcubeStatic = T.GetSet(i);
            Explode(NumVars - 1, 0);
        }

        return Gminterms;
    }

    /// <summary>Explode — recursively expand a cube into individual minterms.</summary>
    private static void Explode(int var, int z)
    {
        int i, last = LastPart![var];
        for (i = FirstPart![var], z *= PartSize![var]; i <= last; i++, z++)
        {
            if (IsInSet(GcubeStatic, i))
            {
                if (var == 0)
                    SetInsert(Gminterms, z);
                else
                    Explode(var - 1, z);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Karnaugh map display index
    // -----------------------------------------------------------------------

    private static readonly int[,] MapIndex = new int[,]
    {
        {0,  1,  3,  2,   16, 17, 19, 18,      80, 81, 83, 82,   64, 65, 67, 66},
        {4,  5,  7,  6,   20, 21, 23, 22,      84, 85, 87, 86,   68, 69, 71, 70},
        {12, 13, 15, 14,   28, 29, 31, 30,      92, 93, 95, 94,   76, 77, 79, 78},
        {8,  9, 11, 10,   24, 25, 27, 26,      88, 89, 91, 90,   72, 73, 75, 74},
        {32, 33, 35, 34,   48, 49, 51, 50,     112,113,115,114,   96, 97, 99, 98},
        {36, 37, 39, 38,   52, 53, 55, 54,     116,117,119,118,  100,101,103,102},
        {44, 45, 47, 46,   60, 61, 63, 62,     124,125,127,126,  108,109,111,110},
        {40, 41, 43, 42,   56, 57, 59, 58,     120,121,123,122,  104,105,107,106},
        {160,161,163,162,  176,177,179,178,     240,241,243,242,  224,225,227,226},
        {164,165,167,166,  180,181,183,182,     244,245,247,246,  228,229,231,230},
        {172,173,175,174,  188,189,191,190,     252,253,255,254,  236,237,239,238},
        {168,169,171,170,  184,185,187,186,     248,249,251,250,  232,233,235,234},
        {128,129,131,130,  144,145,147,146,     208,209,211,210,  192,193,195,194},
        {132,133,135,134,  148,149,151,150,     212,213,215,214,  196,197,199,198},
        {140,141,143,142,  156,157,159,158,     220,221,223,222,  204,205,207,206},
        {136,137,139,138,  152,153,155,154,     216,217,219,218,  200,201,203,202}
    };

    /// <summary>MapSymbolic — display a Karnaugh map of a cover (if applicable).</summary>
    public static void MapSymbolic(Pla pla)
    {
        // TODO: implement symbolic mapping from input variables
    }

    /// <summary>MapOutputSymbolic — display output mappings for symbolic outputs.</summary>
    public static void MapOutputSymbolic(Pla pla)
    {
        // TODO: implement symbolic output mapping
    }

    /// <summary>
    /// MapDisplay — display a Karnaugh map for a set family.
    /// Works for binary inputs and outputs.
    /// </summary>
    public static void MapDisplay(SetFamily T)
    {
        var m = Minterms(T);
        int largestInputInd = 1 << NumBinaryVars;
        int numout = PartSize![NumVars - 1];

        for (int outnum = 0; outnum < numout; outnum++)
        {
            int output_offset = outnum * largestInputInd;
            Console.WriteLine($"\n\nOutput space # {outnum}");

            for (int l = 0; l <= System.Math.Max(NumBinaryVars - 8, 0); l++)
            {
                int other_input_offset = l * 256;

                for (int k = 0; k < 16; k++)
                {
                    bool some_output = false;

                    for (int j = 0; j < 16; j++)
                    {
                        int ind = MapIndex[k, j] + other_input_offset;
                        if (ind < largestInputInd)
                        {
                            char c = IsInSet(m, ind + output_offset) ? '1' : '.';
                            Console.Write(c);
                            some_output = true;
                        }

                        if ((j + 1) % 4 == 0)
                            Console.Write(' ');
                        if ((j + 1) % 8 == 0)
                            Console.Write("  ");
                    }

                    if (some_output)
                        Console.WriteLine();

                    if ((k + 1) % 4 == 0)
                    {
                        if (k != 15 && MapIndex[k + 1, 0] >= largestInputInd)
                            break;
                        Console.WriteLine();
                    }

                    if ((k + 1) % 8 == 0)
                        Console.WriteLine();
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper bit operations
    // -----------------------------------------------------------------------

    /// <summary>IsInSet — check if an element is in a set.</summary>
    private static bool IsInSet(PSet s, int e)
    {
        int word = WhichWord(e);
        int bit = WhichBit(e);
        return ((s[word] >> bit) & 1u) != 0;
    }

    /// <summary>SetInsert — insert an element into a set.</summary>
    private static void SetInsert(PSet s, int e)
    {
        int word = WhichWord(e);
        int bit = WhichBit(e);
        s[word] |= (1u << bit);
    }
}
