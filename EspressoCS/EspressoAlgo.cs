namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;
using static Contain;

/// <summary>
/// EspressoAlgo — main Espresso minimization algorithm.
/// Translated 1:1 from espresso.c.
/// Implements the core Espresso two-level logic minimization algorithm.
/// </summary>
public static class EspressoAlgo
{
    // -----------------------------------------------------------------------
    // espresso — main entry point for Espresso minimization
    // -----------------------------------------------------------------------

    /// <summary>
    /// Espresso — returns a minimized version of the ON-set of a function.
    /// F — ON-set (minterms/cubes where output is 1)
    /// D1 — don't-care set
    /// R — OFF-set (where output must be 0)
    /// 
    /// The following global variables affect the operation:
    /// - Trace: print trace information as minimization progresses
    /// - RemoveEssential: remove essential primes
    /// - SingleExpand: stop after first expand/irredundant
    /// - UseSuperGasp: use super_gasp strategy rather than last_gasp
    /// - RecomputeOnset: recompute onset using complement before starting
    /// - UnwrapOnset: unwrap function output part before first expand
    /// - ForceIrredundant: iterates make_sparse to force minimal solution
    /// - SkipMakeSparse: skip the make_sparse step
    /// </summary>
    public static SetFamily Espresso(SetFamily F, SetFamily D1, SetFamily R)
    {
        SetFamily E, D, Fsave;
        Cost cost, best_cost;

    begin:
        /* Save original function and make scratch copy of D */
        Fsave = SfSave(F);
        D = SfSave(D1);
        E = SfNew(0, Size);  /* Initialize E in case we don't set it */

        /* Setup: recompute onset if requested */
        if (Globals.RecomputeOnset)
        {
            E = Stubs.Simplify(Stubs.Cube1List(F));
            /* F is replaced with simplified version */
            F = E;
        }

        CoverCost(F, out cost);

        /* Unwrap output part if requested and conditions met */
        if (Globals.UnwrapOnset && (PartSize![NumVars - 1] > 1)
            && (cost.Out != cost.Cubes * PartSize![NumVars - 1])
            && (cost.Out < 5000))
        {
            F = SfContain(Stubs.Unravel(F, NumVars - 1));
            if (Globals.Trace)
                SizeStamp(F, "SETUP      ");
        }

        /* Initial expand and irredundant */
        for (int i = 0; i < F.Count; i++)
        {
            var p = F.GetSet(i);
            ResetFlag(p, Prime);
        }

        TimeExec(ref F, Globals.ExpandTime, "EXPAND     ", 
            () => Expand.ExpandCover(F, R, 0));

        TimeExec(ref F, Globals.IrredTime, "IRRED      ", 
            () => Irred.Irredundant(F, D));

        if (!Globals.SingleExpand)
        {
            if (Globals.RemoveEssential)
            {
                TimeExec(ref E, Globals.EssenTime, "ESSEN      ",
                    () => Essen.Essentials(ref F, ref D));
            }
            else
            {
                E = SfNew(0, Size);
            }

            CoverCost(F, out cost);

            /* Main iteration loop */
            do
            {
                /* Inner loop: reduce-expand-irred until stable */
                do
                {
                    best_cost = new EspressoCS.Cost
                    {
                        Cubes = cost.Cubes,
                        Total = cost.Total,
                        In = cost.In,
                        Out = cost.Out,
                        Mv = cost.Mv,
                        Primes = cost.Primes
                    };

                    TimeExec(ref F, Globals.ReduceTime, "REDUCE     ",
                        () => Reduce.ReduceCover(F, D));
                    
                    TimeExec(ref F, Globals.ExpandTime, "EXPAND     ",
                        () => Expand.ExpandCover(F, R, 0));
                    
                    TimeExec(ref F, Globals.IrredTime, "IRRED      ",
                        () => Irred.Irredundant(F, D));

                    CoverCost(F, out cost);
                } while (cost.Cubes < best_cost.Cubes);

                /* Perturb to see if we can continue iterating */
                best_cost = new EspressoCS.Cost
                {
                    Cubes = cost.Cubes,
                    Total = cost.Total,
                    In = cost.In,
                    Out = cost.Out,
                    Mv = cost.Mv,
                    Primes = cost.Primes
                };

                if (Globals.UseSuperGasp)
                {
                    F = Stubs.SuperGasp(F, D, R, ref cost);
                    if (cost.Cubes >= best_cost.Cubes)
                        break;
                }
                else
                {
                    F = Stubs.LastGasp(F, D, R, ref cost);
                }

            } while (cost.Cubes < best_cost.Cubes ||
                     (cost.Cubes == best_cost.Cubes && cost.Total < best_cost.Total));

            /* Append essential cubes to F */
            F = SfAppend(F, E);  /* E is disposed by sf_append */
            if (Globals.Trace)
                SizeStamp(F, "ADJUST     ");
        }

        /* Free the working copy of D */
        /* (implicitly freed by GC) */

        /* Attempt to make PLA matrix sparse */
        if (!Globals.SkipMakeSparse)
        {
            F = Stubs.MakeSparse(F, D1, R);
        }

        /* Sanity check: ensure result is actually smaller */
        if (Fsave.Count < F.Count)
        {
            /* Initial unravel failed; retry without it */
            F = Fsave;
            Globals.UnwrapOnset = false;
            goto begin;
        }
        else
        {
            /* Keep new result; discard original */
        }

        return F;
    }

    // -----------------------------------------------------------------------
    // Helper: time-tracked execution wrapper
    // -----------------------------------------------------------------------

    /// <summary>
    /// TimeExec — execute an operation with timing and optional trace output.
    /// </summary>
    private static void TimeExec(ref SetFamily F, int timeIndex, string label, 
                                  Func<SetFamily> operation)
    {
        long start = Stubs.PTime();
        F = operation();
        long elapsed = Stubs.PTime() - start;
        Globals.TotalTime[timeIndex] += elapsed;
        Globals.TotalCalls[timeIndex]++;
        
        if (Globals.Trace)
        {
            SizeStamp(F, label);
        }
    }

    /// <summary>
    /// TimeExec variant for operations that also compute cost.
    /// </summary>
    private static void TimeExec(ref SetFamily F, int timeIndex, string label,
                                  Func<SetFamily> operation, out Cost cost)
    {
        TimeExec(ref F, timeIndex, label, operation);
        CoverCost(F, out cost);
    }


    // -----------------------------------------------------------------------
    // Cover cost computation
    // -----------------------------------------------------------------------

    /// <summary>
    /// CoverCost — compute cost metrics for a cover (literal count, cube count, output count).
    /// </summary>
    public static void CoverCost(SetFamily F, out EspressoCS.Cost cost)
    {
        cost = new EspressoCS.Cost();
        CvrMisc.CoverCost(F, cost);
    }

    /// <summary>
    /// SizeStamp — print cover size information with a label.
    /// </summary>
    private static void SizeStamp(SetFamily F, string label)
    {
        CoverCost(F, out var cost);
        Console.WriteLine("{0} {1,4} cubes {2,6} literals",
                         label, F.Count, cost.Total);
    }
}
