namespace EspressoCS;

// Translates dominate.c (sm_row_dominance, sm_col_dominance) and
// part.c (sm_block_partition).
public static class Dominate
{
    public static int SmRowDominance(SmMatrix A)
    {
        int rowcnt = A.NRows;

        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
        {
            if (prow.FirstCol == null) continue;

            // Among all columns with a 1 in this row, choose the shortest.
            var leastCol = SparseMatrix.SmGetCol(A, prow.FirstCol.ColNum)!;
            for (var p = prow.FirstCol.NextCol; p != null; p = p.NextCol)
            {
                var pcol = SparseMatrix.SmGetCol(A, p.ColNum)!;
                if (pcol.Length < leastCol.Length)
                    leastCol = pcol;
            }

            // Check rows in that column for containment.
            SmElement? pnext;
            for (var p = leastCol.FirstRow; p != null; p = pnext)
            {
                pnext = p.NextRow;
                var prow1 = SparseMatrix.SmGetRow(A, p.RowNum)!;
                if (prow1.Length > prow.Length ||
                    (prow1.Length == prow.Length && prow1.RowNum > prow.RowNum))
                {
                    if (SparseMatrix.SmRowContains(prow, prow1))
                        SparseMatrix.SmDelRow(A, prow1.RowNum);
                }
            }
        }

        return rowcnt - A.NRows;
    }

    public static int SmColDominance(SmMatrix A, int[]? weight)
    {
        int colcnt = A.NCols;

        SmCol? nextCol;
        for (var pcol = A.FirstCol; pcol != null; pcol = nextCol)
        {
            nextCol = pcol.NextCol;
            if (pcol.FirstRow == null) continue;

            // Among all rows with a 1 in this column, choose the shortest.
            var leastRow = SparseMatrix.SmGetRow(A, pcol.FirstRow.RowNum)!;
            for (var p = pcol.FirstRow.NextRow; p != null; p = p.NextRow)
            {
                var prow = SparseMatrix.SmGetRow(A, p.RowNum)!;
                if (prow.Length < leastRow.Length)
                    leastRow = prow;
            }

            // Check columns in that row for containment.
            for (var p = leastRow.FirstCol; p != null; p = p.NextCol)
            {
                var pcol1 = SparseMatrix.SmGetCol(A, p.ColNum)!;
                if (weight != null && weight[pcol1.ColNum] > weight[pcol.ColNum])
                    continue;
                if (pcol1.Length > pcol.Length ||
                    (pcol1.Length == pcol.Length && pcol1.ColNum > pcol.ColNum))
                {
                    if (SparseMatrix.SmColContains(pcol, pcol1))
                    {
                        SparseMatrix.SmDelCol(A, pcol.ColNum);
                        break;
                    }
                }
            }
        }

        return colcnt - A.NCols;
    }

    public static int SmBlockPartition(SmMatrix A, out SmMatrix L, out SmMatrix R)
    {
        L = SparseMatrix.SmAlloc();
        R = SparseMatrix.SmAlloc();

        if (A.NRows == 0)
            return 0;

        // Reset visited flags on all rows and columns.
        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow) prow.Flag = 0;
        for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol) pcol.Flag = 0;

        int rowsVisited = 0, colsVisited = 0;
        if (VisitRow(A, A.FirstRow!, ref rowsVisited, ref colsVisited))
        {
            // All rows reachable from the first row — no partition exists.
            return 0;
        }

        // Partition: flagged rows go to L, unflagged to R.
        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
        {
            if (prow.Flag != 0) CopyRow(L, prow);
            else CopyRow(R, prow);
        }
        return 1;
    }

    private static bool VisitRow(SmMatrix A, SmRow prow, ref int rowsVisited, ref int colsVisited)
    {
        if (prow.Flag != 0) return false;
        prow.Flag = 1;
        if (++rowsVisited == A.NRows) return true;
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
        {
            var pcol = SparseMatrix.SmGetCol(A, p.ColNum)!;
            if (pcol.Flag == 0 && VisitCol(A, pcol, ref rowsVisited, ref colsVisited))
                return true;
        }
        return false;
    }

    private static bool VisitCol(SmMatrix A, SmCol pcol, ref int rowsVisited, ref int colsVisited)
    {
        if (pcol.Flag != 0) return false;
        pcol.Flag = 1;
        if (++colsVisited == A.NCols) return true;
        for (var p = pcol.FirstRow; p != null; p = p.NextRow)
        {
            var prow = SparseMatrix.SmGetRow(A, p.RowNum)!;
            if (prow.Flag == 0 && VisitRow(A, prow, ref rowsVisited, ref colsVisited))
                return true;
        }
        return false;
    }

    private static void CopyRow(SmMatrix A, SmRow prow)
    {
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
            SparseMatrix.SmInsert(A, p.RowNum, p.ColNum);
    }
}
