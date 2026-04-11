namespace EspressoCS;

using static SetFamily;
using static SetOps;
using static CubeContext;

/// <summary>
/// Canonical — find canonical cover of incompletely specified logic functions.
/// Translated from canonical.c
/// </summary>
public static class Canonical
{
    private static int[]? c_free_list;
    private static int c_free_count;
    private static int[]? r_free_list;
    private static int r_free_count;
    private static int[]? reduced_c_free_list;
    private static int reduced_c_free_count;

    private class VarInfo
    {
        public int variable;
        public int free_count;
    }

    private static VarInfo[]? unate_list;
    private static int unate_count;
    private static VarInfo[]? binate_list;
    private static int binate_count;
    private static int[]? variable_order;
    private static int variable_count;
    private static int variable_head;
    private static SetFamily? COVER;

    // -----------------------------------------------------------------------
    // find_canonical_cover — main entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// FindCanonicalCover — find the canonical cover of an ESF function.
    /// Iteratively processes each cube in the ON-set (F) by reduction and
    /// essential test, building up the canonical cover (ESC).
    /// </summary>
    public static SetFamily FindCanonicalCover(SetFamily F1, SetFamily D, SetFamily R)
    {
        var F = SfSave(F1);
        var E = SfNew(D.Count, D.SfSize);
        SfCopy(E, D);

        var ESC = SfNew(F.Count, F.SfSize);

        while (F.Count > 0)
        {
            F.Count--;
            var c = F.GetSet(F.Count);
            ResetFlag(c, SetOps.NonEssen);

            // Build extended DC cubelist
            var extended_dc = Cofactor.Cube2List(E, F);
            var d = ReduceCube(extended_dc, c);
            Stubs.FreeCubelist(extended_dc);

            if (SetEmpty(d))
            {
                SetFree(d);
                continue;
            }

            // Get sigma (signature) relative to offset
            c = GetSigma(R, d);

            // Perform essential test and reduction ordering
            var COVER = EtrOrder(F, E, R, c, d);
            SetFree(d);

            if ((c[0] & SetOps.NonEssen) != 0)
            {
                // Not essential, append to F for further processing
                F = SfAppend(F, COVER);
            }
            else
            {
                // Essential cube, add to result
                SfFree(COVER);
                SfAddSet(E, c);
                SfAddSet(ESC, c);
            }
            SetFree(c);
        }

        SfFree(F);
        SfFree(E);

        return ESC;
    }

    // -----------------------------------------------------------------------
    // Helper functions — stubs for now (from signature.c, etr.c, etc.)
    // -----------------------------------------------------------------------

    /// <summary>SetEmpty — check if a set is empty (all zeros).</summary>
    private static bool SetEmpty(PSet p)
    {
        for (int i = 0; i < CubeContext.Size; i++)
        {
            if (p[i] != 0) return false;
        }
        return true;
    }

    /// <summary>ReduceCube — reduce a cube with respect to a cubelist.</summary>
    private static PSet ReduceCube(PSet[] cubelist, PSet c)
    {
        return Reduce.ReduceOneCube(cubelist, c);
    }

    /// <summary>GetSigma — compute the signature cube relative to offset.</summary>
    private static PSet GetSigma(SetFamily R, PSet d)
    {
        return Sigma.GetSigma(R, d);
    }

    /// <summary>EtrOrder — essential test and reduction ordering.</summary>
    private static SetFamily EtrOrder(SetFamily F, SetFamily E, SetFamily R, PSet c, PSet d)
    {
        int numBinaryVars = NumBinaryVars;
        c_free_list = new int[numBinaryVars];
        r_free_list = new int[numBinaryVars];
        reduced_c_free_list = new int[numBinaryVars];
        unate_list = new VarInfo[numBinaryVars];
        binate_list = new VarInfo[numBinaryVars];
        variable_order = new int[numBinaryVars];

        for (int i = 0; i < numBinaryVars; i++)
        {
            unate_list[i] = new VarInfo();
            binate_list[i] = new VarInfo();
        }

        c_free_count = 0;
        for (int v = 0; v < numBinaryVars; v++)
        {
            int e0 = v << 1;
            int e1 = e0 + 1;
            if (IsInSet(d, e0) && IsInSet(d, e1))
            {
                c_free_list[c_free_count++] = v;
            }
        }

        r_free_count = 0;
        reduced_c_free_count = 0;
        for (int i = 0; i < c_free_count; i++)
        {
            int v = c_free_list[i];
            int e0 = v << 1;
            int e1 = e0 + 1;
            bool freeVar = true;
            for (int j = 0; j < R.Count; j++)
            {
                PSet r = R.GetSet(j);
                if (!IsInSet(r, e0) || !IsInSet(r, e1))
                {
                    freeVar = false;
                    break;
                }
            }

            if (freeVar)
            {
                r_free_list[r_free_count++] = v;
            }
            else
            {
                reduced_c_free_list[reduced_c_free_count++] = v;
            }
        }

        unate_count = 0;
        binate_count = 0;
        for (int i = 0; i < reduced_c_free_count; i++)
        {
            int v = reduced_c_free_list[i];
            int e0 = v << 1;
            int e1 = e0 + 1;
            int evenCount = 0;
            int oddCount = 0;
            int freeCount = 0;

            for (int j = 0; j < R.Count; j++)
            {
                PSet r = R.GetSet(j);
                bool odd = IsInSet(r, e0);
                bool even = IsInSet(r, e1);
                if (odd && even)
                {
                    freeCount++;
                }
                else if (odd)
                {
                    oddCount++;
                }
                else
                {
                    evenCount++;
                }
            }

            if (oddCount == 0 || evenCount == 0)
            {
                unate_list[unate_count].variable = v;
                unate_list[unate_count].free_count = freeCount;
                unate_count++;
            }
            else
            {
                binate_list[binate_count].variable = v;
                binate_list[binate_count].free_count = freeCount;
                binate_count++;
            }
        }

        Array.Sort(unate_list, 0, unate_count, new VarInfoComparer());
        Array.Sort(binate_list, 0, binate_count, new VarInfoComparer());

        variable_head = 0;
        variable_count = 0;
        for (int i = 0; i < binate_count; i++)
        {
            variable_order[variable_count++] = binate_list[i].variable;
        }
        for (int i = 0; i < unate_count; i++)
        {
            variable_order[variable_count++] = unate_list[i].variable;
        }

        COVER = SfNew(10, Size);
        BlackWhite.SetupBw(R, c);
        SetFlag(c, NonEssen);
        AuxEtrOrder(F, E, R, c, d);
        BlackWhite.FreeBw();

        SetFamily result = COVER;
        COVER = null;
        c_free_list = null;
        r_free_list = null;
        reduced_c_free_list = null;
        unate_list = null;
        binate_list = null;
        variable_order = null;
        return result;
    }

    private static void AuxEtrOrder(SetFamily F, SetFamily E, SetFamily R, PSet c, PSet d)
    {
        PSet[] localDc = Cofactor.Cube3List(F, E, COVER!);
        if (Irred.CubeIsCovered(localDc, d))
        {
            Stubs.FreeCubelist(localDc);
            return;
        }

        if (BlackWhite.BlackWhiteCheck() == 0)
        {
            Stubs.FreeCubelist(localDc);
            PSet sigmaD = Sigma.GetSigma(R, d);
            COVER = SfAddSet(COVER!, sigmaD);
            SetFree(sigmaD);
            return;
        }

        if (variable_head == variable_count)
        {
            SetFamily minterms = GetMins(d);
            for (int i = 0; i < minterms.Count; i++)
            {
                PSet dMinterm = minterms.GetSet(i);
                if (Irred.CubeIsCovered(localDc, dMinterm))
                {
                    continue;
                }

                PSet sigmaD = Sigma.GetSigma(R, dMinterm);
                if (SetpEqual(sigmaD, c))
                {
                    ResetFlag(c, NonEssen);
                    SetFree(sigmaD);
                    break;
                }
                COVER = SfAddSet(COVER!, sigmaD);
                SetFree(sigmaD);
            }
            SfFree(minterms);
            Stubs.FreeCubelist(localDc);
            return;
        }

        int vIndex = variable_order![variable_head];
        Stubs.FreeCubelist(localDc);

        int e0 = vIndex << 1;
        int e1 = e0 + 1;
        variable_head++;

        SetRemove(d, e1);
        BlackWhite.ResetBlackList();
        BlackWhite.SplitList(R, e0);
        BlackWhite.PushBlackList();
        AuxEtrOrder(F, E, R, c, d);
        if (!TestP(c, NonEssen))
        {
            return;
        }
        BlackWhite.PopBlackList();
        BlackWhite.MergeList();
        SetInsert(d, e1);

        SetRemove(d, e0);
        BlackWhite.ResetBlackList();
        BlackWhite.SplitList(R, e1);
        BlackWhite.PushBlackList();
        AuxEtrOrder(F, E, R, c, d);
        if (!TestP(c, NonEssen))
        {
            return;
        }
        BlackWhite.PopBlackList();
        BlackWhite.MergeList();
        SetInsert(d, e0);

        variable_head--;
    }

    private static SetFamily GetMins(PSet c)
    {
        SetFamily minterms = SfNew(1, Size);
        PSet dMinterm = SetNew(Size);
        SetCopy(dMinterm, c);
        SetAnd(dMinterm, dMinterm, BinaryMask);
        for (int i = NumBinaryVars; i < NumVars; i++)
        {
            for (int j = FirstPart![i]; j <= LastPart![i]; j++)
            {
                if (IsInSet(c, j))
                {
                    SetInsert(dMinterm, j);
                    minterms = SfAddSet(minterms, dMinterm);
                    SetRemove(dMinterm, j);
                }
            }
        }
        SetFree(dMinterm);
        return minterms;
    }

    private class VarInfoComparer : IComparer<VarInfo>
    {
        public int Compare(VarInfo? x, VarInfo? y)
        {
            if (x == null || y == null) return 0;
            if (x.free_count > y.free_count) return 1;
            if (x.free_count < y.free_count) return -1;
            return 0;
        }
    }
}
