namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;

/// <summary>
/// Gasp — GASP (Generalized Algebraic Simplification using Primes) algorithm.
/// Translated from gasp.c.
/// 
/// The "last_gasp" heuristic computes the reduction of each cube in
/// the cover (without replacement) and then performs an expansion of
/// these cubes. The cubes which expand to cover some other cube are
/// added to the original cover and irredundant finds a minimal subset.
/// 
/// super_gasp is a variation on this strategy which extracts a minimal
/// subset from the set of all prime implicants which cover all
/// maximally reduced cubes.
/// </summary>
public static class Gasp
{
    // -----------------------------------------------------------------------
    // reduce_gasp — compute the maximal reduction of each cube of F
    // -----------------------------------------------------------------------

    /// <summary>
    /// reduce_gasp — compute the maximal reduction of each cube of F.
    /// 
    /// If a cube does not reduce, it remains prime; otherwise, it is marked
    /// as nonprime. If the cube is redundant (should NEVER happen here) 
    /// we throw an exception.
    /// 
    /// A cover with all of the cubes of F is returned. Those that did
    /// reduce are marked "NONPRIME"; those that didn't reduce are marked "PRIME".
    /// The cubes are in the same order as in F.
    /// </summary>
    private static SetFamily ReduceGasp(SetFamily F, SetFamily D)
    {
        PSet p, cunder;
        PSet[] FD;
        SetFamily G;

        G = SfNew(F.Count, F.SfSize);
        FD = Cofactor.Cube2List(F, D);

        /* Reduce cubes of F without replacement */
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            cunder = Reduce.ReduceOneCube(FD, p);
            
            if (SetpEmpty(cunder))
            {
                throw new InvalidOperationException("empty reduction in reduce_gasp, shouldn't happen");
            }
            else if (SetpEqual(cunder, p))
            {
                SetFlag(cunder, Prime);         /* just to make sure */
                G = SfAddSet(G, p);             /* it did not reduce ... */
            }
            else
            {
                ResetFlag(cunder, Prime);          /* it reduced ... */
                G = SfAddSet(G, cunder);
            }

            if ((Globals.Debug & EspressoConstants.Gasp) != 0)
            {
                Console.WriteLine($"REDUCE_GASP: {Ps1(p)} reduced to {Ps1(cunder)}");
            }

            SetFree(cunder);
        }

        Stubs.FreeCubelist(FD);
        return G;
    }

    // -----------------------------------------------------------------------
    // expand_gasp — expand each nonprime cube of F into a prime implicant
    // -----------------------------------------------------------------------

    /// <summary>
    /// expand_gasp — expand each nonprime cube of F into a prime implicant.
    /// 
    /// The gasp strategy differs in that only those cubes which expand to
    /// cover some other cube are saved; also, all cubes are expanded
    /// regardless of whether they become covered or not.
    /// </summary>
    public static SetFamily ExpandGasp(SetFamily F, SetFamily D, SetFamily R, SetFamily Foriginal)
    {
        int c1index;
        SetFamily G;

        /* Try to expand each nonprime and noncovered cube */
        G = SfNew(10, F.SfSize);
        for (c1index = 0; c1index < F.Count; c1index++)
        {
            Expand1Gasp(F, D, R, Foriginal, c1index, ref G);
        }
        G = Contain.SfDupl(G);
        G = Expand.ExpandCover(G, R, 0);  /* Make them prime ! */
        return G;
    }

    // -----------------------------------------------------------------------
    // expand1_gasp — Expand a single cube against the OFF-set, using gasp strategy
    // -----------------------------------------------------------------------

    /// <summary>
    /// expand1_gasp — expand a single cube against the OFF-set using the GASP strategy.
    /// 
    /// Attempts to expand the cube at c1index to cover other nonprime cubes
    /// and checks if the expansion can cover the reduced version of those cubes
    /// when the expanded cube is used in place of the original.
    /// </summary>
    public static void Expand1Gasp(SetFamily F, SetFamily D, SetFamily R, 
        SetFamily Foriginal, int c1index, ref SetFamily G)
    {
        int c2index;
        PSet p;
        PSet c2under;
        PSet RAISE, FREESET, temp;
        PSet c2essential;
        PSet[] FD;
        SetFamily F1;

        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
        {
            Console.WriteLine($"\nEXPAND1_GASP:\t{Ps1(F.GetSet(c1index))}");
        }

        RAISE = SetNew(Size);
        FREESET = SetNew(Size);
        temp = SetNew(Size);

        /* Initialize the OFF-set */
        R.ActiveCount = R.Count;
        for (int i = 0; i < R.Count; i++)
        {
            p = R.GetSet(i);
            SetFlag(p, Active);
        }

        /* Initialize the reduced ON-set, all nonprime cubes become active */
        F.ActiveCount = F.Count;
        for (c2index = 0; c2index < F.Count; c2index++)
        {
            c2under = F.GetSet(c2index);
            if (c1index == c2index || TestP(c2under, Prime))
            {
                F.ActiveCount--;
                ResetFlag(c2under, Active);
            }
            else
            {
                SetFlag(c2under, Active);
            }
        }

        /* Initialize the raising and unassigned sets */
        SetCopy(RAISE, F.GetSet(c1index));
        SetDiff(FREESET, FullSet, RAISE);

        /* Determine parts which must be lowered */
        Essentiality.EssenParts(R, F, RAISE, FREESET);

        /* Determine parts which can always be raised */
        Essentiality.EssenRaising(R, RAISE, FREESET);

        /* See which, if any, of the reduced cubes we can cover */
        for (c2index = 0; c2index < F.Count; c2index++)
        {
            c2under = F.GetSet(c2index);
            if (TestP(c2under, Active))
            {
                /* See if this cube can be covered by an expansion */
                if (SetpImplies(c2under, RAISE) || 
                    FeasiblyCovered(R, c2under, RAISE, temp))
                {
                    /* See if c1under can be expanded to cover c2 reduced against
                     * (F - c1) u c1under; if so, c2 can definitely be removed !
                     */

                    /* Copy F and replace c1 with c1under */
                    F1 = SfSave(Foriginal);
                    SetCopy(F1.GetSet(c1index), F.GetSet(c1index));

                    /* Reduce c2 against ((F - c1) u c1under) */
                    FD = Cofactor.Cube2List(F1, D);
                    c2essential = Reduce.ReduceOneCube(FD, F1.GetSet(c2index));
                    Stubs.FreeCubelist(FD);
                    SfFree(F1);

                    /* See if c2essential is covered by an expansion of c1under */
                    if (FeasiblyCovered(R, c2essential, RAISE, temp))
                    {
                        SetOr(temp, RAISE, c2essential);
                        ResetFlag(temp, Prime);  /* cube not prime */
                        G = SfAddSet(G, temp);
                    }
                    SetFree(c2essential);
                }
            }
        }

        SetFree(RAISE);
        SetFree(FREESET);
        SetFree(temp);
    }

    // -----------------------------------------------------------------------
    // irred_gasp — Add new primes to F and find an irredundant subset
    // -----------------------------------------------------------------------

    /// <summary>
    /// irred_gasp — add new primes G to F and find an irredundant subset.
    /// G is disposed of.
    /// </summary>
    private static SetFamily IrredGasp(SetFamily F, SetFamily D, SetFamily G)
    {
        if (G.Count != 0)
        {
            F = Irred.Irredundant(SfAppend(F, G), D);
        }
        else
        {
            SfFree(G);
        }
        return F;
    }

    // -----------------------------------------------------------------------
    // last_gasp — main GASP entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// last_gasp — compute the reduction of each cube in the cover (without
    /// replacement) and then perform an expansion of these cubes. The cubes 
    /// which expand to cover some other cube are added to the original cover 
    /// and irredundant finds a minimal subset.
    /// </summary>
    public static SetFamily LastGasp(SetFamily F, SetFamily D, SetFamily R, ref Cost cost)
    {
        SetFamily G, G1;

        long t = Stubs.PTime();
        G = ReduceGasp(F, D);
        CvrMisc.Totals(t, Globals.GreduceTime, G, cost);

        t = Stubs.PTime();
        G1 = ExpandGasp(G, D, R, F);
        CvrMisc.Totals(t, Globals.GexpandTime, G1, cost);

        SfFree(G);

        t = Stubs.PTime();
        F = IrredGasp(F, D, G1);
        CvrMisc.Totals(t, Globals.GirredTime, F, cost);
        return F;
    }

    // -----------------------------------------------------------------------
    // super_gasp — alternative GASP using all primes
    // -----------------------------------------------------------------------

    /// <summary>
    /// super_gasp — variation on GASP strategy which extracts a minimal
    /// subset from the set of all prime implicants which cover all
    /// maximally reduced cubes.
    /// </summary>
    public static SetFamily SuperGasp(SetFamily F, SetFamily D, SetFamily R, ref Cost cost)
    {
        SetFamily G, G1;

        long t = Stubs.PTime();
        G = ReduceGasp(F, D);
        CvrMisc.Totals(t, Globals.GreduceTime, G, cost);

        t = Stubs.PTime();
        G1 = Primes.AllPrimes(G, R);
        CvrMisc.Totals(t, Globals.GexpandTime, G1, cost);

        SfFree(G);

        G = Contain.SfDupl(SfAppend(F, G1));

        t = Stubs.PTime();
        F = Irred.Irredundant(G, D);
        CvrMisc.Totals(t, Globals.GirredTime, F, cost);
        return F;
    }

    // -----------------------------------------------------------------------
    // Helper functions
    // -----------------------------------------------------------------------

    /// <summary>
    /// feasibly_covered — check if a cube can be feasibly covered by an
    /// expansion from RAISE against the OFF-set R, allowing intermediate point temp.
    /// </summary>
    private static bool FeasiblyCovered(SetFamily R, PSet p, PSet RAISE, PSet temp)
    {
        PSet r = SetOr(Temp![0], RAISE, p);
        int dist;

        SetCopy(temp, EmptySet);
        for (int i = 0; i < R.Count; i++)
        {
            PSet off = R.GetSet(i);
            if (!TestP(off, Active))
                continue;

            dist = Cdist01(off, r);
            if (dist > 1)
                continue;

            if (dist == 0)
            {
                return false;
            }
            ForceLower(temp, off, r);
        }

        return true;
    }
}

