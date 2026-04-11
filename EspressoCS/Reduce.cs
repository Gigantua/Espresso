namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;

/// <summary>
/// Reduce — redundancy reduction by removing unnecessary literals from cubes.
/// Translated 1:1 from reduce.c.
/// Uses the SCCC (Smallest Cube Containing the Complement) to find maximal reductions.
/// </summary>
public static class Reduce
{
    private static bool toggle = true;

    // -----------------------------------------------------------------------
    // reduce — replace each cube in F with its reduction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reduce — replace each cube in F with its reduction.
    /// The reduction is the smallest cube contained in the cube which still covers
    /// the same logic function.
    /// </summary>
    public static SetFamily ReduceCover(SetFamily F, SetFamily D)
    {
        PSet p, cunder;
        PSet[] FD;

        /* Order the cubes */
        if (Globals.UseRandomOrder)
            F = RandomOrder(F);
        else
        {
            F = toggle ? SortReduce(F) : MiniSort(F, Descend);
            toggle = !toggle;
        }

        /* Try to reduce each cube */
        FD = Stubs.Cube2List(F, D);
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            cunder = ReduceOneCube(FD, p);  /* reduce the cube */
            if (SetpEqual(cunder, p))
            {
                SetFlag(p, Active);   /* cube remains active */
                SetFlag(p, Prime);    /* cube remains prime ? */
            }
            else
            {
                if ((Globals.Debug & EspressoConstants.Reduce) != 0)
                {
                    Console.WriteLine("REDUCE: {0} to {1} {2}",
                        PcCube1(p), PcCube2(cunder), Stubs.PrintTime(Stubs.PTime()));
                }
                SetCopy(p, cunder);           /* save reduced version */
                ResetFlag(p, Prime);           /* cube is no longer prime */
                if (SetpEmpty(cunder))
                    ResetFlag(p, Active);           /* if null, kill the cube */
                else
                    SetFlag(p, Active);             /* cube is active */
            }
            SetFree(cunder);
        }
        Stubs.FreeCubelist(FD);

        /* Delete any cubes of F which reduced to the empty cube */
        return SfInactive(F);
    }

    // -----------------------------------------------------------------------
    // reduce_cube — find the maximal reduction of a cube
    // -----------------------------------------------------------------------

    /// <summary>
    /// ReduceOneCube — find the maximal reduction of a single cube.
    /// </summary>
    public static PSet ReduceOneCube(PSet[] FD, PSet p)
    {
        PSet cunder;

        cunder = Sccc(Cofactor.GetCofactor(FD, p));
        return SetAnd(cunder, cunder, p);
    }

    // -----------------------------------------------------------------------
    // sccc — find Smallest Cube Containing the Complement of a cover
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sccc — find the Smallest Cube Containing the Complement of a cover.
    /// Uses the unate-recursive paradigm to compute SCCC efficiently.
    /// T will be disposed of.
    /// </summary>
    public static PSet Sccc(PSet[] T)
    {
        PSet r;
        PSet cl, cr;
        int best;
        
        if ((Globals.Debug & EspressoConstants.Reduce1) != 0)
        {
            DebugPrint(T, "SCCC", 0);
        }

        if (ScccSpecialCases(T, out r) == MAYBE)
        {
            cl = SetNew(Size);
            cr = SetNew(Size);
            best = BinateSplitSelect(T, cl, cr, EspressoConstants.Reduce1);
            r = ScccMerge(
                Sccc(Cofactor.Scofactor(T, cl, best)),
                Sccc(Cofactor.Scofactor(T, cr, best)),
                cl, cr);
            Stubs.FreeCubelist(T);
        }

        if ((Globals.Debug & EspressoConstants.Reduce1) != 0)
            Console.WriteLine("SCCC: result is {0}", PcCube1(r));
        return r;
    }

    // -----------------------------------------------------------------------
    // sccc_merge — merge left and right SCCC results
    // -----------------------------------------------------------------------

    private static PSet ScccMerge(PSet left, PSet right, PSet cl, PSet cr)
    {
        InlineAnd(left, left, cl);
        InlineAnd(right, right, cr);
        InlineOr(left, left, right);
        SetFree(right);
        SetFree(cl);
        SetFree(cr);
        return left;
    }

    // -----------------------------------------------------------------------
    // sccc_cube — find the smallest cube containing the complement of a cube
    // -----------------------------------------------------------------------

    private static PSet ScccCube(PSet result, PSet p)
    {
        PSet temp = Temp![0];
        PSet mask;
        int var;

        if ((var = Cactive(p)) >= 0)
        {
            mask = VarMask![var];
            InlineXor(temp, p, mask);
            InlineAnd(result, result, temp);
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // sccc_special_cases — check special cases for sccc
    // -----------------------------------------------------------------------

    private static int ScccSpecialCases(PSet[] T, out PSet result)
    {
        PSet p, temp = Temp![1];
        PSet ceil, cof = T[0];
        PSet[] A, B;

        result = PSet.Null;

        /* empty cover => complement is universe => SCCC is universe */
        if (T[2].IsNull)
        {
            result = SetSave(FullSet);
            Stubs.FreeCubelist(T);
            return TRUE;
        }

        /* row of 1's => complement is empty => SCCC is empty */
        for (int t1 = 2; !T[t1].IsNull; t1++)
        {
            p = T[t1];
            if (FullRow(p, cof))
            {
                result = SetNew(Size);
                Stubs.FreeCubelist(T);
                return TRUE;
            }
        }

        /* Collect column counts, determine unate variables, etc. */
        Stubs.MassiveCount(T);

        /* If cover is unate (or single cube), apply simple rules */
        if (VarsUnate == VarsActive || T[3].IsNull)
        {
            result = SetSave(FullSet);
            for (int t1 = 2; !T[t1].IsNull; t1++)
            {
                p = T[t1];
                ScccCube(result, SetOr(temp, p, cof));
            }
            Stubs.FreeCubelist(T);
            return TRUE;
        }

        /* Check for column of 0's (which can be easily factored) */
        ceil = SetSave(cof);
        for (int t1 = 2; !T[t1].IsNull; t1++)
        {
            p = T[t1];
            InlineOr(ceil, ceil, p);
        }
        if (!SetpEqual(ceil, FullSet))
        {
            result = ScccCube(SetSave(FullSet), ceil);
            if (SetpEqual(result, FullSet))
            {
                SetFree(ceil);
            }
            else
            {
                result = ScccMerge(
                    Sccc(Cofactor.GetCofactor(T, ceil)),
                    SetSave(FullSet), ceil, result);
            }
            Stubs.FreeCubelist(T);
            return TRUE;
        }
        SetFree(ceil);

        /* Single active column at this point => tautology => SCCC is empty */
        if (VarsActive == 1)
        {
            result = SetNew(Size);
            Stubs.FreeCubelist(T);
            return TRUE;
        }

        /* Check for components */
        if (VarZeros![Best] < Cofactor.CubeListSize(T) / 2)
        {
            if (CubelistPartition(T, out A, out B, (Globals.Debug & EspressoConstants.Reduce1) != 0) == 0)
            {
                return MAYBE;
            }
            else
            {
                Stubs.FreeCubelist(T);
                result = Sccc(A);
                PSet ceil2 = Sccc(B);
                SetAnd(result, result, ceil2);
                SetFree(ceil2);
                return TRUE;
            }
        }

        /* Not much we can do about it */
        return MAYBE;
    }

    // -----------------------------------------------------------------------
    // Helper: FindRedundant — identify redundant literals
    // -----------------------------------------------------------------------

    /// <summary>
    /// FindRedundant — identify literals in a cube that are redundant.
    /// These can be removed without affecting the cover.
    /// </summary>
    public static PSet FindRedundant(SetFamily F, SetFamily D, PSet c)
    {
        PSet[] FD = Stubs.Cube2List(F, D);
        PSet cunder = ReduceOneCube(FD, c);
        Stubs.FreeCubelist(FD);
        
        PSet redundant = SetDiff(SetNew(Size), c, cunder);
        SetFree(cunder);
        return redundant;
    }

    // -----------------------------------------------------------------------
    // Helper: FindRedundantWrap — wrapper for debugging
    // -----------------------------------------------------------------------

    /// <summary>
    /// FindRedundantWrap — wrapper to find and report redundant parts.
    /// </summary>
    public static void FindRedundantWrap(SetFamily F, SetFamily D, PSet c)
    {
        PSet redundant = FindRedundant(F, D, c);
        if (!SetpEmpty(redundant))
        {
            if ((Globals.Debug & EspressoConstants.Reduce) != 0)
            {
                Console.WriteLine("REDUNDANT in {0}: {1}", PcCube1(c), PcCube2(redundant));
            }
        }
        SetFree(redundant);
    }

    // -----------------------------------------------------------------------
    // Helper: ReduceOneCubeIterative — iteratively reduce a cube
    // -----------------------------------------------------------------------

    /// <summary>
    /// ReduceOneCubeIterative — iteratively reduce a cube until no more reductions possible.
    /// </summary>
    public static PSet ReduceOneCubeIterative(SetFamily F, SetFamily D, PSet c)
    {
        PSet prev, curr = SetCopy(SetNew(Size), c);
        
        while (true)
        {
            prev = SetCopy(SetNew(Size), curr);
            PSet[] FD = Stubs.Cube2List(F, D);
            PSet next = ReduceOneCube(FD, curr);
            Stubs.FreeCubelist(FD);
            
            if (SetpEqual(next, prev))
            {
                SetFree(next);
                SetFree(prev);
                break;
            }
            SetFree(prev);
            SetCopy(curr, next);
            SetFree(next);
        }
        
        return curr;
    }

    // -----------------------------------------------------------------------
    // Helper: SortReduce — sort cubes for reduce operation
    // -----------------------------------------------------------------------

    private static SetFamily SortReduce(SetFamily F)
        => CvrM.SortReduce(F);

    // -----------------------------------------------------------------------
    // Helper: MiniSort — sort by inner-product of cube and column sums
    // -----------------------------------------------------------------------

    private static SetFamily MiniSort(SetFamily F, Comparison<PSet> cmp)
        => CvrM.MiniSort(F, cmp);

    // -----------------------------------------------------------------------
    // Helper: RandomOrder — randomize cube order
    // -----------------------------------------------------------------------

    private static SetFamily RandomOrder(SetFamily F)
        => CvrM.RandomOrder(F);

    // -----------------------------------------------------------------------
    // Helper: Cactive — number of active variables in a cube
    // -----------------------------------------------------------------------

    private static int Cactive(PSet p) => SetC.Cactive(p);

    // -----------------------------------------------------------------------
    // Helper: Scofactor — cofactor with respect to a single-variable cube
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Helper: Scofactor — cofactor with respect to a single-variable cube
    // -----------------------------------------------------------------------

    private static PSet[] Scofactor(PSet[] T, PSet c, int var)
    {
        // Use the existing implementation from Cofactor class
        return Cofactor.Scofactor(T, c, var);
    }

    // -----------------------------------------------------------------------
    // Helper: BinateSplitSelect — select a binate variable for splitting
    // -----------------------------------------------------------------------

    private static int BinateSplitSelect(PSet[] T, PSet cl, PSet cr, uint debugLevel) =>
        Cofactor.BinateSplitSelect(T, cl, cr, (int)debugLevel);

    // -----------------------------------------------------------------------
    // Helper: CubelistPartition — partition a cubelist into components
    // -----------------------------------------------------------------------

    private static int CubelistPartition(PSet[] T, out PSet[] A, out PSet[] B, bool debug) =>
        CvrM.CubelistPartition(T, out A, out B, debug ? EspressoConstants.Reduce1 : 0);

    // -----------------------------------------------------------------------
    // Helper: DebugPrint — print debug info
    // -----------------------------------------------------------------------

    private static void DebugPrint(PSet[] T, string label, int level) =>
        CvrOut.DebugPrint(T, label, level);

    // -----------------------------------------------------------------------
    // Helper: SfInactive — return family with only active sets
    // -----------------------------------------------------------------------

    private static SetFamily SfInactive(SetFamily F) => SetFamily.SfInactive(F);

    // -----------------------------------------------------------------------
    // Helper: Iterator
    // -----------------------------------------------------------------------

    private static string PcCube1(PSet p) => $"[cube at {SetOrd(p)} bits]";
    private static string PcCube2(PSet p) => $"[cube at {SetOrd(p)} bits]";

    // Constants for special case return values
    private const int TRUE = 1;
    private const int MAYBE = 0;
}
