namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;

/// <summary>
/// Expand — cube expansion to prime implicants.
/// Translated 1:1 from expand.c.
/// Expands non-prime cubes into prime implicants using the Espresso-II expansion algorithm.
/// </summary>
public static class Expand
{
    // -----------------------------------------------------------------------
    // expand — expand each non-prime cube into a prime implicant
    // -----------------------------------------------------------------------

    /// <summary>
    /// expand — expand each non-prime cube of F into a prime implicant.
    /// If nonsparse is true, only non-sparse variables are expanded.
    /// </summary>
    public static SetFamily ExpandCover(SetFamily F, SetFamily R, int nonsparse)
    {
        PSet p;
        PSet RAISE, FREESET, INIT_LOWER, SUPER_CUBE, OVEREXPANDED_CUBE;
        int var, num_covered;
        bool change;

        /* Order the cubes according to "chewing away from edges" */
        if (Globals.UseRandomOrder)
            F = RandomOrder(F);
        else
            F = MiniSort(F, Ascend);

        /* Allocate memory for variables needed by expand1() */
        RAISE = SetNew(Size);
        FREESET = SetNew(Size);
        INIT_LOWER = SetNew(Size);
        SUPER_CUBE = SetNew(Size);
        OVEREXPANDED_CUBE = SetNew(Size);

        /* Setup the initial lowering set (differs only for nonsparse) */
        if (nonsparse != 0)
            for (var = 0; var < NumVars; var++)
                if (SparseVar![var] != 0)
                    SetOr(INIT_LOWER, INIT_LOWER, VarMask![var]);

        /* Mark all cubes as not covered, and maybe essential */
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            ResetFlag(p, Covered);
            ResetFlag(p, NonEssen);
        }

        /* Try to expand each non-prime and non-covered cube */
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            /* do not expand if PRIME or if covered by previous expansion */
            if (!TestP(p, Prime) && !TestP(p, Covered))
            {
                /* expand the cube p, result is RAISE */
                ExpandOneCube(R, F, RAISE, FREESET, OVEREXPANDED_CUBE, SUPER_CUBE,
                    INIT_LOWER, out num_covered, p);
                
                if ((Globals.Debug & EspressoConstants.Expand) != 0)
                    Console.WriteLine("EXPAND: {0} (covered {1})", PcCube1(p), num_covered);
                
                SetCopy(p, RAISE);
                SetFlag(p, Prime);
                ResetFlag(p, Covered);  /* not really necessary */

                /* See if we generated an inessential prime */
                if (num_covered == 0 && !SetpEqual(p, OVEREXPANDED_CUBE))
                {
                    SetFlag(p, NonEssen);
                }
            }
        }

        /* Delete any cubes of F which became covered during expansion */
        F.ActiveCount = 0;
        change = false;
        for (int i = 0; i < F.Count; i++)
        {
            p = F.GetSet(i);
            if (TestP(p, Covered))
            {
                ResetFlag(p, Active);
                change = true;
            }
            else
            {
                SetFlag(p, Active);
                F.ActiveCount++;
            }
        }
        if (change)
            F = SfInactive(F);

        SetFree(RAISE);
        SetFree(FREESET);
        SetFree(INIT_LOWER);
        SetFree(SUPER_CUBE);
        SetFree(OVEREXPANDED_CUBE);
        return F;
    }

    // -----------------------------------------------------------------------
    // expand1 — expand a single cube against the OFF-set
    // -----------------------------------------------------------------------

    public static void ExpandOneCube(SetFamily BB, SetFamily CC, PSet RAISE, PSet FREESET, 
                                      PSet OVEREXPANDED_CUBE, PSet SUPER_CUBE, PSet INIT_LOWER, 
                                      out int num_covered, PSet c)
    {
        int bestindex;

        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
            Console.WriteLine("\nEXPAND1:\t{0}", PcCube1(c));

        /* initialize BB and CC */
        SetFlag(c, Prime);  /* don't try to cover ourself */
        SetupBbCc(BB, CC);

        /* initialize count of # cubes covered, and the supercube of them */
        num_covered = 0;
        SetCopy(SUPER_CUBE, c);

        /* Initialize the lowering, raising and unassigned sets */
        SetCopy(RAISE, c);
        SetDiff(FREESET, FullSet, RAISE);

        /* If some parts are forced into lowering set, remove them */
        if (!SetpEmpty(INIT_LOWER))
        {
            SetDiff(FREESET, FREESET, INIT_LOWER);
            ElimLowering(BB, CC, RAISE, FREESET);
        }

        /* Determine what can be raised, and return the over-expanded cube */
        EssenParts(BB, CC, RAISE, FREESET);
        SetOr(OVEREXPANDED_CUBE, RAISE, FREESET);

        /* While there are still cubes which can be covered, cover them ! */
        if (CC.ActiveCount > 0)
        {
            SelectFeasible(BB, CC, RAISE, FREESET, SUPER_CUBE, ref num_covered);
        }

        /* While there are still cubes covered by the overexpanded cube ... */
        while (CC.ActiveCount > 0)
        {
            bestindex = MostFrequent(CC, FREESET);
            SetInsert(RAISE, bestindex);
            SetRemove(FREESET, bestindex);
            EssenParts(BB, CC, RAISE, FREESET);
        }

        /* Finally, when all else fails, choose the largest possible prime */
        while (BB.ActiveCount > 0)
        {
            Mincov(BB, RAISE, FREESET);
        }

        /* Raise any remaining free coordinates */
        SetOr(RAISE, RAISE, FREESET);
    }

    // -----------------------------------------------------------------------
    // essen_parts — determine forced lowering parts
    // -----------------------------------------------------------------------

    private static void EssenParts(SetFamily BB, SetFamily CC, PSet RAISE, PSet FREESET)
    {
        PSet p, r = RAISE;
        PSet xlower = Temp![0];
        int dist;

        SetCopy(xlower, EmptySet);

        for (int i = 0; i < BB.Count; i++)
        {
            p = BB.GetSet(i);
            if (!TestP(p, Active)) continue;
            
            dist = Cdist01(p, r);
            if (dist > 1) continue;
            
            if (dist == 0)
            {
                throw new InvalidOperationException("ON-set and OFF-set are not orthogonal");
            }
            else
            {
                ForceLower(xlower, p, r);
                BB.ActiveCount--;
                ResetFlag(p, Active);
            }
        }

        if (!SetpEmpty(xlower))
        {
            SetDiff(FREESET, FREESET, xlower);
            ElimLowering(BB, CC, RAISE, FREESET);
        }

        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
            Console.WriteLine("ESSEN_PARTS:\tRAISE={0} FREESET={1}", PcCube1(RAISE), PcCube2(FREESET));
    }

    // -----------------------------------------------------------------------
    // essen_raising — determine essentially raiseable parts
    // -----------------------------------------------------------------------

    private static void EssenRaising(SetFamily BB, PSet RAISE, PSet FREESET)
    {
        PSet p, xraise = Temp![0];

        /* Form union of all cubes of BB, and then take complement wrt FREESET */
        SetCopy(xraise, EmptySet);
        for (int i = 0; i < BB.Count; i++)
        {
            p = BB.GetSet(i);
            if (TestP(p, Active))
                InlineOr(xraise, xraise, p);
        }
        SetDiff(xraise, FREESET, xraise);

        SetOr(RAISE, RAISE, xraise);         /* add to raising set */
        SetDiff(FREESET, FREESET, xraise);   /* remove from free set */

        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
            Console.WriteLine("ESSEN_RAISING:\tRAISE={0} FREESET={1}", PcCube1(RAISE), PcCube2(FREESET));
    }

    // -----------------------------------------------------------------------
    // elim_lowering — reduce BB and CC after removing parts from FREESET
    // -----------------------------------------------------------------------

    private static void ElimLowering(SetFamily BB, SetFamily CC, PSet RAISE, PSet FREESET)
    {
        PSet p, r = SetOps.SetOr(Temp![0], RAISE, FREESET);

        /* Remove sets of BB which are orthogonal to future expansions */
        for (int i = 0; i < BB.Count; i++)
        {
            p = BB.GetSet(i);
            if (!TestP(p, Active)) continue;
            
            if (!Cdist0(p, r))
            {
                BB.ActiveCount--;
                ResetFlag(p, Active);
            }
        }

        /* Remove sets of CC which cannot be covered by future expansions */
        if (CC != null)
        {
            for (int i = 0; i < CC.Count; i++)
            {
                p = CC.GetSet(i);
                if (!TestP(p, Active)) continue;
                
                if (!SetpImplies(p, r))
                {
                    CC.ActiveCount--;
                    ResetFlag(p, Active);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // most_frequent — select reasonable part to raise
    // -----------------------------------------------------------------------

    private static int MostFrequent(SetFamily CC, PSet FREESET)
    {
        int i, best_part, best_count;
        int[] count;
        PSet p;

        /* Count occurrences of each variable */
        count = new int[Size];
        if (CC != null)
            for (int j = 0; j < CC.Count; j++)
            {
                p = CC.GetSet(j);
                if (TestP(p, Active))
                    SetAdjcnt(p, count, 1);
            }

        /* Now find which free part occurs most often */
        best_count = best_part = -1;
        for (i = 0; i < Size; i++)
            if (IsInSet(FREESET, i) && count[i] > best_count)
            {
                best_part = i;
                best_count = count[i];
            }

        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
            Console.WriteLine("MOST_FREQUENT:\tbest={0} FREESET={1}", best_part, PcCube2(FREESET));
        
        return best_part;
    }

    // -----------------------------------------------------------------------
    // setup_BB_CC — set up blocking and covering set families
    // -----------------------------------------------------------------------

    private static void SetupBbCc(SetFamily BB, SetFamily CC)
    {
        PSet p;

        /* Create the block and cover set families */
        BB.ActiveCount = BB.Count;
        for (int i = 0; i < BB.Count; i++)
        {
            p = BB.GetSet(i);
            SetFlag(p, Active);
        }

        if (CC != null)
        {
            CC.ActiveCount = CC.Count;
            for (int i = 0; i < CC.Count; i++)
            {
                p = CC.GetSet(i);
                if (TestP(p, Covered) || TestP(p, Prime))
                {
                    CC.ActiveCount--;
                    ResetFlag(p, Active);
                }
                else
                    SetFlag(p, Active);
            }
        }
    }

    // -----------------------------------------------------------------------
    // select_feasible — determine feasibly covered cubes
    // -----------------------------------------------------------------------

    private static void SelectFeasible(SetFamily BB, SetFamily CC, PSet RAISE, PSet FREESET, 
                                       PSet SUPER_CUBE, ref int num_covered)
    {
        PSet p, bestfeas = PSet.Null;
        PSet[] feas;
        int i, j;
        PSet[] feas_new_lower;
        int bestcount, bestsize, count, size, numfeas, lastfeas;
        SetFamily new_lower;

        /* Start with all cubes covered by the over-expanded cube as feasible */
        feas = new PSet[CC.ActiveCount];
        numfeas = 0;
        for (int ii = 0; ii < CC.Count; ii++)
        {
            p = CC.GetSet(ii);
            if (TestP(p, Active))
                feas[numfeas++] = p;
        }

        /* Setup extra cubes to record parts forced low after covering */
        feas_new_lower = new PSet[CC.ActiveCount];
        new_lower = SfNew(numfeas, Size);
        for (i = 0; i < numfeas; i++)
            feas_new_lower[i] = new_lower.GetSet(i);

        Loop:
        /* Find essentially raised parts */
        EssenRaising(BB, RAISE, FREESET);

        /* Check all "possibly" feasibly covered cubes for feasibility */
        lastfeas = numfeas;
        numfeas = 0;
        for (i = 0; i < lastfeas; i++)
        {
            p = feas[i];

            /* Check active because essen_parts might have removed it */
            if (TestP(p, Active))
            {
                /* See if the cube is already covered by RAISE */
                if (SetpImplies(p, RAISE))
                {
                    num_covered += 1;
                    SetOr(SUPER_CUBE, SUPER_CUBE, p);
                    CC.ActiveCount--;
                    ResetFlag(p, Active);
                    SetFlag(p, Covered);
                }
                /* otherwise, test if it is feasibly covered */
                else if (FeasiblyCovered(BB, p, RAISE, feas_new_lower[numfeas]) != 0)
                {
                    feas[numfeas] = p;  /* save the feasible candidate */
                    numfeas++;
                }
            }
        }

        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
            Console.WriteLine("SELECT_FEASIBLE: started with {0} pfcc, ended with {1} fcc", lastfeas, numfeas);

        /* Exit if no feasibly covered cubes */
        if (numfeas == 0)
        {
            SfFree(new_lower);
            return;
        }

        /* Find which is the best feasibly covered cube */
        bestcount = 0;
        bestsize = 9999;
        for (i = 0; i < numfeas; i++)
        {
            size = SetDist(feas[i], FREESET);
            count = 0;

            for (j = 0; j < numfeas; j++)
                if (SetpDisjoint(feas_new_lower[i], feas[j]))
                    count++;

            if (count > bestcount)
            {
                bestcount = count;
                bestfeas = feas[i];
                bestsize = size;
            }
            else if (count == bestcount && size < bestsize)
            {
                bestfeas = feas[i];
                bestsize = size;
            }
        }

        /* Add the necessary parts to the raising set */
        SetOr(RAISE, RAISE, bestfeas);
        SetDiff(FREESET, FREESET, RAISE);
        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
            Console.WriteLine("FEASIBLE:\tRAISE={0} FREESET={1}", PcCube1(RAISE), PcCube2(FREESET));
        EssenParts(BB, CC, RAISE, FREESET);
        goto Loop;
    }

    // -----------------------------------------------------------------------
    // feasibly_covered — check if a cube is feasibly covered
    // -----------------------------------------------------------------------

    private static int FeasiblyCovered(SetFamily BB, PSet c, PSet RAISE, PSet new_lower)
    {
        PSet p, r = SetOps.SetOr(Temp![0], RAISE, c);
        int dist;

        SetCopy(new_lower, EmptySet);
        for (int i = 0; i < BB.Count; i++)
        {
            p = BB.GetSet(i);
            if (!TestP(p, Active)) continue;
            
            dist = Cdist01(p, r);
            if (dist > 1) continue;
            
            if (dist == 0)
                return 0;  // FALSE
            else
                ForceLower(new_lower, p, r);
        }
        return 1;  // TRUE
    }

    // -----------------------------------------------------------------------
    // mincov — minimum covering when expansion fails
    // -----------------------------------------------------------------------

    private static void Mincov(SetFamily BB, PSet RAISE, PSet FREESET)
    {
        int expansion, nset, var, dist;
        SetFamily B;
        PSet xraise = Temp![0], xlower, p, plower;

        /* Create B which are those cubes which we must avoid intersecting */
        B = SfNew(BB.ActiveCount, Size);
        for (int i = 0; i < BB.Count; i++)
        {
            p = BB.GetSet(i);
            if (TestP(p, Active))
            {
                plower = SetCopy(B.GetSet(B.Count++), EmptySet);
                ForceLower(plower, p, RAISE);
            }
        }

        /* Determine how many sets it will blow up into after the unravel */
        nset = 0;
        for (int i = 0; i < B.Count; i++)
        {
            p = B.GetSet(i);
            expansion = 1;
            for (var = NumBinaryVars; var < NumVars; var++)
            {
                if ((dist = SetDist(p, VarMask![var])) > 1)
                {
                    expansion *= dist;
                    if (expansion > 500) goto heuristic_mincov;
                }
            }
            nset += expansion;
            if (nset > 500) goto heuristic_mincov;
        }

        B = Unravel(B, NumBinaryVars);
        xlower = DoSmMinimumCover(B);

        /* Add any remaining free parts to the raising set */
        SetOr(RAISE, RAISE, SetDiff(xraise, FREESET, xlower));
        SetCopy(FREESET, EmptySet);  /* free set is empty */
        BB.ActiveCount = 0;           /* BB satisfied */
        if ((Globals.Debug & EspressoConstants.Expand1) != 0)
            Console.WriteLine("MINCOV:\tRAISE={0} FREESET={1}", PcCube1(RAISE), PcCube2(FREESET));
        SfFree(B);
        SetFree(xlower);
        return;

        heuristic_mincov:
        SfFree(B);
        /* most_frequent will pick first free part */
        SetInsert(RAISE, MostFrequent(null, FREESET));
        SetDiff(FREESET, FREESET, RAISE);
        EssenParts(BB, null, RAISE, FREESET);
    }

    // -----------------------------------------------------------------------
    // Helper: ForceLower — mark parts that must be lowered
    // -----------------------------------------------------------------------

    private static void ForceLower(PSet xlower, PSet p, PSet r) =>
        SetC.ForceLower(xlower, p, r);

    private static SetFamily RandomOrder(SetFamily F) => CvrM.RandomOrder(F);

    private static SetFamily MiniSort(SetFamily F, Comparison<PSet> cmp) => CvrM.MiniSort(F, cmp);

    private static SetFamily Unravel(SetFamily B, int nvars) => CvrM.Unravel(B, nvars);

    private static PSet DoSmMinimumCover(SetFamily B) => SmInterf.DoSmMinimumCover(B);

    // -----------------------------------------------------------------------
    // Iterator helpers
    // -----------------------------------------------------------------------

    private static SetFamily SfInactive(SetFamily F) => SetFamily.SfInactive(F);

    private static string PcCube1(PSet p) => $"[cube at {SetOrd(p)} bits]";
    private static string PcCube2(PSet p) => $"[cube at {SetOrd(p)} bits]";
}
