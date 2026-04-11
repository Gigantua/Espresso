namespace EspressoCS;

/// <summary>Mirrors PLA_t / pPLA from espresso.h.</summary>
public class Pla
{
    // PLA type constants  (pla_type field flags)
    public const int FType                   = 1;
    public const int DType                   = 2;
    public const int RType                   = 4;
    public const int PleasureType            = 8;
    public const int EqntottType             = 16;
    public const int KissType                = 128;
    public const int ConstraintsType         = 256;
    public const int SymbolicConstraintsType = 512;
    public const int FdType                  = FType | DType;
    public const int FrType                  = FType | RType;
    public const int DrType                  = DType | RType;
    public const int FdrType                 = FType | DType | RType;

    public SetFamily? F;                // on-set
    public SetFamily? D;                // dc-set
    public SetFamily? R;                // off-set
    public string?    Filename;
    public int        PlaType;
    public PSet       Phase;            // pcube phase
    public Pair?      PairData;         // ppair pair
    public string?[]? Label;            // char **label
    public Symbolic?  SymbolicData;     // symbolic_t *symbolic
    public Symbolic?  SymbolicOutput;   // symbolic_t *symbolic_output

    // -----------------------------------------------------------------------
    // new_PLA  (cvrin.c)
    // -----------------------------------------------------------------------
    public static Pla NewPla()
    {
        return new Pla
        {
            F              = null,
            D              = null,
            R              = null,
            Phase          = PSet.Null,
            PairData       = null,
            Label          = null,
            Filename       = null,
            PlaType        = 0,
            SymbolicData   = null,
            SymbolicOutput = null,
        };
    }

    // -----------------------------------------------------------------------
    // free_PLA  (cvrin.c) — in C# just null out fields; GC handles memory
    // -----------------------------------------------------------------------
    public static void FreePla(Pla pla)
    {
        pla.F              = null;
        pla.R              = null;
        pla.D              = null;
        pla.Phase          = PSet.Null;
        pla.PairData       = null;
        pla.Label          = null;
        pla.Filename       = null;
        pla.SymbolicData   = null;
        pla.SymbolicOutput = null;
    }
}
