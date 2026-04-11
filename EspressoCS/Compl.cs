namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetC;

/// <summary>
/// Compl — complement of a multiple-valued function.
/// Uses the unate recursive paradigm (translated 1:1 from compl.c).
/// </summary>
public static class Compl
{
    private const int UseComplLift            = 0;
    private const int UseComplLiftOnset       = 1;
    private const int UseComplLiftOnsetComplex = 2;
    private const int NoLifting               = 3;

    // -----------------------------------------------------------------------
    // complement — public entry point
    // -----------------------------------------------------------------------

    public static SetFamily Complement(PSet[] T)
    {
        if ((Globals.Debug & EspressoConstants.Compl) != 0)
            CvrOut.DebugPrint(T, "COMPLEMENT", 0);

        if (ComplSpecialCases(T, out SetFamily Tbar))
            return Tbar;

        PSet cl = SetNew(Size);
        PSet cr = SetNew(Size);
        int best = Cofactor.BinateSplitSelect(T, cl, cr, (int)EspressoConstants.Compl);

        SetFamily Tl = Complement(Cofactor.Scofactor(T, cl, best));
        SetFamily Tr = Complement(Cofactor.Scofactor(T, cr, best));

        int cubeCount = Cofactor.CubeListSize(T);
        int lifting = (Tr.Count * Tl.Count > (Tr.Count + Tl.Count) * cubeCount)
            ? UseComplLiftOnset
            : UseComplLift;

        Tbar = ComplMerge(T, Tl, Tr, cl, cr, best, lifting);

        if ((Globals.Debug & EspressoConstants.Compl) != 0)
            CvrOut.Debug1Print(Tbar, "exit COMPLEMENT", 0);

        return Tbar;
    }

    // -----------------------------------------------------------------------
    // compl_special_cases
    // -----------------------------------------------------------------------

    private static bool ComplSpecialCases(PSet[] T, out SetFamily Tbar)
    {
        PSet cof = T[0];

        if (T[2].IsNull)
        {
            Tbar = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), FullSet);
            return true;
        }

        if (T[3].IsNull)
        {
            PSet tmp = SetSave(cof);
            SetOr(tmp, cof, T[2]);
            Tbar = ComplCube(tmp);
            return true;
        }

        for (int t1 = 2; !T[t1].IsNull; t1++)
        {
            if (FullRow(T[t1], cof))
            {
                Tbar = SetFamily.SfNew(0, Size);
                return true;
            }
        }

        PSet ceil = SetSave(cof);
        for (int t1 = 2; !T[t1].IsNull; t1++)
            InlineOr(ceil, ceil, T[t1]);

        if (!SetpEqual(ceil, FullSet))
        {
            SetFamily ceilCompl = ComplCube(ceil);
            SetDiff(ceil, FullSet, ceil);
            SetOr(cof, cof, ceil);
            Tbar = SetFamily.SfAppend(Complement(T), ceilCompl);
            return true;
        }

        Cofactor.MassiveCount(T);

        if (VarsActive == 1)
        {
            Tbar = SetFamily.SfNew(0, Size);
            return true;
        }
        else if (VarsUnate == VarsActive)
        {
            SetFamily A = Unate.MapCoverToUnate(T);
            A = Unate.UnateCompl(A);
            Tbar = Unate.MapUnateTocover(A);
            return true;
        }
        else
        {
            Tbar = default!;
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // compl_merge
    // -----------------------------------------------------------------------

    private static SetFamily ComplMerge(
        PSet[] T1, SetFamily L, SetFamily R,
        PSet cl, PSet cr, int var, int lifting)
    {
        // Intersect each cube with the cofactored cube and mark active
        for (int i = 0; i < L.Count; i++)
        {
            PSet p = L.GetSet(i);
            InlineAnd(p, p, cl);
            SetFlag(p, Active);
        }
        for (int i = 0; i < R.Count; i++)
        {
            PSet p = R.GetSet(i);
            InlineAnd(p, p, cr);
            SetFlag(p, Active);
        }

        // Set up d1_order mask
        SetCopy(Temp![0], VarMask![var]);

        // Build null-terminated sorted arrays for distance-1 merge
        PSet[] L1 = SfListNullTermSorted(L);
        PSet[] R1 = SfListNullTermSorted(R);

        ComplD1Merge(L1, R1);

        switch (lifting)
        {
            case UseComplLiftOnset:
            {
                SetFamily Tcover = Cofactor.CubeUnlist(T1);
                ComplLiftOnset(L1, Tcover, cr, var);
                ComplLiftOnset(R1, Tcover, cl, var);
                break;
            }
            case UseComplLiftOnsetComplex:
            {
                SetFamily Tcover = Cofactor.CubeUnlist(T1);
                ComplLiftOnsetComplex(L1, Tcover, var);
                ComplLiftOnsetComplex(R1, Tcover, var);
                break;
            }
            case UseComplLift:
                ComplLift(L1, R1, cr, var);
                ComplLift(R1, L1, cl, var);
                break;
            case NoLifting:
                break;
        }

        // Collect results: all of L, active-only from R
        SetFamily Tbar = SetFamily.SfNew(L.Count + R.Count, Size);
        for (int i = 0; i < L.Count; i++)
            SetFamily.SfAddSet(Tbar, L.GetSet(i));
        for (int i = 0; i < R.Count; i++)
        {
            PSet p = R.GetSet(i);
            if (TestP(p, Active))
                SetFamily.SfAddSet(Tbar, p);
        }

        return Tbar;
    }

    // -----------------------------------------------------------------------
    // compl_d1merge
    // -----------------------------------------------------------------------

    private static void ComplD1Merge(PSet[] L1, PSet[] R1)
    {
        int li = 0, ri = 0;
        while (!L1[li].IsNull && !R1[ri].IsNull)
        {
            switch (SetC.D1Order(L1[li], R1[ri]))
            {
                case 1:
                    ri++;
                    break;
                case -1:
                    li++;
                    break;
                default: // 0
                    ResetFlag(R1[ri], Active);
                    InlineOr(L1[li], L1[li], R1[ri]);
                    ri++;
                    break;
            }
        }
    }

    // -----------------------------------------------------------------------
    // compl_cube — De Morgan complement of a single cube
    // -----------------------------------------------------------------------

    private static SetFamily ComplCube(PSet p)
    {
        PSet diff = Temp![7];
        PSet full = FullSet;
        SetFamily R = SetFamily.SfNew(NumVars, Size);

        InlineDiff(diff, full, p);

        for (int var = 0; var < NumVars; var++)
        {
            PSet mask = VarMask![var];
            if (!SetpDisjoint(diff, mask))
            {
                PSet pdest = R.GetSet(R.Count++);
                InlineMerge(pdest, diff, full, mask);
            }
        }
        return R;
    }

    // -----------------------------------------------------------------------
    // compl_lift
    // -----------------------------------------------------------------------

    private static void ComplLift(PSet[] A1, PSet[] B1, PSet bcube, int var)
    {
        PSet lift   = Temp![4];
        PSet liftor = Temp![5];
        PSet mask   = VarMask![var];

        SetAnd(liftor, bcube, mask);

        for (int ai = 0; !A1[ai].IsNull; ai++)
        {
            PSet a = A1[ai];
            if (TestP(a, Active))
            {
                SetMerge(lift, bcube, a, mask);
                for (int bi = 0; !B1[bi].IsNull; bi++)
                {
                    if (!SetpImplies(lift, B1[bi])) continue;
                    InlineOr(a, a, liftor);
                    break;
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // compl_lift_onset
    // -----------------------------------------------------------------------

    private static void ComplLiftOnset(PSet[] A1, SetFamily T, PSet bcube, int var)
    {
        PSet lift = Temp![4];
        PSet mask = VarMask![var];

        for (int ai = 0; !A1[ai].IsNull; ai++)
        {
            PSet a = A1[ai];
            if (TestP(a, Active))
            {
                InlineAnd(lift, bcube, mask);
                InlineOr(lift, a, lift);

                bool nolift = false;
                for (int ti = 0; ti < T.Count; ti++)
                {
                    if (Cdist0(T.GetSet(ti), lift)) { nolift = true; break; }
                }

                if (!nolift)
                {
                    InlineCopy(a, lift);
                    SetFlag(a, Active);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // compl_lift_onset_complex
    // -----------------------------------------------------------------------

    private static void ComplLiftOnsetComplex(PSet[] A1, SetFamily T, int var)
    {
        PSet xlower = SetNew(Size);

        for (int ai = 0; !A1[ai].IsNull; ai++)
        {
            PSet a = A1[ai];
            if (TestP(a, Active))
            {
                InlineClear(xlower, Size);
                for (int ti = 0; ti < T.Count; ti++)
                {
                    PSet p = T.GetSet(ti);
                    int dist = Cdist01(p, a);
                    if (dist < 2)
                    {
                        if (dist == 0)
                            throw new InvalidOperationException("compl: ON-set and OFF-set are not orthogonal");
                        ForceLower(xlower, p, a);
                    }
                }
                SetDiff(xlower, VarMask![var], xlower);
                SetOr(a, a, xlower);
            }
        }
    }

    // -----------------------------------------------------------------------
    // simplify
    // -----------------------------------------------------------------------

    public static SetFamily Simplify(PSet[] T)
    {
        if ((Globals.Debug & EspressoConstants.Compl) != 0)
            CvrOut.DebugPrint(T, "SIMPLIFY", 0);

        if (SimplifySpecialCases(T, out SetFamily Tbar))
        {
            if ((Globals.Debug & EspressoConstants.Compl) != 0)
                CvrOut.Debug1Print(Tbar, "exit SIMPLIFY", 0);
            return Tbar;
        }

        PSet cl = SetNew(Size);
        PSet cr = SetNew(Size);
        int best = Cofactor.BinateSplitSelect(T, cl, cr, (int)EspressoConstants.Compl);

        SetFamily Tl = Simplify(Cofactor.Scofactor(T, cl, best));
        SetFamily Tr = Simplify(Cofactor.Scofactor(T, cr, best));

        Tbar = ComplMerge(T, Tl, Tr, cl, cr, best, UseComplLift);

        // Fall back to original if we made things worse
        if (Tbar.Count > Cofactor.CubeListSize(T))
        {
            Tbar = Cofactor.CubeUnlist(T);
        }

        if ((Globals.Debug & EspressoConstants.Compl) != 0)
            CvrOut.Debug1Print(Tbar, "exit SIMPLIFY", 0);

        return Tbar;
    }

    // -----------------------------------------------------------------------
    // simp_comp
    // -----------------------------------------------------------------------

    public static void SimpComp(PSet[] T, out SetFamily Tnew, out SetFamily Tbar)
    {
        if ((Globals.Debug & EspressoConstants.Compl) != 0)
            CvrOut.DebugPrint(T, "SIMPCOMP", 0);

        if (SimpCompSpecialCases(T, out Tnew, out Tbar))
        {
            if ((Globals.Debug & EspressoConstants.Compl) != 0)
            {
                CvrOut.Debug1Print(Tnew, "exit SIMPCOMP (new)", 0);
                CvrOut.Debug1Print(Tbar, "exit SIMPCOMP (compl)", 0);
            }
            return;
        }

        PSet cl = SetNew(Size);
        PSet cr = SetNew(Size);
        int best = Cofactor.BinateSplitSelect(T, cl, cr, (int)EspressoConstants.Compl);

        SimpComp(Cofactor.Scofactor(T, cl, best), out SetFamily Tl, out SetFamily Tlbar);
        SimpComp(Cofactor.Scofactor(T, cr, best), out SetFamily Tr, out SetFamily Trbar);

        Tnew = ComplMerge(T, Tl, Tr, cl, cr, best, UseComplLift);
        Tbar = ComplMerge(T, Tlbar, Trbar, cl, cr, best, UseComplLift);

        if (Tnew.Count > Cofactor.CubeListSize(T))
        {
            Tnew = Cofactor.CubeUnlist(T);
        }

        if ((Globals.Debug & EspressoConstants.Compl) != 0)
        {
            CvrOut.Debug1Print(Tnew, "exit SIMPCOMP (new)", 0);
            CvrOut.Debug1Print(Tbar, "exit SIMPCOMP (compl)", 0);
        }
    }

    // -----------------------------------------------------------------------
    // simp_comp_special_cases
    // -----------------------------------------------------------------------

    private static bool SimpCompSpecialCases(PSet[] T, out SetFamily Tnew, out SetFamily Tbar)
    {
        PSet cof = T[0];

        if (T[2].IsNull)
        {
            Tnew = SetFamily.SfNew(1, Size);
            Tbar = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), FullSet);
            return true;
        }

        if (T[3].IsNull)
        {
            SetOr(cof, cof, T[2]);
            Tnew = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), cof);
            Tbar = ComplCube(cof);
            return true;
        }

        for (int t1 = 2; !T[t1].IsNull; t1++)
        {
            if (FullRow(T[t1], cof))
            {
                Tnew = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), FullSet);
                Tbar = SetFamily.SfNew(1, Size);
                return true;
            }
        }

        PSet ceil = SetSave(cof);
        for (int t1 = 2; !T[t1].IsNull; t1++)
            InlineOr(ceil, ceil, T[t1]);

        if (!SetpEqual(ceil, FullSet))
        {
            PSet p = SetNew(Size);
            SetDiff(p, FullSet, ceil);
            SetOr(cof, cof, p);

            SimpComp(T, out Tnew, out Tbar);

            // Adjust ON-set
            for (int i = 0; i < Tnew.Count; i++)
                InlineAnd(Tnew.GetSet(i), Tnew.GetSet(i), ceil);

            Tbar = SetFamily.SfAppend(Tbar, ComplCube(ceil));
            return true;
        }

        Cofactor.MassiveCount(T);

        if (VarsActive == 1)
        {
            Tnew = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), FullSet);
            Tbar = SetFamily.SfNew(1, Size);
            return true;
        }
        else if (VarsUnate == VarsActive)
        {
            SetFamily A = Cofactor.CubeUnlist(T);
            Tnew = Contain.SfContain(A);
            A = Unate.MapCoverToUnate(T);
            A = Unate.UnateCompl(A);
            Tbar = Unate.MapUnateTocover(A);
            return true;
        }
        else
        {
            Tnew = default!;
            Tbar = default!;
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // simplify_special_cases
    // -----------------------------------------------------------------------

    private static bool SimplifySpecialCases(PSet[] T, out SetFamily Tnew)
    {
        PSet cof = T[0];

        if (T[2].IsNull)
        {
            Tnew = SetFamily.SfNew(0, Size);
            return true;
        }

        if (T[3].IsNull)
        {
            SetOr(cof, cof, T[2]);
            Tnew = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), cof);
            return true;
        }

        for (int t1 = 2; !T[t1].IsNull; t1++)
        {
            if (FullRow(T[t1], cof))
            {
                Tnew = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), FullSet);
                return true;
            }
        }

        PSet ceil = SetSave(cof);
        for (int t1 = 2; !T[t1].IsNull; t1++)
            InlineOr(ceil, ceil, T[t1]);

        if (!SetpEqual(ceil, FullSet))
        {
            PSet p = SetNew(Size);
            SetDiff(p, FullSet, ceil);
            SetOr(cof, cof, p);

            SetFamily A = Simplify(T);
            for (int i = 0; i < A.Count; i++)
                InlineAnd(A.GetSet(i), A.GetSet(i), ceil);
            Tnew = A;
            return true;
        }

        Cofactor.MassiveCount(T);

        if (VarsActive == 1)
        {
            Tnew = SetFamily.SfAddSet(SetFamily.SfNew(1, Size), FullSet);
            return true;
        }
        else if (VarsUnate == VarsActive)
        {
            SetFamily A = Cofactor.CubeUnlist(T);
            Tnew = Contain.SfContain(A);
            return true;
        }
        else
        {
            Tnew = default!;
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Helper: null-terminated sorted PSet array from a SetFamily
    // -----------------------------------------------------------------------

    private static PSet[] SfListNullTermSorted(SetFamily F)
    {
        var arr = new PSet[F.Count + 1];
        for (int i = 0; i < F.Count; i++)
            arr[i] = F.GetSet(i);
        // arr[F.Count] stays PSet.Null (sentinel)
        Array.Sort(arr, 0, F.Count, Comparer<PSet>.Create(SetC.D1Order));
        return arr;
    }
}
