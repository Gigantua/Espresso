namespace EspressoCS;

/// <summary>Mirrors pla_types_struct from espresso.h.</summary>
public class PlaTypeEntry
{
    public string Key;
    public int Value;
    public PlaTypeEntry(string key, int value) { Key = key; Value = value; }
}

/// <summary>Mirrors the pla_types[] array from globals.c.</summary>
public static class PlaTypes
{
    public static readonly PlaTypeEntry[] Table =
    {
        new("-f",      Pla.FType),
        new("-r",      Pla.RType),
        new("-d",      Pla.DType),
        new("-fd",     Pla.FdType),
        new("-fr",     Pla.FrType),
        new("-dr",     Pla.DrType),
        new("-fdr",    Pla.FdrType),
        new("-fc",     Pla.FType  | Pla.ConstraintsType),
        new("-rc",     Pla.RType  | Pla.ConstraintsType),
        new("-dc",     Pla.DType  | Pla.ConstraintsType),
        new("-fdc",    Pla.FdType | Pla.ConstraintsType),
        new("-frc",    Pla.FrType | Pla.ConstraintsType),
        new("-drc",    Pla.DrType | Pla.ConstraintsType),
        new("-fdrc",   Pla.FdrType | Pla.ConstraintsType),
        new("-pleasure",  Pla.PleasureType),
        new("-eqn",       Pla.EqntottType),
        new("-eqntott",   Pla.EqntottType),
        new("-kiss",      Pla.KissType),
        new("-cons",      Pla.ConstraintsType),
        new("-scons",     Pla.SymbolicConstraintsType),
    };
}
