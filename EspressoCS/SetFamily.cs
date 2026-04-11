namespace EspressoCS;

// ---------------------------------------------------------------------------
// SetFamily — a two-dimensional matrix of packed bit sets.
// Mirrors C's  set_family_t / pset_family.
// All sf_* functions are static methods on this class.
// ---------------------------------------------------------------------------

public class SetFamily
{
    public int    WSize;        // words per set  (SET_SIZE(SfSize))
    public int    SfSize;       // user-declared set size in bits
    public int    Capacity;     // number of sets currently allocated
    public int    Count;        // number of sets currently in use
    public int    ActiveCount;  // number of active sets
    public uint[] Data;         // flat storage: Capacity * WSize uints

    public SetFamily()
    {
        Data = Array.Empty<uint>();
    }

    /// <summary>GETSET(family, index) — returns a PSet slice pointing at set #index.</summary>
    public PSet GetSet(int index) => new PSet(Data, index * WSize);

    // -----------------------------------------------------------------------
    // sf_new / sf_free / sf_cleanup
    // -----------------------------------------------------------------------

    /// <summary>sf_new — allocate a family of num sets, each of size bits.</summary>
    public static SetFamily SfNew(int num, int size)
    {
        var a = new SetFamily();
        a.SfSize       = size;
        a.WSize        = SetOps.SetSize(size);
        a.Capacity     = num;
        a.Data         = new uint[(long)a.Capacity * a.WSize];
        a.Count        = 0;
        a.ActiveCount  = 0;
        return a;
    }

    /// <summary>sf_free — no-op; GC reclaims memory.</summary>
    public static void SfFree(SetFamily a) { }

    /// <summary>sf_cleanup — no-op; GC handles garbage collection.</summary>
    public static void SfCleanup() { }

    // -----------------------------------------------------------------------
    // sf_save / sf_copy
    // -----------------------------------------------------------------------

    /// <summary>sf_save — deep-copy a set family.</summary>
    public static SetFamily SfSave(SetFamily a) =>
        SfCopy(SfNew(a.Count, a.SfSize), a);

    /// <summary>sf_copy — copy A into R (R must already have sufficient capacity).</summary>
    public static SetFamily SfCopy(SetFamily r, SetFamily a)
    {
        r.SfSize      = a.SfSize;
        r.WSize       = a.WSize;
        r.Count       = a.Count;
        r.ActiveCount = a.ActiveCount;
        Array.Copy(a.Data, 0, r.Data, 0, (long)a.WSize * a.Count);
        return r;
    }

    // -----------------------------------------------------------------------
    // sf_join / sf_append
    // -----------------------------------------------------------------------

    /// <summary>sf_join — join A and B into a new set family (both are preserved).</summary>
    public static SetFamily SfJoin(SetFamily a, SetFamily b)
    {
        if (a.SfSize != b.SfSize)
            throw new InvalidOperationException("sf_join: sf_size mismatch");

        long asize = (long)a.Count * a.WSize;
        long bsize = (long)b.Count * b.WSize;

        var r = SfNew(a.Count + b.Count, a.SfSize);
        r.Count       = a.Count + b.Count;
        r.ActiveCount = a.ActiveCount + b.ActiveCount;
        Array.Copy(a.Data, 0, r.Data, 0,      asize);
        Array.Copy(b.Data, 0, r.Data, asize,  bsize);
        return r;
    }

    /// <summary>sf_append — append sets of B to A and dispose B.</summary>
    public static SetFamily SfAppend(SetFamily a, SetFamily b)
    {
        if (a.SfSize != b.SfSize)
            throw new InvalidOperationException("sf_append: sf_size mismatch");

        long asize = (long)a.Count * a.WSize;
        long bsize = (long)b.Count * b.WSize;

        a.Capacity = a.Count + b.Count;
        Array.Resize(ref a.Data, (int)((long)a.Capacity * a.WSize));
        Array.Copy(b.Data, 0, a.Data, asize, bsize);
        a.Count       += b.Count;
        a.ActiveCount += b.ActiveCount;
        SfFree(b);
        return a;
    }

    // -----------------------------------------------------------------------
    // sf_addset / sf_delset
    // -----------------------------------------------------------------------

    /// <summary>sf_addset — add set s to the end of family A, growing if needed.</summary>
    public static SetFamily SfAddSet(SetFamily a, PSet s)
    {
        if (a.Count >= a.Capacity)
        {
            a.Capacity = a.Capacity + a.Capacity / 2 + 1;
            Array.Resize(ref a.Data, (int)((long)a.Capacity * a.WSize));
        }
        var p = a.GetSet(a.Count++);
        SetOps.InlineCopy(p, s);
        return a;
    }

    /// <summary>sf_delset — delete set i by replacing it with the last set.</summary>
    public static void SfDelSet(SetFamily a, int i) =>
        SetOps.SetCopy(a.GetSet(i), a.GetSet(--a.Count));

    // -----------------------------------------------------------------------
    // sf_or / sf_and
    // -----------------------------------------------------------------------

    /// <summary>sf_or — OR of all sets in the family; returns a new standalone set.</summary>
    public static PSet SfOr(SetFamily a)
    {
        var result = SetOps.SetNew(a.SfSize);
        for (int si = 0; si < a.Count; si++)
            SetOps.InlineOr(result, result, a.GetSet(si));
        return result;
    }

    /// <summary>sf_and — AND of all sets in the family; returns a new standalone set.</summary>
    public static PSet SfAnd(SetFamily a)
    {
        var result = SetOps.SetFull(a.SfSize);
        for (int si = 0; si < a.Count; si++)
            SetOps.InlineAnd(result, result, a.GetSet(si));
        return result;
    }

    // -----------------------------------------------------------------------
    // sf_active / sf_inactive
    // -----------------------------------------------------------------------

    /// <summary>sf_active — mark all sets active.</summary>
    public static SetFamily SfActive(SetFamily a)
    {
        for (int si = 0; si < a.Count; si++)
            SetOps.SetFlag(a.GetSet(si), SetOps.Active);
        a.ActiveCount = a.Count;
        return a;
    }

    /// <summary>sf_inactive — compact out all inactive sets in-place.</summary>
    public static SetFamily SfInactive(SetFamily a)
    {
        int destIdx      = 0;
        int originalCount = a.Count;
        for (int si = 0; si < originalCount; si++)
        {
            var p = a.GetSet(si);
            if (SetOps.TestP(p, SetOps.Active))
            {
                if (destIdx != si)
                    SetOps.InlineCopy(a.GetSet(destIdx), p);
                destIdx++;
            }
            else
            {
                a.Count--;
            }
        }
        return a;
    }

    // -----------------------------------------------------------------------
    // sf_print / sf_bm_print
    // -----------------------------------------------------------------------

    /// <summary>sf_print — print each set as an element list.</summary>
    public static void SfPrint(SetFamily a)
    {
        for (int i = 0; i < a.Count; i++)
            Console.WriteLine($"A[{i}] = {SetOps.Ps1(a.GetSet(i))}");
    }

    /// <summary>sf_bm_print — print as a bit-matrix.</summary>
    public static void SfBmPrint(SetFamily a)
    {
        for (int i = 0; i < a.Count; i++)
            Console.WriteLine($"[{i,4}] {SetOps.Pbv1(a.GetSet(i), a.SfSize)}");
    }

    // -----------------------------------------------------------------------
    // sf_write / sf_read / sf_bm_read
    // -----------------------------------------------------------------------

    /// <summary>sf_write — write family to fp in hex (readable by sf_read).</summary>
    public static void SfWrite(TextWriter fp, SetFamily a)
    {
        fp.WriteLine($"{a.Count} {a.SfSize}");
        for (int si = 0; si < a.Count; si++)
            SetOps.SetWrite(fp, a.GetSet(si));
        fp.Flush();
    }

    /// <summary>sf_read — read a family written by sf_write.</summary>
    public static SetFamily SfRead(TextReader fp)
    {
        var header = fp.ReadLine()!.Trim()
            .Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int count  = int.Parse(header[0]);
        int sfSize = int.Parse(header[1]);

        var a = SfNew(count, sfSize);
        a.Count = count;

        // Tokenise all remaining content at once so hex words separated by
        // any whitespace (including the newlines that set_write emits) work.
        var rest   = fp.ReadToEnd();
        var tokens = rest.Split(new char[] { ' ', '\t', '\n', '\r' },
                                StringSplitOptions.RemoveEmptyEntries);
        int ti = 0;
        for (int si = 0; si < a.Count; si++)
        {
            var p  = a.GetSet(si);
            p[0]   = Convert.ToUInt32(tokens[ti++], 16);
            int lp = SetOps.Loop(p);
            for (int j = 1; j <= lp; j++)
                p[j] = Convert.ToUInt32(tokens[ti++], 16);
        }
        return a;
    }

    /// <summary>sf_bm_read — read a family written by sf_bm_print.</summary>
    public static SetFamily SfBmRead(TextReader fp)
    {
        var header = fp.ReadLine()!.Trim()
            .Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int rows = int.Parse(header[0]);
        int cols = int.Parse(header[1]);

        var a = SfNew(rows, cols);
        for (int i = 0; i < rows; i++)
        {
            var pdest = a.GetSet(a.Count++);
            SetOps.SetClear(pdest, a.SfSize);
            string line = fp.ReadLine()
                ?? throw new InvalidOperationException("Error reading set family");
            if (line.Length < cols)
                throw new InvalidOperationException("Error reading set family");
            for (int j = 0; j < cols; j++)
            {
                switch (line[j])
                {
                    case '0': break;
                    case '1': SetOps.SetInsert(pdest, j); break;
                    default:
                        throw new InvalidOperationException("Error reading set family");
                }
            }
        }
        return a;
    }

    // -----------------------------------------------------------------------
    // sf_delc / sf_addcol / sf_delcol
    // -----------------------------------------------------------------------

    /// <summary>sf_delc — delete columns first..last (inclusive) from A.</summary>
    public static SetFamily SfDelc(SetFamily a, int first, int last) =>
        SfDelcol(a, first, last - first + 1);

    /// <summary>
    /// sf_addcol — add n blank columns starting at firstcol.
    /// Fast path when adding at end and there is spare word capacity.
    /// </summary>
    public static SetFamily SfAddcol(SetFamily a, int firstcol, int n)
    {
        if (firstcol == a.SfSize)
        {
            int maxsize = SetOps.Bpi * SetOps.LoopInit(a.SfSize);
            if ((a.SfSize + n) <= maxsize)
            {
                a.SfSize += n;
                return a;
            }
        }
        return SfDelcol(a, firstcol, -n);
    }

    /// <summary>
    /// sf_delcol — add/delete columns.
    /// n &gt; 0: delete n columns starting at firstcol.
    /// n &lt; 0: insert |n| blank columns starting at firstcol.
    /// </summary>
    public static SetFamily SfDelcol(SetFamily a, int firstcol, int n)
    {
        var b = SfNew(a.Count, a.SfSize - n);
        for (int ai = 0; ai < a.Count; ai++)
        {
            var p     = a.GetSet(ai);
            var pdest = b.GetSet(b.Count++);
            SetOps.InlineClear(pdest, b.SfSize);

            for (int i = 0; i < firstcol; i++)
                if (SetOps.IsInSet(p, i))
                    SetOps.SetInsert(pdest, i);

            int startSrc = n > 0 ? firstcol + n : firstcol;
            for (int i = startSrc; i < a.SfSize; i++)
                if (SetOps.IsInSet(p, i))
                    SetOps.SetInsert(pdest, i - n);
        }
        SfFree(a);
        return b;
    }

    // -----------------------------------------------------------------------
    // sf_copy_col
    // -----------------------------------------------------------------------

    /// <summary>sf_copy_col — copy column srccol of src to column dstcol of dst.</summary>
    public static SetFamily SfCopyCol(SetFamily dst, int dstcol,
                                      SetFamily src, int srccol)
    {
        int  wordTest = SetOps.WhichWord(srccol);
        uint bitTest  = 1u << SetOps.WhichBit(srccol);
        int  wordSet  = SetOps.WhichWord(dstcol);
        uint bitSet   = 1u << SetOps.WhichBit(dstcol);

        for (int si = 0; si < src.Count; si++)
        {
            var p = src.GetSet(si);
            if ((p[wordTest] & bitTest) != 0)
                dst.GetSet(si)[wordSet] |= bitSet;
        }
        return dst;
    }

    // -----------------------------------------------------------------------
    // sf_compress / sf_transpose / sf_permute
    // -----------------------------------------------------------------------

    /// <summary>sf_compress — retain only columns selected by c; frees A.</summary>
    public static SetFamily SfCompress(SetFamily a, PSet c)
    {
        int newCols = SetOps.SetOrd(c);
        var b = SfNew(a.Count, newCols);
        for (int i = 0; i < a.Count; i++)
        {
            var p = b.GetSet(b.Count++);
            SetOps.InlineClear(p, b.SfSize);
        }

        int bcol = 0;
        for (int i = 0; i < a.SfSize; i++)
        {
            if (SetOps.IsInSet(c, i))
                SfCopyCol(b, bcol++, a, i);
        }
        SfFree(a);
        return b;
    }

    /// <summary>sf_transpose — transpose the bit matrix; frees A.</summary>
    public static SetFamily SfTranspose(SetFamily a)
    {
        var b = SfNew(a.SfSize, a.Count);
        b.Count = a.SfSize;

        for (int i = 0; i < b.Count; i++)
            SetOps.InlineClear(b.GetSet(i), b.SfSize);

        for (int i = 0; i < a.Count; i++)
        {
            var p = a.GetSet(i);
            for (int j = 0; j < a.SfSize; j++)
                if (SetOps.IsInSet(p, j))
                    SetOps.SetInsert(b.GetSet(j), i);
        }
        SfFree(a);
        return b;
    }

    /// <summary>sf_permute — retain and reorder columns per permute[]; frees A.</summary>
    public static SetFamily SfPermute(SetFamily a, int[] permute, int npermute)
    {
        var b = SfNew(a.Count, npermute);
        b.Count = a.Count;

        for (int bi = 0; bi < b.Count; bi++)
            SetOps.InlineClear(b.GetSet(bi), npermute);

        for (int ai = 0; ai < a.Count; ai++)
        {
            var p     = a.GetSet(ai);
            var pdest = b.GetSet(ai);
            for (int j = 0; j < npermute; j++)
                if (SetOps.IsInSet(p, permute[j]))
                    SetOps.SetInsert(pdest, j);
        }
        SfFree(a);
        return b;
    }

    // -----------------------------------------------------------------------
    // sf_count / sf_count_restricted
    // -----------------------------------------------------------------------

    /// <summary>sf_count — column sum over all sets.</summary>
    public static int[] SfCount(SetFamily a)
    {
        var count = new int[a.SfSize];

        for (int si = 0; si < a.Count; si++)
        {
            var p = a.GetSet(si);
            int i = SetOps.Loop(p);
            while (i > 0)
            {
                uint val = p[i];
                int  b   = --i << SetOps.LogBpi;
                for (; val != 0; b++, val >>= 1)
                    if ((val & 1) != 0)
                        count[b]++;
            }
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // sf_sort / sf_unlist  (needed by contain.c callers)
    // -----------------------------------------------------------------------

    /// <summary>
    /// sf_sort — build a sorted array of PSet references.
    /// Side-effect: stores set_ord into SIZE field of each set (mirrors C sf_sort).
    /// Returns a null-terminated PSet[] where [0..count-1] are sorted sets.
    /// </summary>
    public static PSet[] SfSort(SetFamily A, Comparison<PSet> compare)
    {
        var A1 = new PSet[A.Count + 1];   // +1 for null-sentinel slot
        for (int i = 0; i < A.Count; i++)
        {
            var p = A.GetSet(i);
            SetOps.PutSize(p, SetOps.SetOrd(p));
            A1[i] = p;
        }
        // A1[A.Count] stays PSet.Null (default) — sentinel
        Array.Sort(A1, 0, A.Count, Comparer<PSet>.Create(compare));
        return A1;
    }

    /// <summary>
    /// sf_unlist — copy sets from sorted array A1 into a new SetFamily of size <paramref name="totcnt"/>.
    /// </summary>
    public static SetFamily SfUnlist(PSet[] A1, int totcnt, int size)
    {
        var R = SfNew(totcnt, size);
        R.Count = totcnt;
        for (int i = 0; i < totcnt; i++)
        {
            if (A1[i].IsNull) break;
            SetOps.InlineCopy(R.GetSet(i), A1[i]);
        }
        return R;
    }

    /// <summary>sf_list — return a null-terminated PSet[] of all set references.</summary>
    public static PSet[] SfList(SetFamily F)
    {
        var list = new PSet[F.Count + 1];
        for (int i = 0; i < F.Count; i++)
            list[i] = F.GetSet(i);
        list[F.Count] = PSet.Null;
        return list;
    }

    /// <summary>
    /// sf_count_restricted — column sum restricted to columns in r,
    /// weighted by 1024/(set_ord(p)-1) per row.
    /// </summary>
    public static int[] SfCountRestricted(SetFamily a, PSet r)
    {
        var count = new int[a.SfSize];

        for (int si = 0; si < a.Count; si++)
        {
            var p      = a.GetSet(si);
            int weight = 1024 / (SetOps.SetOrd(p) - 1);
            int i      = SetOps.Loop(p);
            while (i > 0)
            {
                uint val = p[i] & r[i];
                int  b   = --i << SetOps.LogBpi;
                for (; val != 0; b++, val >>= 1)
                    if ((val & 1) != 0)
                        count[b] += weight;
            }
        }
        return count;
    }
}
