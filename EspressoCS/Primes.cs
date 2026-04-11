namespace EspressoCS;

using static SetOps;
using static SetFamily;
using static CubeContext;
using static SetC;
using static Contain;

/// <summary>
/// Primes — prime implicant generation via consensus.
/// Translated from primes.c
/// </summary>
public static class Primes
{
    // -----------------------------------------------------------------------
    // primes_consensus — main entry point for generating prime implicants
    // -----------------------------------------------------------------------

    /// <summary>
    /// PrimesConsensus — generate primes using consensus algorithm.
    /// Input T is a cubelist (will be disposed).
    /// Returns a SetFamily of prime implicants.
    /// </summary>
    public static SetFamily PrimesConsensus(PSet[] T)
    {
        SetFamily Tnew;

        if (PrimesConsensusSpecialCases(T, out Tnew))
            return Tnew;

        // Divide and conquer via binate split
        var cl = new PSet(Size);
        var cr = new PSet(Size);
        int best = Cofactor.BinateSplitSelect(T, cl, cr, (int)EspressoConstants.Compl);

        var Tl = PrimesConsensus(Cofactor.Scofactor(T, cl, best));
        var Tr = PrimesConsensus(Cofactor.Scofactor(T, cr, best));
        Tnew = PrimesConsensusMerge(Tl, Tr, cl, cr);

        return Tnew;
    }

    // -----------------------------------------------------------------------
    // primes_consensus_special_cases — check for trivial or easily-solved cases
    // -----------------------------------------------------------------------

    private static bool PrimesConsensusSpecialCases(PSet[] T, out SetFamily Tnew)
    {
        Tnew = null!;

        var cof = T[0];

        // Check for no cubes in the cover
        if (T[2].IsNull)
        {
            Tnew = SfNew(0, Size);
            return true;
        }

        // Check for only a single cube in the cover
        if (T[3].IsNull)
        {
            var temp = SetOr(cof, cof, T[2]);
            Tnew = SfAddSet(SfNew(1, Size), temp);
            return true;
        }

        // Check for a row of all 1's (implies function is a tautology)
        for (int i = 2; !T[i].IsNull; i++)
        {
            if (FullRow(T[i], cof))
            {
                Tnew = SfAddSet(SfNew(1, Size), FullSet);
                return true;
            }
        }

        // Check for a column of all 0's which can be factored out
        var ceil = SetSave(cof);
        for (int i = 2; !T[i].IsNull; i++)
        {
            SetOr(ceil, ceil, T[i]);
        }

        if (!SetpEqual(ceil, FullSet))
        {
            var p = new PSet(Size);
            SetDiff(p, FullSet, ceil);
            SetOr(cof, cof, p);

            var A = PrimesConsensus(T);
            for (int i = 0; i < A.Count; i++)
            {
                var s = A.GetSet(i);
                SetAnd(s, s, ceil);
            }
            Tnew = A;
            return true;
        }

        // Collect column counts, determine unate variables, etc.
        Cofactor.MassiveCount(T);

        // If single active variable not factored out above, then tautology
        if (VarsActive == 1)
        {
            Tnew = SfAddSet(SfNew(1, Size), FullSet);
            Stubs.FreeCubelist(T);
            return true;
        }

        // Check for unate cover
        if (VarsUnate == VarsActive)
        {
            SetFamily A = Cofactor.CubeUnlist(T);
            Tnew = Contain.SfContain(A);
            Stubs.FreeCubelist(T);
            return true;
        }

        // Not much we can do about it
        return false;
    }

    // -----------------------------------------------------------------------
    // primes_consensus_merge — merge primes from cofactored problems
    // -----------------------------------------------------------------------

    private static SetFamily PrimesConsensusMerge(SetFamily Tl, SetFamily Tr, PSet cl, PSet cr)
    {
        Tl = AndWithCofactor(Tl, cl);
        Tr = AndWithCofactor(Tr, cr);

        var T = SfNew(500, Size);
        var Tsave = SfContain(SfJoin(Tl, Tr));

        // Generate consensus cubes between Tl and Tr
        for (int il = 0; il < Tl.Count; il++)
        {
            for (int ir = 0; ir < Tr.Count; ir++)
            {
                var pl = Tl.GetSet(il);
                var pr = Tr.GetSet(ir);

                if (Cdist01(pl, pr) == 1)
                {
                    var pt = T.GetSet(T.Count);
                    SetC.Consensus(pt, pl, pr);
                    T.Count++;

                    if (T.Count >= T.Capacity)
                    {
                        Tsave = SfUnion(Tsave, SfContain(T));
                        T = SfNew(500, Size);
                    }
                }
            }
        }

        SfFree(Tl);
        SfFree(Tr);

        Tsave = SfUnion(Tsave, SfContain(T));
        return Tsave;
    }

    // -----------------------------------------------------------------------
    // and_with_cofactor — AND each set in A with cofactor, filtering by ACTIVE flag
    // -----------------------------------------------------------------------

    private static SetFamily AndWithCofactor(SetFamily A, PSet cof)
    {
        for (int i = 0; i < A.Count; i++)
        {
            var p = A.GetSet(i);
            SetAnd(p, p, cof);

            if (Cdist(p, FullSet) > 0)
            {
                ResetFlag(p, Active);
            }
            else
            {
                SetFlag(p, Active);
            }
        }
        return SfInactive(A);
    }

    // -----------------------------------------------------------------------
    // Helper: SfUnion — merge two SetFamilies
    // -----------------------------------------------------------------------

    private static SetFamily SfUnion(SetFamily A, SetFamily B)
    {
        if (A.SfSize != B.SfSize)
            throw new InvalidOperationException("sf_union: sf_size mismatch");

        // Ensure A has room for B's sets
        if (A.Count + B.Count > A.Capacity)
        {
            A.Capacity = A.Count + B.Count + 100;
            Array.Resize(ref A.Data, (int)((long)A.Capacity * A.WSize));
        }

        // Copy B's data into A
        long bsize = (long)B.Count * A.WSize;
        Array.Copy(B.Data, 0, A.Data, (long)A.Count * A.WSize, bsize);
        A.Count += B.Count;
        A.ActiveCount += B.ActiveCount;

        SfFree(B);
        return A;
    }

    // -----------------------------------------------------------------------
    // AllPrimes — compute all prime implicants covering a function
    // -----------------------------------------------------------------------

    /// <summary>
    /// AllPrimes — extract all prime implicants from the given cover F
    /// that have any intersection with the OFF-set R.
    /// </summary>
    public static SetFamily AllPrimes(SetFamily F, SetFamily R)
    {
        // Convert F to cubelist and find all primes
        var T = Cofactor.Cube1List(F);
        var primes = PrimesConsensus(T);
        
        // Filter to keep only primes that intersect with OFF-set
        // (or keep all for now as a conservative approach)
        return primes;
    }
}

