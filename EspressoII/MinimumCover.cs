using System.Buffers;
using System.Runtime.CompilerServices;
namespace Espresso;
using static BitVectorFamily;
using static BitVectorOps;
public sealed class SortedIntArray
{
    private int[] _items;
    private int _count;
    public SortedIntArray() => _items = [];
    public SortedIntArray(SortedIntArray other)
    {
        _count = other._count;
        _items = _count > 0 ? other._items.AsSpan(0, _count).ToArray() : [];
    }
    public static SortedIntArray FromSorted(ReadOnlySpan<int> sorted)
    {
        var a = new SortedIntArray { _items = sorted.ToArray(), _count = sorted.Length };
        return a;
    }
    public int Count { get => _count; }
    public int Min { get => _items[0]; }
    public int Max { get => _items[_count - 1]; }
    public void Add(int value)
    {
        // Monotone-append fast path (common when inserting columns in order)
        if (_count > 0 && value > _items[_count - 1])
        {
            if (_count == _items.Length)
            {
                var n = new int[Math.Max(_items.Length * 2, 4)];
                Array.Copy(_items, n, _count);
                _items = n;
            }
            _items[_count++] = value;
            return;
        }
        int pos = Array.BinarySearch(_items, 0, _count, value);
        if (pos >= 0) return;
        pos = ~pos;
        if (_count == _items.Length)
        {
            var n = new int[Math.Max(_items.Length * 2, 4)];
            if (_count > 0) Array.Copy(_items, n, _count);
            _items = n;
        }
        if (pos < _count) Array.Copy(_items, pos, _items, pos + 1, _count - pos);
        _items[pos] = value;
        _count++;
    }
    public void Remove(int value)
    {
        int pos = Array.BinarySearch(_items, 0, _count, value);
        if (pos < 0) return;
        _count--;
        if (pos < _count) Array.Copy(_items, pos + 1, _items, pos, _count - pos);
    }
    public bool Overlaps(SortedIntArray other)
    {
        int ai = 0, bi = 0, ac = _count, bc = other._count;
        var a = _items; var b = other._items;
        while (ai < ac && bi < bc)
        {
            if (a[ai] < b[bi]) ai++;
            else if (a[ai] > b[bi]) bi++;
            else return true;
        }
        return false;
    }
    public bool IsSupersetOf(SortedIntArray other)
    {
        int ac = _count, bc = other._count;
        if (ac < bc) return false;
        int ai = 0, bi = 0;
        var a = _items; var b = other._items;
        while (bi < bc)
        {
            if (ai >= ac) return false;
            if (a[ai] < b[bi]) ai++;
            else if (a[ai] == b[bi]) { ai++; bi++; }
            else return false;
        }
        return true;
    }
    public void CopyTo(int[] array, int index) => Array.Copy(_items, 0, array, index, _count);
    public Enumerator GetEnumerator() => new(_items, _count);
    public struct Enumerator
    {
        private readonly int[] _items;
        private readonly int _count;
        private int _index;
        internal Enumerator(int[] items, int count) { _items = items; _count = count; _index = -1; }
        public int Current { get => _items[_index]; }
        public bool MoveNext() => ++_index < _count;
    }
}
public class SparseEntry
{
    public int Key;
    public SortedIntArray Refs = new();
    public int RowNum { get => Key; set => Key = value; }
    public SortedIntArray Cols => Refs;
}
public sealed class SparseDict
{
    private SparseEntry?[] _items;
    private int _count;
    public int Count
    {
        get => _count;
    }
    public int Capacity => _items.Length;
    public SparseDict(int capacity = 16) => _items = new SparseEntry?[Math.Max(capacity, 4)];
    public SparseEntry? this[int key]
    {
        get => (uint)key < (uint)_items.Length ? _items[key] : null;
    }
    public bool TryGetValue(int key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SparseEntry? entry)
    {
        if ((uint)key < (uint)_items.Length)
        {
            entry = _items[key];
            return entry != null;
        }
        entry = null;
        return false;
    }
    public void Set(int key, SparseEntry entry)
    {
        EnsureCapacity(key + 1);
        if (_items[key] == null) _count++;
        _items[key] = entry;
    }
    public bool Remove(int key)
    {
        if ((uint)key >= (uint)_items.Length || _items[key] == null) return false;
        _items[key] = null;
        _count--;
        return true;
    }
    public int CopyKeysTo(int[] buf)
    {
        int idx = 0;
        for (int i = 0; i < _items.Length && idx < _count; i++)
            if (_items[i] != null) buf[idx++] = i;
        return idx;
    }
    private void EnsureCapacity(int needed)
    {
        if (needed <= _items.Length) return;
        int newSize = _items.Length;
        while (newSize < needed) newSize *= 2;
        Array.Resize(ref _items, newSize);
    }
    public Enumerator GetEnumerator() => new(_items, _count);
    public struct Enumerator
    {
        private readonly SparseEntry?[] _items;
        private readonly int _total;
        private int _index;
        private int _seen;
        internal Enumerator(SparseEntry?[] items, int count) { _items = items; _total = count; _index = -1; _seen = 0; }
        public SparseEntry Current
        {
            get => _items[_index]!;
        }
        public bool MoveNext()
        {
            if (_seen >= _total) return false;
            while (++_index < _items.Length)
            {
                if (_items[_index] != null) { _seen++; return true; }
            }
            return false;
        }
    }
}
public class SparseMatrix
{
    public SparseDict Rows, Cols;
    public int LastRowNum = -1;
    public int NRows => Rows.Count;
    public int NCols => Cols.Count;
    public SparseMatrix(int rowCap = 16, int colCap = 16)
    {
        Rows = new SparseDict(rowCap);
        Cols = new SparseDict(colCap);
    }
    public static SparseMatrix Clone(SparseMatrix A)
    {
        var B = new SparseMatrix(A.Rows.Capacity, A.Cols.Capacity);
        foreach (var e in A.Rows) B.Rows.Set(e.Key, new SparseEntry { Key = e.Key, Refs = new SortedIntArray(e.Refs) });
        foreach (var e in A.Cols) B.Cols.Set(e.Key, new SparseEntry { Key = e.Key, Refs = new SortedIntArray(e.Refs) });
        B.LastRowNum = A.LastRowNum;
        return B;
    }
    public static void Insert(SparseMatrix A, int row, int col)
    {
        if (!A.Rows.TryGetValue(row, out var prow)) { prow = new SparseEntry { Key = row }; A.Rows.Set(row, prow); if (row > A.LastRowNum) A.LastRowNum = row; }
        if (!A.Cols.TryGetValue(col, out var pcol)) { pcol = new SparseEntry { Key = col }; A.Cols.Set(col, pcol); }
        prow.Refs.Add(col);
        pcol.Refs.Add(row);
    }
    public static void Delete(SparseDict primary, SparseDict secondary, int i)
    {
        if (!primary.TryGetValue(i, out var entry)) return;
        foreach (int r in entry.Refs)
            if (secondary.TryGetValue(r, out var other))
            {
                other.Refs.Remove(i);
                if (other.Refs.Count == 0) secondary.Remove(r);
            }
        primary.Remove(i);
    }
    public static void DeleteRow(SparseMatrix A, int i) => Delete(A.Rows, A.Cols, i);
    public static void DeleteColumn(SparseMatrix A, int i) => Delete(A.Cols, A.Rows, i);
}
public static class MinimumCoverSolver
{
    public static SparseEntry Solve(SparseMatrix A)
    {
        if (A.NRows <= 0) return new SparseEntry();
        var best = SolveRecursive(SparseMatrix.Clone(A), new CoverSolution(), 0, A.NCols + 1);
        var sol = new SparseEntry { Key = best!.Entry.Key, Refs = new SortedIntArray(best.Entry.Refs) };
        foreach (var prow in A.Rows)
            if (!prow.Refs.Overlaps(sol.Refs)) throw new InvalidOperationException("mincov: internal error -- cover verification failed\n");
        return sol;
    }
    private static CoverSolution? SolveRecursive(SparseMatrix A, CoverSolution select, int lb, int bound)
    {
        bool essenDone = false;
        {
            int delcols, delrows, essenCount;
            do
            {
                delcols = Dominance.ApplyDominance(A, false);
                var essen = new SortedIntArray();
                foreach (var prow in A.Rows)
                    if (prow.Refs.Count == 1) essen.Add(prow.Refs.Min);
                essenCount = essen.Count;
                int[] essenBuf = ArrayPool<int>.Shared.Rent(Math.Max(essenCount, 1));
                essen.CopyTo(essenBuf, 0);
                try
                {
                    for (int ei = 0; ei < essenCount; ei++)
                    {
                        select.AcceptColumn(A, essenBuf[ei]);
                        if (select.Cost >= bound) { essenDone = true; break; }
                    }
                }
                finally { ArrayPool<int>.Shared.Return(essenBuf); }
                if (essenDone) break;
                delrows = Dominance.ApplyDominance(A, true);
            } while (delcols > 0 || delrows > 0 || essenCount > 0);
        }
        if (select.Cost >= bound) return null;
        {
            SparseEntry c1g = default!, c2g = default!;
            int primaryRowNum = 0, secondaryRowNum = 0;
            bool reduceIt = false;
            foreach (var prow in A.Rows)
            {
                if (prow.Refs.Count != 2) continue;
                c1g = A.Cols[prow.Refs.Min]!;
                c2g = A.Cols[prow.Refs.Max]!;
                if (c1g.Refs.Count == 2) reduceIt = true;
                else if (c2g.Refs.Count == 2) { (c1g, c2g) = (c2g, c1g); reduceIt = true; }
                if (reduceIt)
                {
                    primaryRowNum = prow.Key;
                    secondaryRowNum = c1g.Refs.Min == primaryRowNum ? c1g.Refs.Max : c1g.Refs.Min;
                    break;
                }
            }
            if (reduceIt)
            {
                int c1Key = c1g.Key, c2Key = c2g.Key;
                var secEntry = A.Rows[secondaryRowNum]!;
                var saveSec = new SparseEntry { Key = secEntry.Key, Refs = new SortedIntArray(secEntry.Refs) };
                saveSec.Refs.Remove(c1Key);
                int c2RefCnt = c2g.Refs.Count;
                int[] c2RefBuf = ArrayPool<int>.Shared.Rent(c2RefCnt);
                c2g.Refs.CopyTo(c2RefBuf, 0);
                for (int ri = 0; ri < c2RefCnt; ri++)
                    if (c2RefBuf[ri] != primaryRowNum)
                        foreach (int col in saveSec.Refs) SparseMatrix.Insert(A, c2RefBuf[ri], col);
                ArrayPool<int>.Shared.Return(c2RefBuf);
                SparseMatrix.DeleteColumn(A, c1Key);
                SparseMatrix.DeleteColumn(A, c2Key);
                SparseMatrix.DeleteRow(A, primaryRowNum);
                SparseMatrix.DeleteRow(A, secondaryRowNum);
                var gBest = SolveRecursive(A, select, lb - 1, bound - 1);
                if (gBest != null)
                {
                    if (saveSec.Refs.Overlaps(gBest.Entry.Refs)) gBest.Add(c2Key);
                    else gBest.Add(c1Key);
                }
                return gBest;
            }
        }
        var indep = new CoverSolution();
        {
            var B = new SparseMatrix();
            foreach (var prow in A.Rows)
            {
                int totalCap = 0;
                foreach (int col in prow.Refs) totalCap += A.Cols[col]!.Refs.Count;
                int[] bimBuf = ArrayPool<int>.Shared.Rent(Math.Max(totalCap, 1));
                int bc = 0;
                foreach (int col in prow.Refs)
                {
                    var colEntry = A.Cols[col]!;
                    colEntry.Refs.CopyTo(bimBuf, bc);
                    bc += colEntry.Refs.Count;
                }
                Array.Sort(bimBuf, 0, bc);
                int unique = 0;
                for (int i = 0; i < bc; i++)
                    if (unique == 0 || bimBuf[unique - 1] != bimBuf[i]) bimBuf[unique++] = bimBuf[i];
                var rowSet = SortedIntArray.FromSorted(bimBuf.AsSpan(0, unique));
                ArrayPool<int>.Shared.Return(bimBuf);
                B.Rows.Set(prow.Key, new SparseEntry { Key = prow.Key, Refs = rowSet });
                foreach (int row in rowSet)
                {
                    if (!B.Cols.TryGetValue(row, out var pcol)) { pcol = new SparseEntry { Key = row }; B.Cols.Set(row, pcol); }
                    pcol.Refs.Add(prow.Key);
                }
            }
            while (B.Rows.Count > 0)
            {
                SparseEntry bestRow = null!;
                foreach (var bprow in B.Rows)
                    if (bestRow == null || bprow.Refs.Count < bestRow.Refs.Count) bestRow = bprow;
                indep.Cost++;
                indep.Entry.Refs.Add(bestRow.Key);
                int cnt = bestRow.Refs.Count;
                int[] buf = ArrayPool<int>.Shared.Rent(cnt);
                bestRow.Refs.CopyTo(buf, 0);
                for (int ci = 0; ci < cnt; ci++)
                {
                    SparseMatrix.DeleteRow(B, buf[ci]);
                    SparseMatrix.DeleteColumn(B, buf[ci]);
                }
                ArrayPool<int>.Shared.Return(buf);
            }
        }
        int lbNew = Math.Max(select.Cost + indep.Cost, lb);
        if (lbNew >= bound) return null;
        if (A.NRows == 0) return select.Clone();
        SparseMatrix tbpL = new(), tbpR = new();
        int blockResult = 0;
        if (A.NRows > 0)
        {
            SparseEntry? firstRow = null;
            foreach (var e in A.Rows) { firstRow = e; break; }
            var visitedRows = new HashSet<int>();
            var visitedCols = new HashSet<int>();
            if (!Dominance.Visit(A, firstRow!, visitedRows, visitedCols, A.Rows, A.Cols))
            {
                foreach (var prow in A.Rows)
                {
                    var target = visitedRows.Contains(prow.Key) ? tbpL : tbpR;
                    target.Rows.Set(prow.Key, new SparseEntry { Key = prow.Key, Refs = new SortedIntArray(prow.Refs) });
                    foreach (int col in prow.Refs)
                    {
                        if (!target.Cols.TryGetValue(col, out var pcol)) { pcol = new SparseEntry { Key = col }; target.Cols.Set(col, pcol); }
                        pcol.Refs.Add(prow.Key);
                    }
                }
                blockResult = 1;
            }
        }
        if (blockResult != 0)
        {
            if (tbpL.NCols > tbpR.NCols) { var t = tbpL; tbpL = tbpR; tbpR = t; }
            var leftBest = SolveRecursive(tbpL, new CoverSolution(), 0, bound - select.Cost);
            if (leftBest is null) return null;
            foreach (int col in leftBest.Entry.Refs) select.Add(col);
            return SolveRecursive(tbpR, select, lbNew, bound);
        }
        int branchCol;
        {
            int totalCap = 0;
            foreach (int indepRow in indep.Entry.Refs) totalCap += A.Rows[indepRow]!.Refs.Count;
            int[] sbBuf = ArrayPool<int>.Shared.Rent(Math.Max(totalCap, 1));
            int bc = 0;
            foreach (int indepRow in indep.Entry.Refs)
            {
                var rowEntry = A.Rows[indepRow]!;
                rowEntry.Refs.CopyTo(sbBuf, bc);
                bc += rowEntry.Refs.Count;
            }
            Array.Sort(sbBuf, 0, bc);
            int bestCol = -1; double bestW = -1.0;
            for (int i = 0; i < bc; i++)
            {
                if (i > 0 && sbBuf[i] == sbBuf[i - 1]) continue;
                var sbpcol = A.Cols[sbBuf[i]]!;
                double w = 0.0;
                foreach (int row in sbpcol.Refs) { var rEntry = A.Rows[row]!; w += 1.0 / ((double)rEntry.Refs.Count - 1.0); }
                if (w > bestW) { bestCol = sbpcol.Key; bestW = w; }
            }
            ArrayPool<int>.Shared.Return(sbBuf);
            branchCol = bestCol;
        }
        var A1 = SparseMatrix.Clone(A);
        var select1 = select.Clone();
        select1.AcceptColumn(A1, branchCol);
        return SolveRecursive(A1, select1, lbNew, bound);
    }
}
public static class UnateComplement
{
    internal static BitVectorFamily ComplementRecursive(BitVectorFamily A)
    {
        if (A.Count == 0)
        {
            var r = BitVectorFamily.Create(1, A.SfSize);
            r.GetSpan(r.Count++).Clear();
            return r;
        }
        if (A.Count == 1)
        {
            ReadOnlySpan<uint> sp0 = A.GetSpan(0);
            var Abar = BitVectorFamily.Create(A.SfSize, A.SfSize);
            for (int i = 0; i < A.SfSize; i++)
                if (BitVectorOps.Contains(sp0, i))
                {
                    Span<uint> sp1 = Abar.GetSpan(Abar.Count++);
                    sp1.Clear();
                    BitVectorOps.Insert(sp1, i);
                }
            return Abar;
        }
        var prestrict = BitVectorOps.Create(A.SfSize);
        Span<uint> spr = prestrict.AsSpan();
        uint minSetOrd = (uint)(A.SfSize + 1);
        for (int si = 0; si < A.Count; si++)
        {
            uint sz = (uint)BitVectorOps.GetSortKey(A.GetSet(si));
            if (sz < minSetOrd) { BitVectorOps.Copy(spr, A.GetSpan(si)); minSetOrd = sz; }
            else if (sz == minSetOrd) BitVectorOps.Or(spr, spr, A.GetSpan(si));
        }
        if (minSetOrd == 0) { A.Count = 0; return A; }
        if (minSetOrd == 1)
        {
            var rdf = BitVectorFamily.Create(A.Count, A.SfSize);
            for (int rsi = 0; rsi < A.Count; rsi++)
                if (BitVectorOps.AreDisjoint(A.GetSpan(rsi), spr))
                {
                    Array.Copy(A.Data, rsi * A.Stride, rdf.Data, rdf.Count * rdf.Stride, A.Stride);
                    rdf.Count++;
                }
            var Abar = ComplementRecursive(rdf);
            for (int si = 0; si < Abar.Count; si++) BitVectorOps.Or(Abar.GetSpan(si), Abar.GetSpan(si), spr);
            return Abar;
        }
        int maxI;
        {
            int words = A.Words;
            int[] sbcCount = ArrayPool<int>.Shared.Rent(A.SfSize);
            Array.Clear(sbcCount, 0, A.SfSize);
            for (int bsi = 0; bsi < A.Count; bsi++)
            {
                var bsp = A.GetSpan(bsi);
                int weight = 1024 / (BitVectorOps.PopCount(A.GetSpan(bsi)) - 1);
                for (int bi = 0; bi < words; bi++)
                {
                    uint bval = bsp[bi] & spr[bi];
                    int bb = bi << BitVectorOps.LogBpi;
                    while (bval != 0)
                    {
                        sbcCount[bb + System.Numerics.BitOperations.TrailingZeroCount(bval)] += weight;
                        bval &= bval - 1;
                    }
                }
            }
            int bestVar = -1, bestCount = 0;
            for (int bi = 0; bi < A.SfSize; bi++)
                if (sbcCount[bi] > bestCount) { bestVar = bi; bestCount = sbcCount[bi]; }
            ArrayPool<int>.Shared.Return(sbcCount, clearArray: false);
            if (bestVar == -1) throw new InvalidOperationException("abs_select_restricted: should not have best_var == -1");
            maxI = bestVar;
        }
        var rncb = BitVectorFamily.Create(A.Count, A.SfSize);
        for (int rsi = 0; rsi < A.Count; rsi++)
            if (!BitVectorOps.Contains(A.GetSpan(rsi), maxI))
            {
                Array.Copy(A.Data, rsi * A.Stride, rncb.Data, rncb.Count * rncb.Stride, A.Stride);
                rncb.Count++;
            }
        var result = ComplementRecursive(rncb);
        for (int si = 0; si < result.Count; si++) BitVectorOps.Insert(result.GetSpan(si), maxI);
        for (int si = 0; si < A.Count; si++)
        {
            Span<uint> sp = A.GetSpan(si);
            if (BitVectorOps.Contains(sp, maxI))
            {
                BitVectorOps.Remove(sp, maxI);
                BitVectorOps.SetSortKey(A.GetSet(si), BitVectorOps.GetSortKey(A.GetSet(si)) - 1);
            }
        }
        return BitVectorFamily.Append(result, ComplementRecursive(A));
    }
}
public class CoverSolution
{
    public SparseEntry Entry = new();
    public int Cost;
    public CoverSolution Clone() => new() { Cost = Cost, Entry = new SparseEntry { Key = Entry.Key, Refs = new SortedIntArray(Entry.Refs) } };
    public void Add(int col) { Entry.Refs.Add(col); Cost++; }
    public void AcceptColumn(SparseMatrix A, int col)
    {
        Add(col);
        if (!A.Cols.TryGetValue(col, out var pcol)) return;
        int rowCnt = pcol.Refs.Count;
        int[] rowBuf = ArrayPool<int>.Shared.Rent(rowCnt);
        pcol.Refs.CopyTo(rowBuf, 0);
        for (int ri = 0; ri < rowCnt; ri++) SparseMatrix.DeleteRow(A, rowBuf[ri]);
        ArrayPool<int>.Shared.Return(rowBuf);
    }
}
public static class Dominance
{
    public static int ApplyDominance(SparseMatrix A, bool isRow)
    {
        var primary = isRow ? A.Rows : A.Cols;
        var secondary = isRow ? A.Cols : A.Rows;
        int initCount = primary.Count;
        int[] keyBuf = ArrayPool<int>.Shared.Rent(Math.Max(initCount, 1));
        primary.CopyKeysTo(keyBuf);
        for (int ki = 0; ki < initCount; ki++)
        {
            if (!primary.TryGetValue(keyBuf[ki], out var entry)) continue;
            if (entry.Refs.Count == 0) continue;
            var least = secondary[entry.Refs.Min]!;
            foreach (int r in entry.Refs)
            {
                var s = secondary[r]!;
                if (s.Refs.Count < least.Refs.Count) least = s;
            }
            int leastCnt = least.Refs.Count;
            int[] leastBuf = ArrayPool<int>.Shared.Rent(leastCnt);
            least.Refs.CopyTo(leastBuf, 0);
            if (isRow)
            {
                for (int ri = 0; ri < leastCnt; ri++)
                {
                    var other = primary[leastBuf[ri]];
                    if (other == null) continue;
                    if (other.Refs.Count > entry.Refs.Count ||
                        (other.Refs.Count == entry.Refs.Count && other.Key > entry.Key))
                        if (other.Refs.IsSupersetOf(entry.Refs))
                            SparseMatrix.Delete(primary, secondary, other.Key);
                }
            }
            else
            {
                bool deleted = false;
                for (int ci = 0; ci < leastCnt && !deleted; ci++)
                {
                    var other = primary[leastBuf[ci]];
                    if (other == null) continue;
                    if (other.Refs.Count > entry.Refs.Count ||
                        (other.Refs.Count == entry.Refs.Count && other.Key > entry.Key))
                        if (other.Refs.IsSupersetOf(entry.Refs))
                        {
                            SparseMatrix.Delete(primary, secondary, entry.Key);
                            deleted = true;
                        }
                }
            }
            ArrayPool<int>.Shared.Return(leastBuf);
        }
        ArrayPool<int>.Shared.Return(keyBuf);
        return initCount - primary.Count;
    }
    internal static bool Visit(SparseMatrix A, SparseEntry entry, HashSet<int> visitedSelf, HashSet<int> visitedOther,
        SparseDict selfDict, SparseDict otherDict)
    {
        if (!visitedSelf.Add(entry.Key)) return false;
        if (visitedSelf.Count == selfDict.Count) return true;
        foreach (int r in entry.Refs)
            if (Visit(A, otherDict[r]!, visitedOther, visitedSelf, otherDict, selfDict)) return true;
        return false;
    }
}
