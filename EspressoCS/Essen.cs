namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;

/// <summary>
/// Essen — essentiality checking for cubes in offset.
/// Translated 1:1 from essen.c.
/// Identifies essential prime implicants and marks redundant ones.
/// </summary>
public static class Essen
{
    // -----------------------------------------------------------------------
    // essential — return essential prime implicants
    // -----------------------------------------------------------------------

    /// <summary>
    /// essential — return a cover consisting of the essential prime implicants.
    /// Removes these cubes from F and adds them to D.
    /// </summary>
    public static SetFamily Essentials(ref SetFamily F, ref SetFamily D)
    {
        PSet p;
        SetFamily E;
        SetFamily oldD;

        SetFamily.SfActive(F);
        E = SfNew(10, Size);

        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            
            /* don't test a prime which EXPAND says is nonessential */
            if (!TestP(p, NonEssen))
            {
                /* only test a prime which was relatively essential */
                if (TestP(p, RelEssen))
                {
                    /* Check essentiality */
                    if (EssenCube(F, D, p))
                    {
                        if ((Globals.Debug & EspressoConstants.Essen) != 0)
                            Console.WriteLine("ESSENTIAL: {0}", PcCube1(p));
                        E = SfAddSet(E, p);
                        ResetFlag(p, Active);
                        F.ActiveCount--;
                    }
                }
            }
        }

        F = SetFamily.SfInactive(F);  /* delete the inactive cubes from F */
        oldD = D;
        D = SfJoin(D, E);             /* add the essentials to D */
        SfFree(oldD);
        return E;
    }

    // -----------------------------------------------------------------------
    // essen_cube — check if a single cube is essential
    // -----------------------------------------------------------------------

    /// <summary>
    /// essen_cube — check if a single cube is essential or not.
    /// The prime c is essential iff consensus((F u D) # c, c) u D does not contain c.
    /// </summary>
    public static bool EssenCube(SetFamily F, SetFamily D, PSet c)
    {
        SetFamily H, FD;
        PSet[] H1;
        bool essen;

        /* Append F and D together, and take the sharp-consensus with c */
        FD = SfJoin(F, D);
        H = CbConsensus(FD, c);
        SfFree(FD);

        /* Add the don't care set, and see if this covers c */
        H1 = Stubs.Cube2List(H, D);
        essen = !Stubs.CubeIsCovered(H1, c);
        Stubs.FreeCubelist(H1);

        SfFree(H);
        return essen;
    }

    // -----------------------------------------------------------------------
    // cb_consensus — compute consensus(T # c, c)
    // -----------------------------------------------------------------------

    /// <summary>
    /// cb_consensus — compute consensus(T # c, c).
    /// This is used for essentiality checking via the sharp-consensus operation.
    /// </summary>
    public static SetFamily CbConsensus(SetFamily T, PSet c)
    {
        PSet temp, p;
        SetFamily R;

        R = SfNew(T.Count * 2, Size);
        temp = SetNew(Size);
        
        for (int i = 0; i < T.Count; i++)
        {
            p = T.GetSet(i);
            if (p != c)
            {
                switch (Cdist01(p, c))
                {
                    case 0:
                        /* distance-0 needs special care */
                        R = CbConsensusDist0(R, p, c);
                        break;

                    case 1:
                        /* distance-1 is easy because no sharping required */
                        Consensus(temp, p, c);
                        R = SfAddSet(R, temp);
                        break;
                }
            }
        }
        SetFree(temp);
        return R;
    }

    // -----------------------------------------------------------------------
    // cb_consensus_dist0 — sharp-consensus for p and c when they intersect
    // -----------------------------------------------------------------------

    /// <summary>
    /// cb_consensus_dist0 — form the sharp-consensus for p and c when they intersect.
    /// This handles the special case where distance(p, c) == 0.
    /// </summary>
    public static SetFamily CbConsensusDist0(SetFamily R, PSet p, PSet c)
    {
        int var;
        bool got_one;
        PSet temp, mask;
        PSet p_diff_c = Temp![0];
        PSet p_and_c = Temp![1];

        /* If c contains p, then this gives us no information for essential test */
        if (SetpImplies(p, c))
        {
            return R;
        }

        /* For the multiple-valued variables */
        temp = SetNew(Size);
        got_one = false;
        SetDiff(p_diff_c, p, c);
        InlineAnd(p_and_c, p, c);

        for (var = NumBinaryVars; var < NumVars; var++)
        {
            /* Check if c(var) is contained in p(var) -- if so, no news */
            mask = VarMask![var];
            if (!SetpDisjoint(p_diff_c, mask))
            {
                InlineMerge(temp, c, p_and_c, mask);
                R = SfAddSet(R, temp);
                got_one = true;
            }
        }

        /* if no cube so far, add one for the intersection */
        if (!got_one && NumBinaryVars > 0)
        {
            /* Add a single cube for the intersection of p and c */
            InlineAnd(temp, p, c);
            R = SfAddSet(R, temp);
        }

        SetFree(temp);
        return R;
    }

    // -----------------------------------------------------------------------
    // Helper: ComputeEssentials (for multi-pass optimization)
    // -----------------------------------------------------------------------

    /// <summary>
    /// ComputeEssentials — compute and mark essential primes iteratively.
    /// This is a convenience wrapper that repeatedly finds essentials until none remain.
    /// </summary>
    public static SetFamily ComputeEssentials(SetFamily F, SetFamily D)
    {
        SetFamily E = SfNew(0, Size);
        SetFamily E_new;

        while (F.Count > 0)
        {
            E_new = Essentials(ref F, ref D);
            if (E_new.Count == 0)
            {
                SfFree(E_new);
                break;
            }
            E = SfAppend(E, E_new);
        }

        return E;
    }

    // -----------------------------------------------------------------------
    // Helper: ComputeNonEssentials (mark inessential primes)
    // -----------------------------------------------------------------------

    /// <summary>
    /// ComputeNonEssentials — identify and mark non-essential (redundant) primes.
    /// After EXPAND, we mark primes that are not essential.
    /// </summary>
    public static void ComputeNonEssentials(SetFamily F, SetFamily D)
    {
        for (int i = 0; i < F.Count; i++)
        {
            PSet p = F.GetSet(i);
            if (!TestP(p, NonEssen) && !EssenCube(F, D, p))
            {
                SetFlag(p, NonEssen);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper: FindEssentials (convenience method)
    // -----------------------------------------------------------------------

    /// <summary>
    /// FindEssentials — find essential primes that cover minterms not covered by other primes.
    /// </summary>
    public static SetFamily FindEssentials(SetFamily F, SetFamily D)
    {
        return Essentials(ref F, ref D);
    }

    // -----------------------------------------------------------------------
    // Helper: cube_is_covered — check if a cube is covered by a cubelist
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Helper: PcCube1 — format cube for debugging
    // -----------------------------------------------------------------------

    private static string PcCube1(PSet p)
    {
        // Simple representation of a cube for debugging
        return $"[cube at {SetOps.SetOrd(p)} bits]";
    }
}
