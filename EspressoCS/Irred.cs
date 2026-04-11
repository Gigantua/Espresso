namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;

/// <summary>
/// Irred — irredundancy checking and minimal subset extraction.
/// Translated from irred.c.
/// Finds and removes redundant cubes from a cover, producing a minimal subset.
/// </summary>
public static class Irred
{
    // -----------------------------------------------------------------------
    // Static state for irred_derive_table and recursion tracking
    // -----------------------------------------------------------------------
    private static int Rp_current;
    private static int taut_level = 0;
    private static int ftaut_level = 0;

    // Tri-state return values matching C MAYBE/TRUE/FALSE
    private const int TautFalse = 0;
    private const int TautTrue  = 1;
    private const int TautMaybe = 2;

    // -----------------------------------------------------------------------
    // irredundant — Return a minimal subset of F
    // -----------------------------------------------------------------------

    /// <summary>
    /// irredundant — return a minimal subset of F that still covers D.
    /// Marks redundant cubes as inactive and returns only the active ones.
    /// </summary>
    public static SetFamily Irredundant(SetFamily F, SetFamily D)
    {
        MarkIrredundant(F, D);
        return SfInactive(F);
    }

    // -----------------------------------------------------------------------
    // mark_irredundant — find redundant cubes, and mark them "INACTIVE"
    // -----------------------------------------------------------------------

    /// <summary>
    /// mark_irredundant — find redundant cubes, and mark them "INACTIVE".
    /// Splits the cover into E (essential), Rt (totally redundant), 
    /// and Rp (partially redundant), then finds a minimum cover.
    /// </summary>
    public static void MarkIrredundant(SetFamily F, SetFamily D)
    {
        SetFamily E, Rt, Rp;
        PSet p, p1;
        SmMatrix table;
        SmRow cover;
        SmElement pe;

        /* extract a minimum cover */
        IrredSplitCover(F, D, out E, out Rt, out Rp);
        table = IrredDeriveTable(D, E, Rp);
        cover = MinCov.SmMinimumCover(table, null, 1, 0);

        /* mark the cubes for the result */
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            ResetFlag(p, Active);
            ResetFlag(p, RelEssen);
        }

        for (int i = 0; i < E.Count; i++)
        {
            p = E.GetSet(i);
            p1 = F.GetSet((int)GetSize(p));  // SIZE(p) tracks original index in F
            SetFlag(p1, Active);
            SetFlag(p1, RelEssen);  /* for essen(), mark as rel. ess. */
        }

        for (var pe2 = cover.FirstCol; pe2 != null; pe2 = pe2.NextCol)
        {
            p1 = F.GetSet(pe2.ColNum);
            SetFlag(p1, Active);
        }

        if ((Globals.Debug & EspressoConstants.Irred) != 0)
        {
            Console.WriteLine($"# IRRED: F={F.Count} E={E.Count} R={Rt.Count + Rp.Count} " +
                $"Rt={Rt.Count} Rp={Rp.Count} Rc={cover.Length} " +
                $"Final={E.Count + cover.Length} Bound=0");
        }

        SfFree(E);
        SfFree(Rt);
        SfFree(Rp);
        SparseMatrix.SmFree(table);
        SparseMatrix.SmRowFree(cover);
    }

    // -----------------------------------------------------------------------
    // irred_split_cover — find E, Rt, and Rp from the cover F, D
    // -----------------------------------------------------------------------

    /// <summary>
    /// irred_split_cover — split cover into:
    ///   E  — relatively essential cubes
    ///   Rt — totally redundant cubes
    ///   Rp — partially redundant cubes
    /// </summary>
    public static void IrredSplitCover(SetFamily F, SetFamily D, 
        out SetFamily E, out SetFamily Rt, out SetFamily Rp)
    {
        PSet p;
        int index;
        SetFamily R;
        PSet[] FD, ED;

        /* number the cubes of F -- these numbers track into E, Rp, Rt, etc. */
        index = 0;
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            PutSize(p, index);
            index++;
        }

        E = SfNew(10, F.SfSize);
        Rt = SfNew(10, F.SfSize);
        Rp = SfNew(10, F.SfSize);
        R = SfNew(10, F.SfSize);

        /* Split F into E and R */
        FD = Stubs.Cube2List(F, D);
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            if (CubeIsCovered(FD, p))
            {
                R = SfAddSet(R, p);
            }
            else
            {
                E = SfAddSet(E, p);
            }
        }
        Stubs.FreeCubelist(FD);

        /* Split R into Rt and Rp */
        ED = Stubs.Cube2List(E, D);
        for (int i = 0; i < R.Count; i++)
        {
            p = R.GetSet(i);
            if (CubeIsCovered(ED, p))
            {
                Rt = SfAddSet(Rt, p);
            }
            else
            {
                Rp = SfAddSet(Rp, p);
            }
        }
        Stubs.FreeCubelist(ED);

        SfFree(R);
    }

    // -----------------------------------------------------------------------
    // irred_derive_table — given the covers D, E and the set of
    // partially redundant primes Rp, build a covering table showing
    // possible selections of primes to cover Rp.
    // -----------------------------------------------------------------------

    /// <summary>
    /// irred_derive_table — build a sparse matrix showing which primes
    /// can cover which elements of Rp (partially redundant cubes).
    /// </summary>
    public static SmMatrix IrredDeriveTable(
        SetFamily D, SetFamily E, SetFamily Rp)
    {
        PSet p;
        PSet[] list;
        SmMatrix table;
        int size_last_dominance, i;

        /* Mark each cube in DE as not part of the redundant set */
        for (int j = 0; j < D.Count; j++)
        {
            p = D.GetSet(j);
            ResetFlag(p, Redund);
        }
        for (int j = 0; j < E.Count; j++)
        {
            p = E.GetSet(j);
            ResetFlag(p, Redund);
        }

        /* Mark each cube in Rp as partially redundant */
        for (int j = 0; j < Rp.Count; j++)
        {
            p = Rp.GetSet(j);
            SetFlag(p, Redund);
        }

        /* For each cube in Rp, find ways to cover its minterms */
        list = Cofactor.Cube3List(D, E, Rp);
        table = SparseMatrix.SmAlloc();
        size_last_dominance = 0;
        i = 0;

        for (int j = 0; j < Rp.Count; j++)
        {
            p = Rp.GetSet(j);
            Rp_current = GetSize(p);
            FcubeIsCovered(list, p, table);
            ResetFlag(p, Redund);

            if ((Globals.Debug & EspressoConstants.Irred1) != 0)
            {
                Console.WriteLine($"IRRED1: {i} of {Rp.Count} to-go={Rp.Count - i}, " +
                    $"table={table.NRows}x{table.NCols}");
            }

            /* try to keep memory limits down by reducing table as we go along */
            if (table.NRows - size_last_dominance > 1000)
            {
                /* SmRowDominance removed - not implemented yet */
                size_last_dominance = table.NRows;
                if ((Globals.Debug & EspressoConstants.Irred1) != 0)
                {
                    Console.WriteLine($"IRRED1: delete redundant rows, now {table.NRows}x{table.NCols}");
                }
            }
            i++;
        }
        Stubs.FreeCubelist(list);

        return table;
    }

    // -----------------------------------------------------------------------
    // cube_is_covered — determine if a cubelist "covers" a single cube
    // -----------------------------------------------------------------------

    /// <summary>
    /// cube_is_covered — check if a cubelist (as a cover) tautologically
    /// covers a single cube by computing tautology of the cofactor.
    /// </summary>
    public static bool CubeIsCovered(PSet[] T, PSet c)
    {
        var cofactored = Cofactor.GetCofactor(T, c);
        bool result = Tautology(cofactored);
        Stubs.FreeCubelist(cofactored);
        return result;
    }

    // -----------------------------------------------------------------------
    // tautology — answer the tautology question for T
    // -----------------------------------------------------------------------

    /// <summary>
    /// tautology — check if cubelist T forms a tautology.
    /// Uses recursive algorithm with special case checking.
    /// T will be disposed of.
    /// </summary>
    public static bool Tautology(PSet[] T)
    {
        int result;

        if ((Globals.Debug & (int)EspressoConstants.Taut) != 0)
        {
            DebugPrint(T, "TAUTOLOGY", taut_level++);
        }

        result = TautSpecialCases(T);
        if (result == TautMaybe)
        {
            PSet cl = SetNew(Size);
            PSet cr = SetNew(Size);
            int best = Cofactor.BinateSplitSelect(T, cl, cr, (int)EspressoConstants.Taut);
            
            PSet[] T_cl = Cofactor.Scofactor(T, cl, best);
            PSet[] T_cr = Cofactor.Scofactor(T, cr, best);
            
            bool res1 = Tautology(T_cl);
            bool res2 = Tautology(T_cr);
            result = res1 && res2 ? TautTrue : TautFalse;

            Stubs.FreeCubelist(T);
            SetFree(cl);
            SetFree(cr);
        }

        if ((Globals.Debug & (int)EspressoConstants.Taut) != 0)
        {
            Console.WriteLine($"exit TAUTOLOGY[{--taut_level}]: {PrintBool(result == TautTrue)}");
        }

        return result == TautTrue;
    }

    // -----------------------------------------------------------------------
    // taut_special_cases — check special cases for tautology
    // -----------------------------------------------------------------------

    /// <summary>
    /// taut_special_cases — check special cases for tautology.
    /// Returns TautTrue/TautFalse if answer is determined (T is freed),
    /// or TautMaybe if recursion is needed (T is NOT freed).
    /// </summary>
    private static int TautSpecialCases(PSet[] T)
    {
        PSet p, ceil = Temp![0], temp = Temp![1];
        int var;

        /* Check for a row of all 1's which implies tautology */
        for (int i = 2; i < T.Length && !T[i].IsNull; i++)
        {
            p = T[i];
            if (FullRow(p, T[0]))
            {
                Stubs.FreeCubelist(T);
                return TautTrue;
            }
        }

        /* Check for a column of all 0's which implies no tautology */
    start:
        SetCopy(ceil, T[0]);
        for (int i = 2; i < T.Length && !T[i].IsNull; i++)
        {
            p = T[i];
            InlineOr(ceil, ceil, p);
        }

        if (!SetpEqual(ceil, FullSet))
        {
            Stubs.FreeCubelist(T);
            return TautFalse;
        }

        /* Collect column counts, determine unate variables, etc. */
        Stubs.MassiveCount(T);

        /* If function is unate (and no row of all 1's), then no tautology */
        if (VarsUnate == VarsActive)
        {
            Stubs.FreeCubelist(T);
            return TautFalse;
        }

        /* If active in a single variable (and no column of 0's) then tautology */
        if (VarsActive == 1)
        {
            Stubs.FreeCubelist(T);
            return TautTrue;
        }

        /* Check for unate variables, and reduce cover if there are any */
        if (VarsUnate != 0)
        {
            /* Form a cube "ceil" with full variables in the unate variables */
            SetCopy(ceil, EmptySet);
            for (var = 0; var < NumVars; var++)
            {
                if (IsUnate![var])
                {
                    InlineOr(ceil, ceil, VarMask![var]);
                }
            }

            /* Save only those cubes that are "full" in all unate variables */
            int Tsave_idx = 2;
            for (int i = 2; i < T.Length && !T[i].IsNull; i++)
            {
                p = T[i];
                if (SetpImplies(ceil, SetOr(temp, p, T[0])))
                {
                    T[Tsave_idx++] = p;
                }
            }
            T[Tsave_idx] = PSet.Null;

            if ((Globals.Debug & (int)EspressoConstants.Taut) != 0)
            {
                int cubeListSize = Tsave_idx - 2;
                Console.WriteLine($"UNATE_REDUCTION: {VarsUnate} unate variables, " +
                    $"reduced to {cubeListSize}");
            }
            goto start;
        }

        /* Check for component reduction */
        if (VarZeros![Best] < Cofactor.CubeListSize(T) / 2)
        {
            PSet[] A, B;
            if (CubelistPartition(T, out A, out B, (int)(Globals.Debug & EspressoConstants.Taut)) == 0)
            {
                return TautMaybe;
            }
            else
            {
                Stubs.FreeCubelist(T);
                if (Tautology(A))
                {
                    Stubs.FreeCubelist(B);
                    return TautTrue;
                }
                else
                {
                    return Tautology(B) ? TautTrue : TautFalse;
                }
            }
        }

        /* We tried as hard as we could, but must recurse from here on */
        return TautMaybe;
    }

    // -----------------------------------------------------------------------
    // fcube_is_covered — determine exactly how a cubelist "covers" a cube
    // -----------------------------------------------------------------------

    /// <summary>
    /// fcube_is_covered — find all ways a cubelist covers a cube (for table building).
    /// </summary>
    private static void FcubeIsCovered(PSet[] T, PSet c, SmMatrix table)
    {
        var cofactored = Cofactor.GetCofactor(T, c);
        Ftautology(cofactored, table);
        Stubs.FreeCubelist(cofactored);
    }

    // -----------------------------------------------------------------------
    // ftautology — find ways to make a tautology
    // -----------------------------------------------------------------------

    /// <summary>
    /// ftautology — find all ways to make a tautology and record in table.
    /// T will be disposed of.
    /// </summary>
    private static void Ftautology(PSet[] T, SmMatrix table)
    {
        PSet cl, cr;
        int best;

        if ((Globals.Debug & (int)EspressoConstants.Taut) != 0)
        {
            DebugPrint(T, "FIND_TAUTOLOGY", ftaut_level++);
        }

        if (FtautSpecialCases(T, table) == TautMaybe)  /* if not determined */
        {
            cl = SetNew(Size);
            cr = SetNew(Size);
            best = Cofactor.BinateSplitSelect(T, cl, cr, (int)EspressoConstants.Taut);

            var T_cl = Cofactor.Scofactor(T, cl, best);
            var T_cr = Cofactor.Scofactor(T, cr, best);

            Ftautology(T_cl, table);
            Ftautology(T_cr, table);

            Stubs.FreeCubelist(T);
            SetFree(cl);
            SetFree(cr);
        }

        if ((Globals.Debug & (int)EspressoConstants.Taut) != 0)
        {
            Console.WriteLine($"exit FIND_TAUTOLOGY[{--ftaut_level}]: table is {table.NRows} by {table.NCols}");
        }
    }

    // -----------------------------------------------------------------------
    // ftaut_special_cases — check special cases for find_tautology
    // -----------------------------------------------------------------------

    /// <summary>
    /// ftaut_special_cases — check special cases for find_tautology.
    /// Returns TautTrue/TautFalse if determined (T freed), TautMaybe if not (T not freed).
    /// </summary>
    private static int FtautSpecialCases(PSet[] T, SmMatrix table)
    {
        PSet p, temp = Temp![0], ceil = Temp![1];
        int var, rownum;

        /* Check for a row of all 1's in the essential cubes */
        for (int i = 2; i < T.Length && !T[i].IsNull; i++)
        {
            p = T[i];
            if (!TestP(p, Redund))
            {
                if (FullRow(p, T[0]))
                {
                    /* subspace is covered by essentials -- no new rows for table */
                    Stubs.FreeCubelist(T);
                    return TautTrue;
                }
            }
        }

        /* Collect column counts, determine unate variables, etc. */
    start:
        Stubs.MassiveCount(T);

        /* If function is unate, find the rows of all 1's */
        if (VarsUnate == VarsActive)
        {
            /* find which nonessentials cover this subspace */
            rownum = table.LastRow != null ? table.LastRow.RowNum + 1 : 0;
            SparseMatrix.SmInsert(table, rownum, Rp_current);
            
            for (int i = 2; i < T.Length && !T[i].IsNull; i++)
            {
                p = T[i];
                if (TestP(p, Redund))
                {
                    /* See if a redundant cube covers this leaf */
                    if (FullRow(p, T[0]))
                    {
                        SparseMatrix.SmInsert(table, rownum, (int)GetSize(p));
                    }
                }
            }
            Stubs.FreeCubelist(T);
            return TautTrue;
        }

        /* Perform unate reduction if there are any unate variables */
        if (VarsUnate != 0)
        {
            /* Form a cube "ceil" with full variables in the unate variables */
            SetCopy(ceil, EmptySet);
            for (var = 0; var < NumVars; var++)
            {
                if (IsUnate![var])
                {
                    InlineOr(ceil, ceil, VarMask![var]);
                }
            }

            /* Save only those cubes that are "full" in all unate variables */
            int Tsave_idx = 2;
            for (int i = 2; i < T.Length && !T[i].IsNull; i++)
            {
                p = T[i];
                if (SetpImplies(ceil, SetOr(temp, p, T[0])))
                {
                    T[Tsave_idx++] = p;
                }
            }
            T[Tsave_idx] = PSet.Null;

            if ((Globals.Debug & (int)EspressoConstants.Taut) != 0)
            {
                int cubeListSize = Tsave_idx - 2;
                Console.WriteLine($"UNATE_REDUCTION: {VarsUnate} unate variables, " +
                    $"reduced to {cubeListSize}");
            }
            goto start;
        }

        /* Not much we can do about it */
        return TautMaybe;
    }

    // -----------------------------------------------------------------------
    // Helper functions
    // -----------------------------------------------------------------------

    private static void DebugPrint(PSet[] T, string name, int level)
    {
        Console.WriteLine($"{name}[{level}]: {Ps1(T[2])}");
    }

    private static string PrintBool(bool b) => b ? "TRUE" : "FALSE";

    private static int CubelistPartition(PSet[] T, out PSet[] A, out PSet[] B, int debug)
        => CvrM.CubelistPartition(T, out A, out B, (uint)debug);
}
