namespace EspressoCS;

// Defines solution_t and stats_t (from mincov_int.h), then translates solution.c.

/// <summary>Mirrors solution_struct / solution_t from mincov_int.h.</summary>
public class SolutionT
{
    public SmRow Row = SparseMatrix.SmRowAlloc();
    public int Cost;
}

/// <summary>Mirrors stats_struct / stats_t from mincov_int.h.</summary>
public class StatsT
{
    public bool Debug;
    public int  MaxPrintDepth;
    public int  MaxDepth;
    public int  Nodes;
    public int  Component;
    public int  CompCount;
    public int  GimpelCount;
    public int  Gimpel;
    public long StartTime;
    public bool NoBranching;
    public int  LowerBound;
}

public static class Solution
{
    // WEIGHT macro: weight==null means 1; otherwise weight[col].
    internal static int Weight(int[]? weight, int col) => weight is null ? 1 : weight[col];

    public static SolutionT SolutionAlloc()
    {
        var sol = new SolutionT();
        sol.Cost = 0;
        sol.Row  = SparseMatrix.SmRowAlloc();
        return sol;
    }

    public static void SolutionFree(SolutionT sol)
    {
        SparseMatrix.SmRowFree(sol.Row);
        // GC reclaims the object
    }

    public static SolutionT SolutionDup(SolutionT sol)
    {
        var newSol = new SolutionT();
        newSol.Cost = sol.Cost;
        newSol.Row  = SparseMatrix.SmRowDup(sol.Row);
        return newSol;
    }

    public static void SolutionAdd(SolutionT sol, int[]? weight, int col)
    {
        SparseMatrix.SmRowInsert(sol.Row, col);
        sol.Cost += Weight(weight, col);
    }

    public static void SolutionAccept(SolutionT sol, SmMatrix A, int[]? weight, int col)
    {
        SolutionAdd(sol, weight, col);

        // Delete rows covered by this column.
        var pcol = SparseMatrix.SmGetCol(A, col);
        if (pcol != null)
        {
            SmElement? pnext;
            for (var p = pcol.FirstRow; p != null; p = pnext)
            {
                pnext = p.NextRow;   // grab before it disappears
                SparseMatrix.SmDelRow(A, p.RowNum);
            }
        }
    }

    // ARGSUSED: sol and weight are intentionally unused.
    public static void SolutionReject(SolutionT sol, SmMatrix A, int[]? weight, int col)
    {
        SparseMatrix.SmDelCol(A, col);
    }

    public static SolutionT? SolutionChooseBest(SolutionT? best1, SolutionT? best2)
    {
        if (best1 != null)
        {
            if (best2 != null)
            {
                if (best1.Cost <= best2.Cost)
                {
                    SolutionFree(best2);
                    return best1;
                }
                else
                {
                    SolutionFree(best1);
                    return best2;
                }
            }
            else
            {
                return best1;
            }
        }
        else
        {
            return best2; // null if best2 is also null
        }
    }
}
