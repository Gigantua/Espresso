using System.Text;

namespace EspressoCS;

public class SmElement
{
    public int RowNum;
    public int ColNum;
    public SmElement? NextRow;   // next row in this column
    public SmElement? PrevRow;
    public SmElement? NextCol;   // next column in this row
    public SmElement? PrevCol;
    public object? UserWord;
}

public class SmRow
{
    public int RowNum;
    public int Length;
    public int Flag;
    public SmElement? FirstCol;
    public SmElement? LastCol;
    public SmRow? NextRow;
    public SmRow? PrevRow;
    public object? UserWord;
}

public class SmCol
{
    public int ColNum;
    public int Length;
    public int Flag;
    public SmElement? FirstRow;
    public SmElement? LastRow;
    public SmCol? NextCol;
    public SmCol? PrevCol;
    public object? UserWord;
}

public class SmMatrix
{
    public SmRow?[]? Rows;       // pointer array indexed by row num
    public int RowsSize;
    public SmCol?[]? Cols;       // pointer array indexed by col num
    public int ColsSize;
    public SmRow? FirstRow;
    public SmRow? LastRow;
    public int NRows;
    public SmCol? FirstCol;
    public SmCol? LastCol;
    public int NCols;
    public object? UserWord;
}

public static class SparseMatrix
{
    // --- Indexed accessors (translate sm_get_row / sm_get_col macros) ---

    public static SmCol? SmGetCol(SmMatrix A, int colnum) =>
        colnum >= 0 && colnum < A.ColsSize ? A.Cols![colnum] : null;

    public static SmRow? SmGetRow(SmMatrix A, int rownum) =>
        rownum >= 0 && rownum < A.RowsSize ? A.Rows![rownum] : null;

    // -----------------------------------------------------------------------
    // Private sorted doubly-linked list insertion helpers
    // Translates the sorted_insert macro from sparse_int.h.
    // Each helper returns the element actually at that position (new or existing).
    // -----------------------------------------------------------------------

    private static SmRow SortedInsertMatrixRow(SmMatrix A, SmRow e)
    {
        int newval = e.RowNum;
        if (A.LastRow == null)
        {
            A.FirstRow = A.LastRow = e;
            e.NextRow = e.PrevRow = null;
            A.NRows++;
            return e;
        }
        if (A.LastRow.RowNum < newval)
        {
            A.LastRow.NextRow = e;
            e.PrevRow = A.LastRow;
            A.LastRow = e;
            e.NextRow = null;
            A.NRows++;
            return e;
        }
        if (A.FirstRow!.RowNum > newval)
        {
            A.FirstRow.PrevRow = e;
            e.NextRow = A.FirstRow;
            A.FirstRow = e;
            e.PrevRow = null;
            A.NRows++;
            return e;
        }
        SmRow p = A.FirstRow;
        while (p.RowNum < newval) p = p.NextRow!;
        if (p.RowNum > newval)
        {
            SmRow prev = p.PrevRow!;
            p.PrevRow = e;
            e.NextRow = p;
            prev.NextRow = e;
            e.PrevRow = prev;
            A.NRows++;
            return e;
        }
        return p; // already exists
    }

    private static SmCol SortedInsertMatrixCol(SmMatrix A, SmCol e)
    {
        int newval = e.ColNum;
        if (A.LastCol == null)
        {
            A.FirstCol = A.LastCol = e;
            e.NextCol = e.PrevCol = null;
            A.NCols++;
            return e;
        }
        if (A.LastCol.ColNum < newval)
        {
            A.LastCol.NextCol = e;
            e.PrevCol = A.LastCol;
            A.LastCol = e;
            e.NextCol = null;
            A.NCols++;
            return e;
        }
        if (A.FirstCol!.ColNum > newval)
        {
            A.FirstCol.PrevCol = e;
            e.NextCol = A.FirstCol;
            A.FirstCol = e;
            e.PrevCol = null;
            A.NCols++;
            return e;
        }
        SmCol p = A.FirstCol;
        while (p.ColNum < newval) p = p.NextCol!;
        if (p.ColNum > newval)
        {
            SmCol prev = p.PrevCol!;
            p.PrevCol = e;
            e.NextCol = p;
            prev.NextCol = e;
            e.PrevCol = prev;
            A.NCols++;
            return e;
        }
        return p; // already exists
    }

    // Insert element e into prow's col list sorted by ColNum; returns actual element at col.
    private static SmElement SortedInsertRowElement(SmRow prow, SmElement e, int col)
    {
        if (prow.LastCol == null)
        {
            prow.FirstCol = prow.LastCol = e;
            e.NextCol = e.PrevCol = null;
            prow.Length++;
            return e;
        }
        if (prow.LastCol.ColNum < col)
        {
            prow.LastCol.NextCol = e;
            e.PrevCol = prow.LastCol;
            prow.LastCol = e;
            e.NextCol = null;
            prow.Length++;
            return e;
        }
        if (prow.FirstCol!.ColNum > col)
        {
            prow.FirstCol.PrevCol = e;
            e.NextCol = prow.FirstCol;
            prow.FirstCol = e;
            e.PrevCol = null;
            prow.Length++;
            return e;
        }
        SmElement p = prow.FirstCol;
        while (p.ColNum < col) p = p.NextCol!;
        if (p.ColNum > col)
        {
            SmElement prev = p.PrevCol!;
            p.PrevCol = e;
            e.NextCol = p;
            prev.NextCol = e;
            e.PrevCol = prev;
            prow.Length++;
            return e;
        }
        return p; // already exists
    }

    // Insert element e into pcol's row list sorted by RowNum; returns actual element at row.
    private static SmElement SortedInsertColElement(SmCol pcol, SmElement e, int row)
    {
        if (pcol.LastRow == null)
        {
            pcol.FirstRow = pcol.LastRow = e;
            e.NextRow = e.PrevRow = null;
            pcol.Length++;
            return e;
        }
        if (pcol.LastRow.RowNum < row)
        {
            pcol.LastRow.NextRow = e;
            e.PrevRow = pcol.LastRow;
            pcol.LastRow = e;
            e.NextRow = null;
            pcol.Length++;
            return e;
        }
        if (pcol.FirstRow!.RowNum > row)
        {
            pcol.FirstRow.PrevRow = e;
            e.NextRow = pcol.FirstRow;
            pcol.FirstRow = e;
            e.PrevRow = null;
            pcol.Length++;
            return e;
        }
        SmElement p = pcol.FirstRow;
        while (p.RowNum < row) p = p.NextRow!;
        if (p.RowNum > row)
        {
            SmElement prev = p.PrevRow!;
            p.PrevRow = e;
            e.NextRow = p;
            prev.NextRow = e;
            e.PrevRow = prev;
            pcol.Length++;
            return e;
        }
        return p; // already exists
    }

    // -----------------------------------------------------------------------
    // Private dll_unlink helpers — translate the dll_unlink macro
    // -----------------------------------------------------------------------

    private static void DllUnlinkRowElement(SmElement p, SmRow prow)
    {
        if (p.PrevCol == null) prow.FirstCol = p.NextCol;
        else p.PrevCol.NextCol = p.NextCol;
        if (p.NextCol == null) prow.LastCol = p.PrevCol;
        else p.NextCol.PrevCol = p.PrevCol;
        prow.Length--;
    }

    private static void DllUnlinkColElement(SmElement p, SmCol pcol)
    {
        if (p.PrevRow == null) pcol.FirstRow = p.NextRow;
        else p.PrevRow.NextRow = p.NextRow;
        if (p.NextRow == null) pcol.LastRow = p.PrevRow;
        else p.NextRow.PrevRow = p.PrevRow;
        pcol.Length--;
    }

    private static void DllUnlinkMatrixRow(SmRow prow, SmMatrix A)
    {
        if (prow.PrevRow == null) A.FirstRow = prow.NextRow;
        else prow.PrevRow.NextRow = prow.NextRow;
        if (prow.NextRow == null) A.LastRow = prow.PrevRow;
        else prow.NextRow.PrevRow = prow.PrevRow;
        A.NRows--;
    }

    private static void DllUnlinkMatrixCol(SmCol pcol, SmMatrix A)
    {
        if (pcol.PrevCol == null) A.FirstCol = pcol.NextCol;
        else pcol.PrevCol.NextCol = pcol.NextCol;
        if (pcol.NextCol == null) A.LastCol = pcol.PrevCol;
        else pcol.NextCol.PrevCol = pcol.PrevCol;
        A.NCols--;
    }

    // Internal element-removal helpers (sm_row_remove_element / sm_col_remove_element)
    private static void SmRowRemoveElement(SmRow prow, SmElement p)
    {
        DllUnlinkRowElement(p, prow);
    }

    private static void SmColRemoveElement(SmCol pcol, SmElement p)
    {
        DllUnlinkColElement(p, pcol);
    }

    // -----------------------------------------------------------------------
    // Matrix allocation / duplication / resize  (matrix.c)
    // -----------------------------------------------------------------------

    public static SmMatrix SmAlloc() => new SmMatrix();

    public static SmMatrix SmAllocSize(int row, int col)
    {
        var A = SmAlloc();
        SmResize(A, row, col);
        return A;
    }

    public static void SmFree(SmMatrix A)
    {
        // GC reclaims objects; null out fields so references are dropped.
        A.Rows = null;
        A.Cols = null;
        A.FirstRow = A.LastRow = null;
        A.FirstCol = A.LastCol = null;
    }

    public static SmMatrix SmDup(SmMatrix A)
    {
        var B = SmAlloc();
        if (A.LastRow != null)
        {
            SmResize(B, A.LastRow.RowNum, A.LastCol!.ColNum);
            for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
                for (var p = prow.FirstCol; p != null; p = p.NextCol)
                    SmInsert(B, p.RowNum, p.ColNum);
        }
        return B;
    }

    public static void SmResize(SmMatrix A, int row, int col)
    {
        if (row >= A.RowsSize)
        {
            int newSize = Math.Max(A.RowsSize * 2, row + 1);
            var newRows = new SmRow?[newSize];
            if (A.Rows != null) Array.Copy(A.Rows, newRows, A.RowsSize);
            A.Rows = newRows;
            A.RowsSize = newSize;
        }
        if (col >= A.ColsSize)
        {
            int newSize = Math.Max(A.ColsSize * 2, col + 1);
            var newCols = new SmCol?[newSize];
            if (A.Cols != null) Array.Copy(A.Cols, newCols, A.ColsSize);
            A.Cols = newCols;
            A.ColsSize = newSize;
        }
    }

    // -----------------------------------------------------------------------
    // Element insert / find / remove  (matrix.c)
    // -----------------------------------------------------------------------

    public static SmElement SmInsert(SmMatrix A, int row, int col)
    {
        if (row >= A.RowsSize || col >= A.ColsSize)
            SmResize(A, row, col);

        // Get or create the row header
        SmRow? prow = A.Rows![row];
        if (prow == null)
        {
            var newRow = SmRowAlloc();
            newRow.RowNum = row;
            prow = SortedInsertMatrixRow(A, newRow);
            A.Rows[row] = prow;
        }

        // Get or create the col header
        SmCol? pcol = A.Cols![col];
        if (pcol == null)
        {
            var newCol = SmColAlloc();
            newCol.ColNum = col;
            pcol = SortedInsertMatrixCol(A, newCol);
            A.Cols[col] = pcol;
        }

        // Insert the element into the row's sorted col list
        var newElem = new SmElement { RowNum = row, ColNum = col };
        var element = SortedInsertRowElement(prow, newElem, col);

        // If the element was actually inserted (not a pre-existing duplicate),
        // also insert it into the column's sorted row list.
        if (ReferenceEquals(element, newElem))
            SortedInsertColElement(pcol, newElem, row);

        return element;
    }

    public static SmElement? SmFind(SmMatrix A, int rownum, int colnum)
    {
        var prow = SmGetRow(A, rownum);
        if (prow == null) return null;
        var pcol = SmGetCol(A, colnum);
        if (pcol == null) return null;
        return prow.Length < pcol.Length
            ? SmRowFind(prow, colnum)
            : SmColFind(pcol, rownum);
    }

    public static void SmRemove(SmMatrix A, int rownum, int colnum)
    {
        var p = SmFind(A, rownum, colnum);
        if (p != null) SmRemoveElement(A, p);
    }

    public static void SmRemoveElement(SmMatrix A, SmElement p)
    {
        var prow = SmGetRow(A, p.RowNum)!;
        DllUnlinkRowElement(p, prow);
        if (prow.FirstCol == null)
            SmDelRow(A, p.RowNum);

        var pcol = SmGetCol(A, p.ColNum)!;
        DllUnlinkColElement(p, pcol);
        if (pcol.FirstRow == null)
            SmDelCol(A, p.ColNum);
    }

    public static void SmDelRow(SmMatrix A, int i)
    {
        var prow = SmGetRow(A, i);
        if (prow != null)
        {
            // Walk the row, removing each element from its column.
            SmElement? p = prow.FirstCol;
            while (p != null)
            {
                SmElement? pnext = p.NextCol;
                var pcol = SmGetCol(A, p.ColNum)!;
                SmColRemoveElement(pcol, p);
                if (pcol.FirstRow == null)
                    SmDelCol(A, pcol.ColNum);
                p = pnext;
            }

            A.Rows![i] = null;
            DllUnlinkMatrixRow(prow, A);
            prow.FirstCol = prow.LastCol = null;
            SmRowFree(prow);
        }
    }

    public static void SmDelCol(SmMatrix A, int i)
    {
        var pcol = SmGetCol(A, i);
        if (pcol != null)
        {
            // Walk the column, removing each element from its row.
            SmElement? p = pcol.FirstRow;
            while (p != null)
            {
                SmElement? pnext = p.NextRow;
                var prow = SmGetRow(A, p.RowNum)!;
                SmRowRemoveElement(prow, p);
                if (prow.FirstCol == null)
                    SmDelRow(A, prow.RowNum);
                p = pnext;
            }

            A.Cols![i] = null;
            DllUnlinkMatrixCol(pcol, A);
            pcol.FirstRow = pcol.LastRow = null;
            SmColFree(pcol);
        }
    }

    public static void SmCopyRow(SmMatrix dest, int destRow, SmRow prow)
    {
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
            SmInsert(dest, destRow, p.ColNum);
    }

    public static void SmCopyCol(SmMatrix dest, int destCol, SmCol pcol)
    {
        for (var p = pcol.FirstRow; p != null; p = p.NextRow)
            SmInsert(dest, destCol, p.RowNum);
    }

    // -----------------------------------------------------------------------
    // I/O  (matrix.c)
    // -----------------------------------------------------------------------

    public static void SmWrite(TextWriter fp, SmMatrix A)
    {
        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
            for (var p = prow.FirstCol; p != null; p = p.NextCol)
                fp.WriteLine($"{p.RowNum} {p.ColNum}");
    }

    public static void SmPrint(TextWriter fp, SmMatrix A)
    {
        if (A.LastCol == null) return;

        if (A.LastCol.ColNum >= 100)
        {
            fp.Write("    ");
            for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
                fp.Write((pcol.ColNum / 100) % 10);
            fp.WriteLine();
        }

        if (A.LastCol.ColNum >= 10)
        {
            fp.Write("    ");
            for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
                fp.Write((pcol.ColNum / 10) % 10);
            fp.WriteLine();
        }

        fp.Write("    ");
        for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
            fp.Write(pcol.ColNum % 10);
        fp.WriteLine();

        fp.Write("    ");
        for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
            fp.Write('-');
        fp.WriteLine();

        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
        {
            fp.Write($"{prow.RowNum,3}:");
            for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
                fp.Write(SmRowFind(prow, pcol.ColNum) != null ? '1' : '.');
            fp.WriteLine();
        }
    }

    public static void SmDump(SmMatrix A, string s, int max)
    {
        Console.WriteLine($"{s} {A.NRows} rows by {A.NCols} cols");
        if (A.NRows < max)
            SmPrint(Console.Out, A);
    }

    public static void SmCleanup() { } // no-op: no free-lists in C# port

    public static int SmRead(TextReader fp, out SmMatrix A)
    {
        A = SmAlloc();
        string? line;
        while ((line = fp.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return 0;
            if (!int.TryParse(parts[0], out int i) || !int.TryParse(parts[1], out int j))
                return 0;
            SmInsert(A, i, j);
        }
        return 1;
    }

    public static int SmReadCompressed(TextReader fp, out SmMatrix A)
    {
        A = SmAlloc();

        var tok = ReadNextToken(fp);
        if (tok == null || !int.TryParse(tok, out int nrows)) return 0;
        tok = ReadNextToken(fp);
        if (tok == null || !int.TryParse(tok, out int ncols)) return 0;
        SmResize(A, nrows, ncols);

        for (int i = 0; i < nrows; i++)
        {
            // Read and discard one hex row-header value (matches C fscanf("%lx",&x) that is then overwritten)
            tok = ReadNextToken(fp);
            if (tok == null) return 0;

            for (int j = 0; j < ncols; j += 32)
            {
                tok = ReadNextToken(fp);
                if (tok == null) return 0;
                if (!TryParseHexUlong(tok, out ulong x)) return 0;
                for (int k = j; x != 0; x >>= 1, k++)
                {
                    if ((x & 1) != 0)
                        SmInsert(A, i, k);
                }
            }
        }
        return 1;
    }

    private static string? ReadNextToken(TextReader fp)
    {
        int ch;
        while ((ch = fp.Read()) != -1 && char.IsWhiteSpace((char)ch)) { }
        if (ch == -1) return null;
        var sb = new StringBuilder();
        sb.Append((char)ch);
        while (fp.Peek() != -1 && !char.IsWhiteSpace((char)fp.Peek()))
            sb.Append((char)fp.Read());
        return sb.ToString();
    }

    private static bool TryParseHexUlong(string s, out ulong value) =>
        ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out value);

    // -----------------------------------------------------------------------
    // Longest row / col  (matrix.c)
    // -----------------------------------------------------------------------

    public static SmRow? SmLongestRow(SmMatrix A)
    {
        SmRow? largeRow = null;
        int maxLength = 0;
        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
        {
            if (prow.Length > maxLength)
            {
                maxLength = prow.Length;
                largeRow = prow;
            }
        }
        return largeRow;
    }

    public static SmCol? SmLongestCol(SmMatrix A)
    {
        SmCol? largeCol = null;
        int maxLength = 0;
        for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
        {
            if (pcol.Length > maxLength)
            {
                maxLength = pcol.Length;
                largeCol = pcol;
            }
        }
        return largeCol;
    }

    // -----------------------------------------------------------------------
    // Row operations  (rows.c)
    // -----------------------------------------------------------------------

    public static SmRow SmRowAlloc() => new SmRow();

    public static void SmRowFree(SmRow prow) { } // GC handles reclamation

    public static SmRow SmRowDup(SmRow prow)
    {
        var pnew = SmRowAlloc();
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
            SmRowInsert(pnew, p.ColNum);
        return pnew;
    }

    public static SmElement SmRowInsert(SmRow prow, int col)
    {
        var newElem = new SmElement { ColNum = col };
        return SortedInsertRowElement(prow, newElem, col);
    }

    public static SmElement? SmRowFind(SmRow prow, int col)
    {
        SmElement? p;
        for (p = prow.FirstCol; p != null && p.ColNum < col; p = p.NextCol) { }
        return p != null && p.ColNum == col ? p : null;
    }

    public static void SmRowRemove(SmRow prow, int col)
    {
        SmElement? p;
        for (p = prow.FirstCol; p != null && p.ColNum < col; p = p.NextCol) { }
        if (p != null && p.ColNum == col)
            DllUnlinkRowElement(p, prow);
    }

    public static void SmRowPrint(TextWriter fp, SmRow prow)
    {
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
            fp.Write($" {p.ColNum}");
    }

    // Returns true if p2 contains all elements of p1.
    public static bool SmRowContains(SmRow p1, SmRow p2)
    {
        var q1 = p1.FirstCol;
        var q2 = p2.FirstCol;
        while (q1 != null)
        {
            if (q2 == null || q1.ColNum < q2.ColNum) return false;
            if (q1.ColNum == q2.ColNum) { q1 = q1.NextCol; q2 = q2.NextCol; }
            else q2 = q2.NextCol;
        }
        return true;
    }

    public static bool SmRowIntersects(SmRow p1, SmRow p2)
    {
        var q1 = p1.FirstCol;
        var q2 = p2.FirstCol;
        if (q1 == null || q2 == null) return false;
        for (;;)
        {
            if (q1.ColNum < q2.ColNum)
            {
                if ((q1 = q1.NextCol) == null) return false;
            }
            else if (q1.ColNum > q2.ColNum)
            {
                if ((q2 = q2.NextCol) == null) return false;
            }
            else
            {
                return true;
            }
        }
    }

    public static int SmRowCompare(SmRow p1, SmRow p2)
    {
        var q1 = p1.FirstCol;
        var q2 = p2.FirstCol;
        while (q1 != null && q2 != null)
        {
            if (q1.ColNum != q2.ColNum)
                return q1.ColNum - q2.ColNum;
            q1 = q1.NextCol;
            q2 = q2.NextCol;
        }
        if (q1 != null) return 1;
        if (q2 != null) return -1;
        return 0;
    }

    public static SmRow? SmRowAnd(SmRow p1, SmRow p2)
    {
        var result = SmRowAlloc();
        var q1 = p1.FirstCol;
        var q2 = p2.FirstCol;
        if (q1 == null || q2 == null) return result;
        for (;;)
        {
            if (q1.ColNum < q2.ColNum)
            {
                if ((q1 = q1.NextCol) == null) return result;
            }
            else if (q1.ColNum > q2.ColNum)
            {
                if ((q2 = q2.NextCol) == null) return result;
            }
            else
            {
                SmRowInsert(result, q1.ColNum);
                if ((q1 = q1.NextCol) == null) return result;
                if ((q2 = q2.NextCol) == null) return result;
            }
        }
    }

    public static int SmRowHash(SmRow prow, int modulus)
    {
        int sum = 0;
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
            sum = (sum * 17 + p.ColNum) % modulus;
        return sum;
    }

    // -----------------------------------------------------------------------
    // Column operations  (cols.c)
    // -----------------------------------------------------------------------

    public static SmCol SmColAlloc() => new SmCol();

    public static void SmColFree(SmCol pcol) { } // GC handles reclamation

    public static SmCol SmColDup(SmCol pcol)
    {
        var pnew = SmColAlloc();
        for (var p = pcol.FirstRow; p != null; p = p.NextRow)
            SmColInsert(pnew, p.RowNum);
        return pnew;
    }

    public static SmElement SmColInsert(SmCol pcol, int row)
    {
        var newElem = new SmElement { RowNum = row };
        return SortedInsertColElement(pcol, newElem, row);
    }

    public static SmElement? SmColFind(SmCol pcol, int row)
    {
        SmElement? p;
        for (p = pcol.FirstRow; p != null && p.RowNum < row; p = p.NextRow) { }
        return p != null && p.RowNum == row ? p : null;
    }

    public static void SmColRemove(SmCol pcol, int row)
    {
        SmElement? p;
        for (p = pcol.FirstRow; p != null && p.RowNum < row; p = p.NextRow) { }
        if (p != null && p.RowNum == row)
            DllUnlinkColElement(p, pcol);
    }

    public static void SmColPrint(TextWriter fp, SmCol pcol)
    {
        for (var p = pcol.FirstRow; p != null; p = p.NextRow)
            fp.Write($" {p.RowNum}");
    }

    // Returns true if p2 contains all elements of p1.
    public static bool SmColContains(SmCol p1, SmCol p2)
    {
        var q1 = p1.FirstRow;
        var q2 = p2.FirstRow;
        while (q1 != null)
        {
            if (q2 == null || q1.RowNum < q2.RowNum) return false;
            if (q1.RowNum == q2.RowNum) { q1 = q1.NextRow; q2 = q2.NextRow; }
            else q2 = q2.NextRow;
        }
        return true;
    }

    public static bool SmColIntersects(SmCol p1, SmCol p2)
    {
        var q1 = p1.FirstRow;
        var q2 = p2.FirstRow;
        if (q1 == null || q2 == null) return false;
        for (;;)
        {
            if (q1.RowNum < q2.RowNum)
            {
                if ((q1 = q1.NextRow) == null) return false;
            }
            else if (q1.RowNum > q2.RowNum)
            {
                if ((q2 = q2.NextRow) == null) return false;
            }
            else
            {
                return true;
            }
        }
    }

    public static int SmColCompare(SmCol p1, SmCol p2)
    {
        var q1 = p1.FirstRow;
        var q2 = p2.FirstRow;
        while (q1 != null && q2 != null)
        {
            if (q1.RowNum != q2.RowNum)
                return q1.RowNum - q2.RowNum;
            q1 = q1.NextRow;
            q2 = q2.NextRow;
        }
        if (q1 != null) return 1;
        if (q2 != null) return -1;
        return 0;
    }

    public static SmCol? SmColAnd(SmCol p1, SmCol p2)
    {
        var result = SmColAlloc();
        var q1 = p1.FirstRow;
        var q2 = p2.FirstRow;
        if (q1 == null || q2 == null) return result;
        for (;;)
        {
            if (q1.RowNum < q2.RowNum)
            {
                if ((q1 = q1.NextRow) == null) return result;
            }
            else if (q1.RowNum > q2.RowNum)
            {
                if ((q2 = q2.NextRow) == null) return result;
            }
            else
            {
                SmColInsert(result, q1.RowNum);
                if ((q1 = q1.NextRow) == null) return result;
                if ((q2 = q2.NextRow) == null) return result;
            }
        }
    }

    public static int SmColHash(SmCol pcol, int modulus)
    {
        int sum = 0;
        for (var p = pcol.FirstRow; p != null; p = p.NextRow)
            sum = (sum * 17 + p.RowNum) % modulus;
        return sum;
    }

    // -----------------------------------------------------------------------
    // Block partition  (part.c)
    // -----------------------------------------------------------------------

    private static void CopyRow(SmMatrix A, SmRow prow)
    {
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
            SmInsert(A, p.RowNum, p.ColNum);
    }

    private static bool VisitCol(SmMatrix A, SmCol pcol, ref int rowsVisited, ref int colsVisited)
    {
        if (pcol.Flag != 0) return false;
        pcol.Flag = 1;
        colsVisited++;
        if (colsVisited == A.NCols) return true;
        for (var p = pcol.FirstRow; p != null; p = p.NextRow)
        {
            var prow = SmGetRow(A, p.RowNum);
            if (prow != null && prow.Flag == 0)
                if (VisitRow(A, prow, ref rowsVisited, ref colsVisited))
                    return true;
        }
        return false;
    }

    private static bool VisitRow(SmMatrix A, SmRow prow, ref int rowsVisited, ref int colsVisited)
    {
        if (prow.Flag != 0) return false;
        prow.Flag = 1;
        rowsVisited++;
        if (rowsVisited == A.NRows) return true;
        for (var p = prow.FirstCol; p != null; p = p.NextCol)
        {
            var pcol = SmGetCol(A, p.ColNum);
            if (pcol != null && pcol.Flag == 0)
                if (VisitCol(A, pcol, ref rowsVisited, ref colsVisited))
                    return true;
        }
        return false;
    }

    public static bool SmBlockPartition(SmMatrix A, out SmMatrix L, out SmMatrix R)
    {
        L = R = SmAlloc();
        if (A.NRows == 0) return false;

        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
            prow.Flag = 0;
        for (var pcol = A.FirstCol; pcol != null; pcol = pcol.NextCol)
            pcol.Flag = 0;

        int colsVisited = 0, rowsVisited = 0;
        if (VisitRow(A, A.FirstRow!, ref rowsVisited, ref colsVisited))
            return false;

        L = SmAlloc();
        R = SmAlloc();
        for (var prow = A.FirstRow; prow != null; prow = prow.NextRow)
        {
            if (prow.Flag != 0) CopyRow(L, prow);
            else CopyRow(R, prow);
        }
        return true;
    }
}
