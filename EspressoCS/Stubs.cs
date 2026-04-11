namespace EspressoCS;

/// <summary>
/// Stub implementations for functions not yet ported from their C source files.
/// These allow the project to compile while the full port is in progress.
/// Functions here should be replaced with proper ports as each source file is translated.
/// </summary>
public static class Stubs
{
    // -----------------------------------------------------------------------
    // cofactor.c — delegate to Cofactor
    // -----------------------------------------------------------------------

    /// <summary>cube1list — delegates to Cofactor.Cube1List.</summary>
    public static PSet[] Cube1List(SetFamily A) => Cofactor.Cube1List(A);

    /// <summary>massive_count — delegates to Cofactor.MassiveCount.</summary>
    public static void MassiveCount(PSet[] T) => Cofactor.MassiveCount(T);

    /// <summary>free_cubelist — GC handles reclamation; no-op in C# port.</summary>
    public static void FreeCubelist(PSet[] T) { }

    /// <summary>cube2list — delegates to Cofactor.Cube2List.</summary>
    public static PSet[] Cube2List(SetFamily F, SetFamily D) => Cofactor.Cube2List(F, D);

    /// <summary>cube_is_covered — delegates to Irred.CubeIsCovered.</summary>
    public static bool CubeIsCovered(PSet[] T, PSet c) => Irred.CubeIsCovered(T, c);

    // -----------------------------------------------------------------------
    // setc.c — delegate to SetC
    // -----------------------------------------------------------------------

    /// <summary>cdist — delegates to SetC.Cdist.</summary>
    public static int Cdist(PSet a, PSet b) => SetC.Cdist(a, b);

    /// <summary>ccommon — delegates to SetC.Ccommon.</summary>
    public static bool Ccommon(PSet p, PSet seed, PSet cof) => SetC.Ccommon(p, seed, cof);

    // -----------------------------------------------------------------------
    // espresso.c — Espresso / exact minimisation / gasp strategies
    // -----------------------------------------------------------------------

    /// <summary>espresso — delegates to EspressoAlgo.Espresso.</summary>
    public static SetFamily Espresso(SetFamily F, SetFamily D, SetFamily R) =>
        EspressoAlgo.Espresso(F, D, R);

    /// <summary>minimize_exact — delegates to Exact.MinimizeExact.</summary>
    public static SetFamily MinimizeExact(SetFamily F, SetFamily D, SetFamily R, int x) =>
        Exact.MinimizeExact(F, D, R, x);

    /// <summary>simplify — delegates to Compl.Simplify.</summary>
    public static SetFamily Simplify(PSet[] cubelist) => Compl.Simplify(cubelist);

    /// <summary>unravel — delegates to CvrM.Unravel.</summary>
    public static SetFamily Unravel(SetFamily F, int var) => CvrM.Unravel(F, var);

    /// <summary>last_gasp — delegates to Gasp.LastGasp.</summary>
    public static SetFamily LastGasp(SetFamily F, SetFamily D, SetFamily R, ref EspressoCS.Cost cost) =>
        Gasp.LastGasp(F, D, R, ref cost);

    /// <summary>super_gasp — delegates to Gasp.SuperGasp.</summary>
    public static SetFamily SuperGasp(SetFamily F, SetFamily D, SetFamily R, ref EspressoCS.Cost cost) =>
        Gasp.SuperGasp(F, D, R, ref cost);

    /// <summary>
    /// make_sparse — attempt to make the PLA matrix sparse.
    /// TODO: port make_sparse() from cvrmisc.c.
    /// </summary>
    public static SetFamily MakeSparse(SetFamily F, SetFamily D, SetFamily R) => Sparse.MakeSparse(F, D, R);

    // -----------------------------------------------------------------------
    // compl.c — complement / d1merge
    // -----------------------------------------------------------------------

    /// <summary>complement — delegates to Compl.Complement.</summary>
    public static SetFamily Complement(PSet[] cubelist) => Compl.Complement(cubelist);

    /// <summary>d1merge — delegates to Contain.D1Merge.</summary>
    public static SetFamily D1Merge(SetFamily cover, int var) => Contain.D1Merge(cover, var);

    // -----------------------------------------------------------------------
    // opo.c / pair.c / hack.c — PLA transformations
    // -----------------------------------------------------------------------

    /// <summary>set_phase — delegates to Opo.SetPhase.</summary>
    public static Pla SetPhase(Pla PLA) => Opo.SetPhase(PLA);

    /// <summary>set_pair — set up two-bit decoder pairing. Delegates to PairOps.SetPair.</summary>
    public static void SetPair(Pla PLA) => PairOps.SetPair(PLA);

    /// <summary>map_symbolic — map symbolic inputs into positional cube notation. Delegates to Hack.MapSymbolic.</summary>
    public static void MapSymbolic(Pla PLA) => Hack.MapSymbolic(PLA);

    /// <summary>map_output_symbolic — map symbolic outputs. Delegates to Hack.MapOutputSymbolic.</summary>
    public static void MapOutputSymbolic(Pla PLA) => Hack.MapOutputSymbolic(PLA);

    // -----------------------------------------------------------------------
    // prtime.c / cpu_time.c — timing utilities
    // -----------------------------------------------------------------------

    /// <summary>ptime() — current wall-clock time in milliseconds.</summary>
    public static long PTime() => Environment.TickCount64;

    /// <summary>print_time(t) — format elapsed milliseconds as a human-readable string.</summary>
    public static string PrintTime(long t) => $"{t}ms";
}