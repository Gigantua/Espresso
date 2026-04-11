namespace EspressoCS;

using static SetOps;
using static CubeContext;

/// <summary>
/// Contain — set containment, deduplication, and family merge operations.
/// Translated 1:1 from contain.c.
/// </summary>
public static class Contain
{
    // -----------------------------------------------------------------------
    // sf_contain — remove cubes contained by any larger cube in the family
    // -----------------------------------------------------------------------

    public static SetFamily SfContain(SetFamily A)
    {
        PSet[] A1 = SfSort(A, Descend);
        int cnt   = RmEqual(A1, Descend);
        cnt       = RmContain(A1);
        SetFamily R = SfUnlist(A1, cnt, A.SfSize);
        SetFamily.SfFree(A);
        return R;
    }

    // -----------------------------------------------------------------------
    // sf_rev_contain — remove cubes that contain some smaller cube in the family
    // -----------------------------------------------------------------------

    public static SetFamily SfRevContain(SetFamily A)
    {
        PSet[] A1 = SfSort(A, Ascend);
        int cnt   = RmEqual(A1, Ascend);
        cnt       = RmRevContain(A1);
        SetFamily R = SfUnlist(A1, cnt, A.SfSize);
        SetFamily.SfFree(A);
        return R;
    }

    // -----------------------------------------------------------------------
    // sf_ind_contain — containment with row-index tracking
    // -----------------------------------------------------------------------

    public static SetFamily SfIndContain(SetFamily A, int[] rowIndices)
    {
        PSet[] A1 = SfSort(A, Descend);
        int cnt   = RmEqual(A1, Descend);
        cnt       = RmContain(A1);
        SetFamily R = SfIndUnlist(A1, cnt, A.SfSize, rowIndices, A.GetSet(0));
        SetFamily.SfFree(A);
        return R;
    }

    // -----------------------------------------------------------------------
    // sf_dupl — delete duplicate sets in a set family
    // -----------------------------------------------------------------------

    public static SetFamily SfDupl(SetFamily A)
    {
        PSet[] A1 = SfSort(A, Descend);
        int cnt   = RmEqual(A1, Descend);
        SetFamily R = SfUnlist(A1, cnt, A.SfSize);
        SetFamily.SfFree(A);
        return R;
    }

    // -----------------------------------------------------------------------
    // sf_union — contained union of two already-sorted set families
    // -----------------------------------------------------------------------

    public static SetFamily SfUnion(SetFamily A, SetFamily B)
    {
        PSet[] A1 = SfList(A);
        PSet[] B1 = SfList(B);
        PSet[] E1 = new PSet[Math.Max(A.Count, B.Count) + 1];

        int cnt  = Rm2Equal(A1, B1, E1, Descend);
        cnt     += Rm2Contain(A1, B1) + Rm2Contain(B1, A1);
        SetFamily R = SfMerge(A1, B1, E1, cnt, A.SfSize);
        SetFamily.SfFree(A);
        SetFamily.SfFree(B);
        return R;
    }

    // -----------------------------------------------------------------------
    // dist_merge — OR cubes with mask, then delete duplicates
    // -----------------------------------------------------------------------

    public static SetFamily DistMerge(SetFamily A, PSet mask)
    {
        SetCopy(Temp![0], mask);   // put mask in temp[0] for D1Order
        PSet[] A1   = SfSort(A, SetC.D1Order);
        int cnt     = D1RmEqual(A1, SetC.D1Order);
        SetFamily R = SfUnlist(A1, cnt, A.SfSize);
        SetFamily.SfFree(A);
        return R;
    }

    // -----------------------------------------------------------------------
    // d1merge — distance-1 merge over variable var
    // -----------------------------------------------------------------------

    public static SetFamily D1Merge(SetFamily A, int var)
    {
        return DistMerge(A, VarMask![var]);
    }

    // -----------------------------------------------------------------------
    // d1_rm_equal — merge adjacent equal cubes (under mask in temp[0])
    // -----------------------------------------------------------------------

    public static int D1RmEqual(PSet[] A1, Comparison<PSet> compare)
    {
        int dest = 0;
        if (!A1[0].IsNull)
        {
            int i = 0, j = 1;
            for (; !A1[j].IsNull; j++)
            {
                if (compare(A1[i], A1[j]) == 0)
                {
                    SetOr(A1[i], A1[i], A1[j]);   // merge equal cubes
                }
                else
                {
                    A1[dest++] = A1[i];
                    i = j;
                }
            }
            A1[dest++] = A1[i];
        }
        A1[dest] = PSet.Null;
        return dest;
    }

    // -----------------------------------------------------------------------
    // rm_equal — remove duplicate cubes from a sorted array
    // -----------------------------------------------------------------------

    public static int RmEqual(PSet[] A1, Comparison<PSet> compare)
    {
        int pdest = 0;
        if (!A1[0].IsNull)
        {
            int p = 1;
            for (; !A1[p].IsNull; p++)
                if (compare(A1[p], A1[p - 1]) != 0)
                    A1[pdest++] = A1[p - 1];
            A1[pdest++] = A1[p - 1];
            A1[pdest]   = PSet.Null;
        }
        return pdest;
    }

    // -----------------------------------------------------------------------
    // rm_contain — remove cubes contained by some larger cube in the sorted array
    // -----------------------------------------------------------------------

    public static int RmContain(PSet[] A1)
    {
        int pdest    = 0;
        int pcheck   = 0;   // index up to which we check for containment
        int lastSize = -1;

        for (int pa = 0; !A1[pa].IsNull;)
        {
            PSet a = A1[pa++];

            if (GetSize(a) != lastSize)
            {
                lastSize = GetSize(a);
                pcheck   = pdest;
            }

            bool contained = false;
            for (int pb = 0; pb < pcheck; pb++)
            {
                PSet b = A1[pb];
                if (!SetpImplies(a, b)) continue;
                contained = true;
                break;
            }

            if (!contained)
                A1[pdest++] = a;
        }

        A1[pdest] = PSet.Null;
        return pdest;
    }

    // -----------------------------------------------------------------------
    // rm_rev_contain — remove cubes that contain some smaller cube
    // -----------------------------------------------------------------------

    public static int RmRevContain(PSet[] A1)
    {
        int pdest    = 0;
        int pcheck   = 0;
        int lastSize = -1;

        for (int pa = 0; !A1[pa].IsNull;)
        {
            PSet a = A1[pa++];

            if (GetSize(a) != lastSize)
            {
                lastSize = GetSize(a);
                pcheck   = pdest;
            }

            bool contained = false;
            for (int pb = 0; pb < pcheck; pb++)
            {
                PSet b = A1[pb];
                if (!SetpImplies(b, a)) continue;
                contained = true;
                break;
            }

            if (!contained)
                A1[pdest++] = a;
        }

        A1[pdest] = PSet.Null;
        return pdest;
    }

    // -----------------------------------------------------------------------
    // rm2_equal — split two sorted arrays by equality (equal cubes go to E1)
    // -----------------------------------------------------------------------

    public static int Rm2Equal(PSet[] A1, PSet[] B1, PSet[] E1,
                                Comparison<PSet> compare)
    {
        int pda = 0, pdb = 0, pde = 0;
        int a1  = 0, b1  = 0;

        while (!A1[a1].IsNull && !B1[b1].IsNull)
        {
            switch (compare(A1[a1], B1[b1]))
            {
                case -1: A1[pda++] = A1[a1++]; break;
                case  0: E1[pde++] = A1[a1++]; b1++; break;
                case  1: B1[pdb++] = B1[b1++]; break;
            }
        }

        while (!A1[a1].IsNull) A1[pda++] = A1[a1++];
        while (!B1[b1].IsNull) B1[pdb++] = B1[b1++];

        A1[pda] = PSet.Null;
        B1[pdb] = PSet.Null;
        E1[pde] = PSet.Null;

        return pde;
    }

    // -----------------------------------------------------------------------
    // rm2_contain — remove from A1 any cube contained by a larger cube in B1
    // -----------------------------------------------------------------------

    public static int Rm2Contain(PSet[] A1, PSet[] B1)
    {
        int pdest = 0;

        for (int pa = 0; !A1[pa].IsNull;)
        {
            PSet a          = A1[pa++];
            bool contained  = false;

            for (int pb = 0; !B1[pb].IsNull && GetSize(B1[pb]) > GetSize(a); pb++)
            {
                PSet b = B1[pb];
                if (!SetpImplies(a, b)) continue;
                contained = true;
                break;
            }

            if (!contained)
                A1[pdest++] = a;
        }

        A1[pdest] = PSet.Null;
        return pdest;
    }

    // -----------------------------------------------------------------------
    // sf_sort — build a null-terminated sorted array of PSet references
    // (updates the SIZE field of each set as a side-effect)
    // -----------------------------------------------------------------------

    public static PSet[] SfSort(SetFamily A, Comparison<PSet> compare)
    {
        var A1 = new PSet[A.Count + 1];   // +1 for null sentinel
        for (int i = 0; i < A.Count; i++)
        {
            var p = A.GetSet(i);
            PutSize(p, SetOrd(p));
            A1[i] = p;
        }
        // A1[A.Count] remains PSet.Null — sentinel

        Array.Sort(A1, 0, A.Count, Comparer<PSet>.Create(compare));
        return A1;
    }

    // -----------------------------------------------------------------------
    // sf_list — build a null-terminated array of PSet references (no sort)
    // -----------------------------------------------------------------------

    public static PSet[] SfList(SetFamily A)
    {
        var A1 = new PSet[A.Count + 1];
        for (int i = 0; i < A.Count; i++)
            A1[i] = A.GetSet(i);
        A1[A.Count] = PSet.Null;   // sentinel
        return A1;
    }

    // -----------------------------------------------------------------------
    // sf_unlist — build a SetFamily from a null-terminated sorted pointer array
    // -----------------------------------------------------------------------

    public static SetFamily SfUnlist(PSet[] A1, int totcnt, int size)
    {
        var R   = SetFamily.SfNew(totcnt, size);
        R.Count = totcnt;
        for (int pa = 0, i = 0; !A1[pa].IsNull; pa++, i++)
            InlineCopy(R.GetSet(i), A1[pa]);
        return R;
    }

    // -----------------------------------------------------------------------
    // sf_ind_unlist — sf_unlist with row-index remapping
    // -----------------------------------------------------------------------

    public static SetFamily SfIndUnlist(PSet[] A1, int totcnt, int size,
                                         int[] rowIndices, PSet pfirst)
    {
        var R   = SetFamily.SfNew(totcnt, size);
        R.Count = totcnt;

        var newRowIndices = new int[totcnt];
        int prOffset = 0;

        for (int pa = 0, i = 0; !A1[pa].IsNull; pa++, prOffset += R.WSize, i++)
        {
            PSet p = A1[pa];
            InlineCopy(new PSet(R.Data, prOffset), p);
            // (p._o - pfirst._o) / R.WSize gives original row index in A
            newRowIndices[i] = rowIndices[(p._o - pfirst._o) / R.WSize];
        }

        for (int i = 0; i < totcnt; i++)
            rowIndices[i] = newRowIndices[i];

        return R;
    }

    // -----------------------------------------------------------------------
    // sf_merge — 3-way descending merge of null-terminated pointer arrays
    // -----------------------------------------------------------------------

    public static SetFamily SfMerge(PSet[] A1, PSet[] B1, PSet[] E1,
                                     int totcnt, int size)
    {
        var R   = SetFamily.SfNew(totcnt, size);
        R.Count = totcnt;

        // Use three (array, cursor-index) pairs
        PSet[][] arrs = { A1, B1, E1 };
        int[]    idxs = { 0,  0,  0  };

        // Bubble-sort the three head elements into descending order
        for (int ii = 0; ii < 2; ii++)
            for (int jj = ii + 1; jj < 3; jj++)
                if (Desc1(arrs[ii][idxs[ii]], arrs[jj][idxs[jj]]) > 0)
                    (arrs[ii], arrs[jj]) = (arrs[jj], arrs[ii]);

        int pmin = 0, pmid = 1, pmax = 2;
        int prOffset = 0;

        while (!arrs[pmin][idxs[pmin]].IsNull)
        {
            PSet ps = arrs[pmin][idxs[pmin]++];
            InlineCopy(new PSet(R.Data, prOffset), ps);
            prOffset += R.WSize;

            // Re-establish pmin as the array with the current maximum head
            if (Desc1(arrs[pmin][idxs[pmin]], arrs[pmax][idxs[pmax]]) > 0)
            {
                int tmp = pmax; pmax = pmin; pmin = pmid; pmid = tmp;
            }
            else if (Desc1(arrs[pmin][idxs[pmin]], arrs[pmid][idxs[pmid]]) > 0)
            {
                int tmp = pmin; pmin = pmid; pmid = tmp;
            }
        }

        return R;
    }
}
