namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;

/// <summary>
/// Sparse — make_sparse and mv_reduce.
/// Translated 1:1 from sparse.c.
/// </summary>
public static class Sparse
{
    /// <summary>
    /// make_sparse — last-step cleanup to reduce the total number of literals in the cover.
    /// Performs mv_reduce (reduce sparse variables) followed by expand (raise dense variables).
    /// </summary>
    public static SetFamily MakeSparse(SetFamily F, SetFamily D, SetFamily R)
    {
        var cost = new Cost();
        var bestCost = new Cost();

        CvrMisc.CoverCost(F, bestCost);

        do
        {
            long t = Stubs.PTime();
            F = MvReduce(F, D);
            CvrMisc.Totals(t, Globals.MvReduceTime, F, cost);
            if (cost.Total == bestCost.Total)
                break;
            CvrMisc.CopyCost(cost, bestCost);

            t = Stubs.PTime();
            F = Expand.ExpandCover(F, R, 1 /* TRUE */);
            CvrMisc.Totals(t, Globals.RaiseInTime, F, cost);
            if (cost.Total == bestCost.Total)
                break;
            CvrMisc.CopyCost(cost, bestCost);

        } while (Globals.ForceIrredundant);

        return F;
    }

    /// <summary>
    /// mv_reduce — perform an "optimal" reduction of the variables which we desire to be sparse.
    /// Uses IRRED to find which cubes of an output are redundant.
    /// </summary>
    public static SetFamily MvReduce(SetFamily F, SetFamily D)
    {
        for (int var = 0; var < NumVars; var++)
        {
            if (SparseVar![var] != 0)
            {
                for (int i = FirstPart![var]; i <= LastPart![var]; i++)
                {
                    var fCubeTable = new PSet[F.Count];

                    SetFamily F1 = SfNew(F.Count, Size);
                    for (int fi = 0; fi < F.Count; fi++)
                    {
                        PSet p = F.GetSet(fi);
                        if (IsInSet(p, i))
                        {
                            fCubeTable[F1.Count] = p;
                            PSet p1 = F1.GetSet(F1.Count++);
                            SetDiff(p1, p, VarMask![var]);
                            SetInsert(p1, i);
                        }
                    }

                    SetFamily D1 = SfNew(D.Count, Size);
                    for (int di = 0; di < D.Count; di++)
                    {
                        PSet p = D.GetSet(di);
                        if (IsInSet(p, i))
                        {
                            PSet p1 = D1.GetSet(D1.Count++);
                            SetDiff(p1, p, VarMask![var]);
                            SetInsert(p1, i);
                        }
                    }

                    Irred.MarkIrredundant(F1, D1);

                    int index = 0;
                    for (int fi = 0; fi < F1.Count; fi++)
                    {
                        PSet p1 = F1.GetSet(fi);
                        if (!TestP(p1, Active))
                        {
                            PSet p = fCubeTable[index];
                            if (var == NumVars - 1 || !SetpImplies(VarMask![var], p))
                            {
                                SetRemove(p, i);
                            }
                            ResetFlag(p, Prime);
                        }
                        index++;
                    }
                }
            }
        }

        SfActive(F);
        for (int var = 0; var < NumVars; var++)
        {
            if (SparseVar![var] != 0)
            {
                for (int fi = 0; fi < F.Count; fi++)
                {
                    PSet p = F.GetSet(fi);
                    if (TestP(p, Active) && SetpDisjoint(p, VarMask![var]))
                    {
                        ResetFlag(p, Active);
                        F.ActiveCount--;
                    }
                }
            }
        }

        if (F.Count != F.ActiveCount)
        {
            F = SfInactive(F);
        }

        return F;
    }
}
