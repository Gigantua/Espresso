namespace EspressoCS;

// Translates mincov.c, gimpel.c, and indep.c — all part of the mincov subsystem
// (shared via mincov_int.h).

public static class MinCov
{
    // -----------------------------------------------------------------------
    // mincov.c — public entry point
    // -----------------------------------------------------------------------

    public static SmRow SmMinimumCover(SmMatrix A, int[]? weight, int heuristic, int debugLevel)
    {
        if (A.NRows <= 0)
            return SparseMatrix.SmRowAlloc();   // easy to cover

        var stats = new StatsT
        {
            StartTime     = UtilCpuTime(),
            Debug         = debugLevel > 0,
            MaxPrintDepth = debugLevel,
            MaxDepth      = -1,
            NoBranching   = heuristic != 0,
            LowerBound    = -1,
        };

        int nelem = 0;
        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
            nelem += prow.Length;
        double sparsity = (double)nelem / (double)(A.NRows * A.NCols);

        int bound = 1;
        for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
            bound += Solution.Weight(weight, pcol.ColNum);

        var select = Solution.SolutionAlloc();
        var dupA   = SparseMatrix.SmDup(A);
        var best   = SmMincov(dupA, select, weight, 0, bound, 0, stats);
        SparseMatrix.SmFree(dupA);
        Solution.SolutionFree(select);

        if (stats.Debug)
        {
            if (stats.NoBranching)
            {
                Console.WriteLine("**** heuristic covering ...");
                Console.WriteLine($"lower bound = {stats.LowerBound}");
            }
            Console.WriteLine($"matrix     = {A.NRows} by {A.NCols} with {nelem} elements ({sparsity * 100.0:F3}%)");
            Console.WriteLine($"cover size = {best!.Row.Length} elements");
            Console.WriteLine($"cover cost = {best.Cost}");
            Console.WriteLine($"time       = {UtilPrintTime(UtilCpuTime() - stats.StartTime)}");
            Console.WriteLine($"components = {stats.CompCount}");
            Console.WriteLine($"gimpel     = {stats.GimpelCount}");
            Console.WriteLine($"nodes      = {stats.Nodes}");
            Console.WriteLine($"max_depth  = {stats.MaxDepth}");
        }

        var sol = SparseMatrix.SmRowDup(best!.Row);
        if (!VerifyCover(A, sol))
            throw new InvalidOperationException("mincov: internal error -- cover verification failed\n");
        Solution.SolutionFree(best);
        return sol;
    }

    // -----------------------------------------------------------------------
    // sm_mincov — recursive core
    // -----------------------------------------------------------------------

    public static SolutionT? SmMincov(
        SmMatrix A, SolutionT select, int[]? weight,
        int lb, int bound, int depth, StatsT stats)
    {
        SolutionT? best;

        stats.Nodes++;
        if (depth > stats.MaxDepth) stats.MaxDepth = depth;
        bool debug = stats.Debug && (depth <= stats.MaxPrintDepth);

        SelectEssential(A, select, weight, bound);
        if (select.Cost >= bound)
            return null;

        // Gimpel reduction (only when weight is null, matching C #ifdef USE_GIMPEL + hack)
        if (weight is null)
        {
            if (GimpelReduce(A, select, weight, lb, bound, depth, stats, out var gBest))
                return gBest;
        }

        // Independent-set lower bound (#ifdef USE_INDEP_SET)
        var indep  = SmMaximalIndependentSet(A, weight);
        int lbNew  = Math.Max(select.Cost + indep.Cost, lb);
        int pick   = SelectColumn(A, weight, indep);
        Solution.SolutionFree(indep);

        if (depth == 0)
            stats.LowerBound = lbNew + stats.Gimpel;

        if (debug)
        {
            Console.Write($"ABSMIN[{depth,2}]{(stats.Component != 0 ? "*" : " ")}");
            Console.Write($" {A.NRows,3}x{A.NCols,3}" +
                          $" sel={select.Cost + stats.Gimpel,3}" +
                          $" bnd={bound + stats.Gimpel,3}" +
                          $" lb={lbNew + stats.Gimpel,3}" +
                          $" {UtilPrintTime(UtilCpuTime() - stats.StartTime),12} ");
        }

        if (lbNew >= bound)
        {
            if (debug) Console.WriteLine("bounded");
            best = null;
        }
        else if (A.NRows == 0)
        {
            best = Solution.SolutionDup(select);
            if (debug) Console.WriteLine("BEST");
            if (stats.Debug && stats.Component == 0)
                Console.WriteLine(
                    $"new 'best' solution {best.Cost + stats.Gimpel} at level {depth}" +
                    $" (time is {UtilPrintTime(UtilCpuTime() - stats.StartTime)})");
        }
        else if (Dominate.SmBlockPartition(A, out var L, out var R) != 0)
        {
            // Make L the smaller problem.
            if (L.NCols > R.NCols) { var t = L; L = R; R = t; }
            if (debug) Console.WriteLine($"comp {L.NRows} {R.NRows}");
            stats.CompCount++;

            var select1 = Solution.SolutionAlloc();
            stats.Component++;
            var best1 = SmMincov(L, select1, weight, 0, bound - select.Cost, depth + 1, stats);
            stats.Component--;
            Solution.SolutionFree(select1);
            SparseMatrix.SmFree(L);

            if (best1 is null)
            {
                best = null;
            }
            else
            {
                for (var p = best1.Row.FirstCol; p != null; p = p.NextCol)
                    Solution.SolutionAdd(select, weight, p.ColNum);
                Solution.SolutionFree(best1);
                best = SmMincov(R, select, weight, lbNew, bound, depth + 1, stats);
            }
            SparseMatrix.SmFree(R);
        }
        else
        {
            if (debug) Console.WriteLine($"pick={pick}");

            // Branch 1: accept the chosen column.
            var A1      = SparseMatrix.SmDup(A);
            var select1 = Solution.SolutionDup(select);
            Solution.SolutionAccept(select1, A1, weight, pick);
            var best1   = SmMincov(A1, select1, weight, lbNew, bound, depth + 1, stats);
            Solution.SolutionFree(select1);
            SparseMatrix.SmFree(A1);

            if (best1 != null && bound > best1.Cost)
                bound = best1.Cost;

            if (stats.NoBranching)
                return best1;

            if (best1 != null && best1.Cost == lbNew)
                return best1;

            // Branch 2: reject the chosen column.
            var A2      = SparseMatrix.SmDup(A);
            var select2 = Solution.SolutionDup(select);
            Solution.SolutionReject(select2, A2, weight, pick);
            var best2   = SmMincov(A2, select2, weight, lbNew, bound, depth + 1, stats);
            Solution.SolutionFree(select2);
            SparseMatrix.SmFree(A2);

            best = Solution.SolutionChooseBest(best1, best2);
        }

        return best;
    }

    // -----------------------------------------------------------------------
    // select_column — pick the best column to branch on
    // -----------------------------------------------------------------------

    private static int SelectColumn(SmMatrix A, int[]? weight, SolutionT? indep)
    {
        var indepCols = SparseMatrix.SmRowAlloc();

        if (indep != null)
        {
            for (var p = indep.Row.FirstCol; p != null; p = p.NextCol)
            {
                var prow = SparseMatrix.SmGetRow(A, p.ColNum);
                if (prow != null)
                    for (var p1 = prow.FirstCol; p1 != null; p1 = p1.NextCol)
                        SparseMatrix.SmRowInsert(indepCols, p1.ColNum);
            }
        }
        else
        {
            for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
                SparseMatrix.SmRowInsert(indepCols, pcol.ColNum);
        }

        int    bestCol = -1;
        double best    = -1.0;

        for (var p1 = indepCols.FirstCol; p1 != null; p1 = p1.NextCol)
        {
            var pcol = SparseMatrix.SmGetCol(A, p1.ColNum);
            if (pcol is null) continue;

            double w = 0.0;
            for (var p = pcol.FirstRow; p != null; p = p.NextRow)
            {
                var prow = SparseMatrix.SmGetRow(A, p.RowNum);
                if (prow != null)
                    w += 1.0 / ((double)prow.Length - 1.0);
            }
            w /= (double)Solution.Weight(weight, pcol.ColNum);

            if (w > best)
            {
                bestCol = pcol.ColNum;
                best    = w;
            }
        }

        SparseMatrix.SmRowFree(indepCols);
        return bestCol;
    }

    // -----------------------------------------------------------------------
    // select_essential — row/col dominance + essential column selection
    // -----------------------------------------------------------------------

    private static void SelectEssential(SmMatrix A, SolutionT select, int[]? weight, int bound)
    {
        int delcols, delrows, essenCount;

        do
        {
            delcols = Dominate.SmColDominance(A, weight);

            var essen = SparseMatrix.SmRowAlloc();
            for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
                if (prow.Length == 1)
                    SparseMatrix.SmRowInsert(essen, prow.FirstCol!.ColNum);

            // Collect into list first to avoid iterator invalidation.
            SmElement? pnext;
            for (var p = essen.FirstCol; p != null; p = pnext)
            {
                pnext = p.NextCol;
                Solution.SolutionAccept(select, A, weight, p.ColNum);
                if (select.Cost >= bound)
                {
                    SparseMatrix.SmRowFree(essen);
                    return;
                }
            }
            essenCount = essen.Length;
            SparseMatrix.SmRowFree(essen);

            delrows = Dominate.SmRowDominance(A);

        } while (delcols > 0 || delrows > 0 || essenCount > 0);
    }

    // -----------------------------------------------------------------------
    // verify_cover
    // -----------------------------------------------------------------------

    private static bool VerifyCover(SmMatrix A, SmRow cover)
    {
        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
            if (!SparseMatrix.SmRowIntersects(prow, cover))
                return false;
        return true;
    }

    // -----------------------------------------------------------------------
    // gimpel.c — Gimpel reduction
    // -----------------------------------------------------------------------

    public static bool GimpelReduce(
        SmMatrix A, SolutionT select, int[]? weight,
        int lb, int bound, int depth, StatsT stats,
        out SolutionT? best)
    {
        SmCol? c1 = null, c2 = null;
        int c1ColNum = 0, c2ColNum = 0, primaryRowNum = 0, secondaryRowNum = 0;
        bool reduceIt = false;

        for (var prow = A.FirstRow; prow != null && !reduceIt; prow = prow.NextRow)
        {
            if (prow.Length == 2)
            {
                c1 = SparseMatrix.SmGetCol(A, prow.FirstCol!.ColNum);
                c2 = SparseMatrix.SmGetCol(A, prow.LastCol!.ColNum);
                if (c1!.Length == 2)
                {
                    reduceIt = true;
                }
                else if (c2!.Length == 2)
                {
                    c1 = SparseMatrix.SmGetCol(A, prow.LastCol!.ColNum);
                    c2 = SparseMatrix.SmGetCol(A, prow.FirstCol!.ColNum);
                    reduceIt = true;
                }
                if (reduceIt)
                {
                    primaryRowNum   = prow.RowNum;
                    secondaryRowNum = c1!.FirstRow!.RowNum;
                    if (secondaryRowNum == primaryRowNum)
                        secondaryRowNum = c1.LastRow!.RowNum;
                }
            }
        }

        if (!reduceIt)
        {
            best = null;
            return false;
        }

        c1ColNum = c1!.ColNum;
        c2ColNum = c2!.ColNum;
        var saveSec = SparseMatrix.SmRowDup(SparseMatrix.SmGetRow(A, secondaryRowNum)!);
        SparseMatrix.SmRowRemove(saveSec, c1ColNum);

        for (var p = c2.FirstRow; p != null; p = p.NextRow)
        {
            if (p.RowNum != primaryRowNum)
                for (var p1 = saveSec.FirstCol; p1 != null; p1 = p1.NextCol)
                    SparseMatrix.SmInsert(A, p.RowNum, p1.ColNum);
        }

        SparseMatrix.SmDelCol(A, c1ColNum);
        SparseMatrix.SmDelCol(A, c2ColNum);
        SparseMatrix.SmDelRow(A, primaryRowNum);
        SparseMatrix.SmDelRow(A, secondaryRowNum);

        stats.GimpelCount++;
        stats.Gimpel++;
        best = SmMincov(A, select, weight, lb - 1, bound - 1, depth, stats);
        stats.Gimpel--;

        if (best != null)
        {
            if (SparseMatrix.SmRowIntersects(saveSec, best.Row))
                Solution.SolutionAdd(best, weight, c2ColNum);
            else
                Solution.SolutionAdd(best, weight, c1ColNum);
        }

        SparseMatrix.SmRowFree(saveSec);
        return true;
    }

    // -----------------------------------------------------------------------
    // indep.c — maximal independent set
    // -----------------------------------------------------------------------

    public static SolutionT SmMaximalIndependentSet(SmMatrix A, int[]? weight)
    {
        var indep = Solution.SolutionAlloc();
        var B     = BuildIntersectionMatrix(A);

        while (B.NRows > 0)
        {
            // Find the row in B with fewest neighbours (smallest length).
            var bestRow = B.FirstRow!;
            for (var prow = B.FirstRow!.NextRow; prow != null; prow = prow.NextRow)
                if (prow.Length < bestRow.Length)
                    bestRow = prow;

            // Find the minimum weight element in the original row.
            int leastWeight;
            if (weight is null)
            {
                leastWeight = 1;
            }
            else
            {
                var origRow = SparseMatrix.SmGetRow(A, bestRow.RowNum)!;
                leastWeight = weight[origRow.FirstCol!.ColNum];
                for (var p = origRow.FirstCol!.NextCol; p != null; p = p.NextCol)
                    if (weight[p.ColNum] < leastWeight)
                        leastWeight = weight[p.ColNum];
            }
            indep.Cost += leastWeight;
            SparseMatrix.SmRowInsert(indep.Row, bestRow.RowNum);

            // Discard rows that intersect this one.
            var save = SparseMatrix.SmRowDup(bestRow);
            for (var p = save.FirstCol; p != null; p = p.NextCol)
            {
                SparseMatrix.SmDelRow(B, p.ColNum);
                SparseMatrix.SmDelCol(B, p.ColNum);
            }
            SparseMatrix.SmRowFree(save);
        }

        SparseMatrix.SmFree(B);
        return indep;
    }

    private static SmMatrix BuildIntersectionMatrix(SmMatrix A)
    {
        var B = SparseMatrix.SmAlloc();

        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
        {
            // Clear flags on all rows reachable from prow.
            for (var p = prow.FirstCol; p != null; p = p.NextCol)
            {
                var pcol = SparseMatrix.SmGetCol(A, p.ColNum);
                if (pcol is null) continue;
                for (var p1 = pcol.FirstRow; p1 != null; p1 = p1.NextRow)
                {
                    var prow1 = SparseMatrix.SmGetRow(A, p1.RowNum);
                    if (prow1 != null) prow1.Flag = 0;
                }
            }

            // Record which rows are reachable.
            for (var p = prow.FirstCol; p != null; p = p.NextCol)
            {
                var pcol = SparseMatrix.SmGetCol(A, p.ColNum);
                if (pcol is null) continue;
                for (var p1 = pcol.FirstRow; p1 != null; p1 = p1.NextRow)
                {
                    var prow1 = SparseMatrix.SmGetRow(A, p1.RowNum);
                    if (prow1 != null && prow1.Flag == 0)
                    {
                        prow1.Flag = 1;
                        SparseMatrix.SmInsert(B, prow.RowNum, prow1.RowNum);
                    }
                }
            }
        }

        return B;
    }

    // -----------------------------------------------------------------------
    // Timing helpers (utility.h replacements)
    // -----------------------------------------------------------------------

    private static long UtilCpuTime() => Environment.TickCount64;
    private static string UtilPrintTime(long t) => $"{t}ms";
}
