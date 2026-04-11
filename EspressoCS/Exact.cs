namespace EspressoCS;

using static SetFamily;
using static SetOps;
using static SparseMatrix;
using static CubeContext;

/// <summary>
/// Exact — exact minimization algorithm.
/// Translated from exact.c
/// </summary>
public static class Exact
{
    // -----------------------------------------------------------------------
    // minimize_exact — public entry point for exact minimization
    // -----------------------------------------------------------------------

    /// <summary>
    /// MinimizeExact — perform exact minimization of a Boolean function.
    /// Generates all prime implicants, sets up a covering table, and solves
    /// the exact set cover problem using branch-and-bound.
    /// </summary>
    public static SetFamily MinimizeExact(SetFamily F, SetFamily D, SetFamily R, int exactCover)
    {
        return DoMinimize(F, D, R, exactCover, weighted: false);
    }

    /// <summary>
    /// MinimizeExactLiterals — exact minimization with literal-weighted cost.
    /// </summary>
    public static SetFamily MinimizeExactLiterals(SetFamily F, SetFamily D, SetFamily R, int exactCover)
    {
        return DoMinimize(F, D, R, exactCover, weighted: true);
    }

    // -----------------------------------------------------------------------
    // do_minimize — core exact minimization algorithm
    // -----------------------------------------------------------------------

    private static SetFamily DoMinimize(SetFamily F, SetFamily D, SetFamily R, int exactCover, bool weighted)
    {
        uint debugSave = Globals.Debug;

        if ((Globals.Debug & EspressoConstants.Exact) != 0)
        {
            Globals.Debug |= (uint)(EspressoConstants.Irred | EspressoConstants.Mincov);
        }

        int level = ((Globals.Debug & EspressoConstants.Mincov) != 0) ? 4 : 0;
        int heuristic = (exactCover != 0) ? 0 : 1;

        // Generate all prime implicants
        F = Primes.PrimesConsensus(Cofactor.Cube2List(F, D));

        // Setup the prime implicant table
        Irred.IrredSplitCover(F, D, out SetFamily E, out SetFamily Rt, out SetFamily Rp);
        SmMatrix table = Irred.IrredDeriveTable(D, E, Rp);

        // Solve either a weighted or nonweighted covering problem
        int[]? weights = null;
        if (weighted)
        {
            weights = new int[F.Count];
            for (int i = 0; i < Rp.Count; i++)
            {
                PSet p = Rp.GetSet(i);
                weights[GetSize(p)] = Size - SetOrd(p);
            }
        }

        SmRow cover = MinCov.SmMinimumCover(table, weights, heuristic, level);

        // Form the result cover
        SetFamily newF = SfNew(100, Size);
        for (int i = 0; i < E.Count; i++)
        {
            newF = SfAddSet(newF, E.GetSet(i));
        }
        for (SmElement? pe = cover.FirstCol; pe != null; pe = pe.NextCol)
        {
            newF = SfAddSet(newF, F.GetSet(pe.ColNum));
        }

        SfFree(E);
        SfFree(Rt);
        SfFree(Rp);
        SparseMatrix.SmFree(table);
        SparseMatrix.SmRowFree(cover);
        SfFree(F);

        // Attempt to make the results more sparse
        Globals.Debug &= ~(uint)(EspressoConstants.Irred | EspressoConstants.Sharp | EspressoConstants.Mincov);
        if (!Globals.SkipMakeSparse && R != null)
        {
            newF = Sparse.MakeSparse(newF, D, R);
        }

        Globals.Debug = debugSave;
        return newF;
    }
}
