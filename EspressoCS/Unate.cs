namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static Contain;

// Translates the unate-complement functions from unate.c that are required by Sigma.cs.
// Included here are: unate_compl, unate_complement, sf_rev_contain, and the private
// helpers abs_covered, abs_covered_many, abs_select_restricted.

public static class Unate
{
    // -----------------------------------------------------------------------
    // unate_compl (public entry — called from Sigma.GetSigma)
    // -----------------------------------------------------------------------

    /// <summary>
    /// unate_compl — compute the unate complement of set family A.
    /// Disposes of A and returns a new, minimised family.
    /// </summary>
    public static SetFamily UnateCompl(SetFamily A)
    {
        // Record set sizes (SIZE field).
        for (int si = 0; si < A.Count; si++)
        {
            var p = A.GetSet(si);
            SetOps.PutSize(p, SetOps.SetOrd(p));
        }

        A = UnateComplement(A);
        A = SfRevContain(A);
        return A;
    }

    // -----------------------------------------------------------------------
    // unate_complement — recursive complement (disposes of A)
    // -----------------------------------------------------------------------

    private static SetFamily UnateComplement(SetFamily A)
    {
        SetFamily Abar;

        if (A.Count == 0)
        {
            // No sets — complement is the universe (one all-zeros set).
            SetFamily.SfFree(A);
            Abar = SetFamily.SfNew(1, A.SfSize);
            SetOps.SetClear(Abar.GetSet(Abar.Count++), A.SfSize);
            return Abar;
        }

        if (A.Count == 1)
        {
            // Single set — de Morgan complement: one singleton per element.
            var p = A.GetSet(0);
            Abar = SetFamily.SfNew(A.SfSize, A.SfSize);
            for (int i = 0; i < A.SfSize; i++)
            {
                if (SetOps.IsInSet(p, i))
                {
                    var p1 = Abar.GetSet(Abar.Count++);
                    SetOps.SetClear(p1, A.SfSize);
                    SetOps.SetInsert(p1, i);
                }
            }
            SetFamily.SfFree(A);
            return Abar;
        }

        // General case: select a splitting variable.
        var prestrict  = SetOps.SetNew(A.SfSize);
        uint minSetOrd = (uint)(A.SfSize + 1);

        for (int si = 0; si < A.Count; si++)
        {
            var p   = A.GetSet(si);
            uint sz = (uint)SetOps.GetSize(p);
            if (sz < minSetOrd)
            {
                SetOps.SetCopy(prestrict, p);
                minSetOrd = sz;
            }
            else if (sz == minSetOrd)
            {
                SetOps.SetOr(prestrict, prestrict, p);
            }
        }

        if (minSetOrd == 0)
        {
            // All sets are empty — result is an empty family.
            A.Count = 0;
            Abar    = A;
        }
        else if (minSetOrd == 1)
        {
            // Essential columns: add them to every set of the complement.
            Abar = UnateComplement(AbsCoveredMany(A, prestrict));
            SetFamily.SfFree(A);
            for (int si = 0; si < Abar.Count; si++)
                SetOps.SetOr(Abar.GetSet(si), Abar.GetSet(si), prestrict);
        }
        else
        {
            int maxI = AbsSelectRestricted(A, prestrict);

            // Rows not covered by maxI: recur, then insert maxI back.
            Abar = UnateComplement(AbsCovered(A, maxI));
            for (int si = 0; si < Abar.Count; si++)
                SetOps.SetInsert(Abar.GetSet(si), maxI);

            // Remove maxI from all sets of A, then recur on the reduced family.
            for (int si = 0; si < A.Count; si++)
            {
                var p = A.GetSet(si);
                if (SetOps.IsInSet(p, maxI))
                {
                    SetOps.SetRemove(p, maxI);
                    SetOps.PutSize(p, SetOps.GetSize(p) - 1);
                }
            }

            Abar = SetFamily.SfAppend(Abar, UnateComplement(A));
        }

        SetOps.SetFree(prestrict);
        return Abar;
    }

    // -----------------------------------------------------------------------
    // sf_rev_contain — remove sets that are supersets of some smaller set
    // -----------------------------------------------------------------------

    private static SetFamily SfRevContain(SetFamily A)
    {
        if (A.Count == 0) return A;

        // Recompute sizes.
        for (int i = 0; i < A.Count; i++)
            SetOps.PutSize(A.GetSet(i), SetOps.SetOrd(A.GetSet(i)));

        // Sort indices by ascending set size.
        var order = new int[A.Count];
        for (int i = 0; i < A.Count; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int sa = SetOps.GetSize(A.GetSet(a));
            int sb = SetOps.GetSize(A.GetSet(b));
            return sa != sb ? sa - sb : SetOps.Ascend(A.GetSet(a), A.GetSet(b));
        });

        // Mark sets to keep: a set is discarded if some earlier (smaller) set is a subset.
        var keep = new bool[A.Count];
        Array.Fill(keep, true);

        for (int i = 0; i < order.Length; i++)
        {
            if (!keep[order[i]]) continue;
            var pi = A.GetSet(order[i]);

            for (int j = i + 1; j < order.Length; j++)
            {
                if (!keep[order[j]]) continue;
                var pj = A.GetSet(order[j]);
                // If pi ⊆ pj (pj contains pi), then pj is dominated — remove it.
                if (SetOps.SetpImplies(pi, pj))
                    keep[order[j]] = false;
            }
        }

        // Rebuild — skip equal duplicates too (keep first occurrence only).
        int cnt = 0;
        for (int i = 0; i < order.Length; i++)
            if (keep[order[i]]) cnt++;

        var R = SetFamily.SfNew(cnt, A.SfSize);
        for (int i = 0; i < order.Length; i++)
        {
            if (!keep[order[i]]) continue;
            var dst = R.GetSet(R.Count++);
            SetOps.InlineCopy(dst, A.GetSet(order[i]));
        }

        SetFamily.SfFree(A);
        return R;
    }

    // -----------------------------------------------------------------------
    // abs_covered — rows of A not covered by column pick
    // -----------------------------------------------------------------------

    private static SetFamily AbsCovered(SetFamily A, int pick)
    {
        var Aprime = SetFamily.SfNew(A.Count, A.SfSize);
        for (int si = 0; si < A.Count; si++)
        {
            var p = A.GetSet(si);
            if (!SetOps.IsInSet(p, pick))
            {
                SetOps.InlineCopy(Aprime.GetSet(Aprime.Count++), p);
            }
        }
        return Aprime;
    }

    // -----------------------------------------------------------------------
    // abs_covered_many — rows of A disjoint from pick_set
    // -----------------------------------------------------------------------

    private static SetFamily AbsCoveredMany(SetFamily A, PSet pickSet)
    {
        var Aprime = SetFamily.SfNew(A.Count, A.SfSize);
        for (int si = 0; si < A.Count; si++)
        {
            var p = A.GetSet(si);
            if (SetOps.SetpDisjoint(p, pickSet))
            {
                SetOps.InlineCopy(Aprime.GetSet(Aprime.Count++), p);
            }
        }
        return Aprime;
    }

    // -----------------------------------------------------------------------
    // abs_select_restricted — column of max weighted count within prestrict
    // -----------------------------------------------------------------------

    private static int AbsSelectRestricted(SetFamily A, PSet prestrict)
    {
        var count = SetFamily.SfCountRestricted(A, prestrict);

        int bestVar   = -1;
        int bestCount = 0;
        for (int i = 0; i < A.SfSize; i++)
        {
            if (count[i] > bestCount)
            {
                bestVar   = i;
                bestCount = count[i];
            }
        }

        if (bestVar == -1)
            throw new InvalidOperationException("abs_select_restricted: should not have best_var == -1");

        return bestVar;
    }

    // -----------------------------------------------------------------------
    // MapCoverToUnate — map a cover to unate representation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Map a cover T to unate (single-valued) representation.
    /// Extracts only the unate variables and creates a new family
    /// where each set represents which unate variable parts are off.
    /// </summary>
    public static SetFamily MapCoverToUnate(PSet[] T)
    {
        var A = SetFamily.SfNew(CubelistSize(T), VarsUnate);
        A.Count = CubelistSize(T);

        // Initialize all sets to cleared
        for (int i = 0; i < A.Count; i++)
        {
            SetOps.SetClear(A.GetSet(i), A.SfSize);
        }

        int ncol = 0;
        for (int i = 0; i < Size; i++)
        {
            if (PartZeros![i] > 0)
            {
                System.Diagnostics.Debug.Assert(ncol <= VarsUnate);

                // Copy a column from T to A
                int wordTest = SetOps.WhichWord(i);
                int bitTest = SetOps.WhichBit(i);
                int wordSet = SetOps.WhichWord(ncol);
                int bitSet = SetOps.WhichBit(ncol);

                for (int j = 2; j < T.Length && T[j] != PSet.Null; j++)
                {
                    var p = T[j];
                    if ((p[wordTest] & (1u << bitTest)) == 0)
                    {
                        var aSet = A.GetSet(j - 2);
                        aSet[wordSet] |= (uint)(1 << bitSet);
                    }
                }

                ncol++;
            }
        }

        return A;
    }

    // -----------------------------------------------------------------------
    // MapUnateTocover — map unate representation back to cover
    // -----------------------------------------------------------------------

    /// <summary>
    /// Map a unate representation back to full cover.
    /// Reconstructs the full cover from the unate-compressed representation.
    /// </summary>
    public static SetFamily MapUnateTocover(SetFamily A)
    {
        var B = SetFamily.SfNew(A.Count, Size);
        B.Count = A.Count;

        // Find the unate variables
        var unate = new int[NumVars];
        int nunate = 0;
        for (int var = 0; var < NumVars; var++)
        {
            if (IsUnate![var])
            {
                unate[nunate++] = var;
            }
        }

        // Loop for each set of A
        for (int si = 0; si < A.Count; si++)
        {
            var p = A.GetSet(si);
            var pB = B.GetSet(si);

            // Initialize this set of B (all 1's)
            SetOps.InlineFill(pB, Size);

            // For each unate variable
            for (int ncol = 0; ncol < nunate; ncol++)
            {
                if (SetOps.IsInSet(p, ncol))
                {
                    int lp = LastPart![unate[ncol]];
                    for (int i = FirstPart![unate[ncol]]; i <= lp; i++)
                    {
                        if (PartZeros![i] == 0)
                        {
                            SetOps.SetRemove(pB, i);
                        }
                    }
                }
            }
        }

        return B;
    }

    // -----------------------------------------------------------------------
    // UnateIntersect — intersect two unate covers
    // -----------------------------------------------------------------------

    private const int MAGIC = 500;  // save 500 cubes before containment

    /// <summary>
    /// Intersect two unate covers.
    /// If largestOnly is true, return only the largest cube(s).
    /// </summary>
    public static SetFamily UnateIntersect(SetFamily A, SetFamily B, bool largestOnly)
    {
        var T = SetFamily.SfNew(MAGIC, A.SfSize);
        SetFamily? Tsave = null;
        int maxord = 0;

        for (int ai = 0; ai < A.Count; ai++)
        {
            var pi = A.GetSet(ai);

            for (int bi = 0; bi < B.Count; bi++)
            {
                var pj = B.GetSet(bi);

                var pt = T.GetSet(T.Count);
                bool save = SetAndP(pt, pi, pj);

                if (save && largestOnly)
                {
                    int ord = SetOrd(pt);
                    if (ord > maxord)
                    {
                        if (Tsave != null)
                        {
                            SetFamily.SfFree(Tsave);
                            Tsave = null;
                        }
                        T.Count = 0;
                        save = true;
                        pt = T.GetSet(0);
                        SetAnd(pt, pi, pj);
                        maxord = ord;
                    }
                    else if (ord < maxord)
                    {
                        save = false;
                    }
                }

                if (save)
                {
                    T.Count++;
                    if (T.Count >= T.Capacity)
                    {
                        T = SfContain(T);
                        Tsave = (Tsave == null) ? T : SfUnion(Tsave, T);
                        T = SetFamily.SfNew(MAGIC, A.SfSize);
                    }
                }
            }
        }

        T = SfContain(T);
        Tsave = (Tsave == null) ? T : SfUnion(Tsave, T);

        return Tsave;
    }

    // -----------------------------------------------------------------------
    // ExactMinimumCover — compute all minimal coverings (stack-based)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Compute the set of all minimal coverings for the unate function T.
    /// Uses a stack-based approach with leveled intersections.
    /// </summary>
    public static SetFamily ExactMinimumCover(SetFamily T)
    {
        if (T.Count <= 0)
            return SetFamily.SfNew(1, T.SfSize);

        int n = T.Count;
        int lev = 0;
        while (n != 0) { n >>= 1; lev++; }

        // Sort by lex order
        T = LexSort(SetFamily.SfSave(T));

        // Initialize stack
        var stack = new (SetFamily sf, int level)[32];  // 32 suffices for 2^32 cubes
        stack[0].sf = SetFamily.SfNew(1, T.SfSize);
        stack[0].level = lev;
        SetFill(stack[0].sf.GetSet(stack[0].sf.Count++), T.SfSize);

        int stackLen = 1;
        var nlast = T.GetSet(T.Count - 1);
        SetFamily temp;

        for (int ti = 0; ti < T.Count; ti++)
        {
            var p = T.GetSet(ti);

            // "Unstack" the set into a family
            int ord = SetOrd(p);
            temp = SetFamily.SfNew(ord, T.SfSize);
            for (int i = 0; i < T.SfSize; i++)
            {
                if (IsInSet(p, i))
                {
                    var p1 = SetFamily.SfNew(1, T.SfSize).GetSet(0);
                    SetFill(p1, T.SfSize);
                    SetRemove(p1, i);
                    InlineCopy(temp.GetSet(temp.Count++), p1);
                }
            }

            stack[stackLen].sf = temp;
            stack[stackLen].level = lev;
            stackLen++;

            // Pop the stack and perform (leveled) intersections
            bool equal = SetpEqual(nlast, p);
            while (stackLen > 1 && 
                   (stack[stackLen - 1].level == stack[stackLen - 2].level || equal))
            {
                temp = UnateIntersect(stack[stackLen - 1].sf, stack[stackLen - 2].sf, false);
                int lvl = System.Math.Min(stack[stackLen - 1].level, stack[stackLen - 2].level) - 1;

                if ((Globals.Debug & EspressoConstants.Mincov) != 0 && lvl < 10)
                {
                    Console.WriteLine($"# EXACT_MINCOV[{lvl}]: {temp.Count,4} = {stack[stackLen - 1].sf.Count,4} x {stack[stackLen - 2].sf.Count,4}");
                    Console.Out.Flush();
                }

                SetFamily.SfFree(stack[stackLen - 2].sf);
                SetFamily.SfFree(stack[stackLen - 1].sf);
                stack[stackLen - 2].sf = temp;
                stack[stackLen - 2].level = lvl;
                stackLen--;
            }
        }

        temp = stack[0].sf;
        var p1fill = SetFill(SetNew(T.SfSize), T.SfSize);
        for (int i = 0; i < temp.Count; i++)
        {
            var q = temp.GetSet(i);
            InlineDiff(q, p1fill, q);
        }

        SetFree(p1fill);
        SetFamily.SfFree(T);

        return temp;
    }

    // -----------------------------------------------------------------------
    // LexSort — sort a family in lexicographic order
    // -----------------------------------------------------------------------

    private static SetFamily LexSort(SetFamily A)
    {
        var indices = new int[A.Count];
        for (int i = 0; i < A.Count; i++) indices[i] = i;

        System.Array.Sort(indices, (i, j) =>
        {
            return Ascend(A.GetSet(i), A.GetSet(j));
        });

        var R = SetFamily.SfNew(A.Count, A.SfSize);
        for (int i = 0; i < A.Count; i++)
        {
            InlineCopy(R.GetSet(R.Count++), A.GetSet(indices[i]));
        }

        SetFamily.SfFree(A);
        return R;
    }

    // -----------------------------------------------------------------------
    // Helper: CubelistSize — count non-null elements
    // -----------------------------------------------------------------------

    private static int CubelistSize(PSet[] T)
    {
        int count = 0;
        for (int i = 2; i < T.Length && T[i] != PSet.Null; i++)
            count++;
        return count;
    }
}
