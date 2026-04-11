using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace EspressoCS;

// ---------------------------------------------------------------------------
// PSet — a slice into a uint[] array (or a standalone allocated set).
// Mirrors C's  pset = unsigned int*  with an embedded offset so it can
// point into SetFamily.Data without extra heap allocation.
// ---------------------------------------------------------------------------

public struct PSet
{
    internal readonly uint[] _d;
    internal readonly int    _o;

    public bool IsNull => _d is null;
    public static PSet Null => default;

    /// <summary>Allocates a standalone set large enough for <paramref name="size"/> elements.</summary>
    public PSet(int size)
    {
        _d = new uint[SetOps.SetSize(size)];
        _o = 0;
    }

    /// <summary>Creates a slice that points into an existing array at the given word offset.</summary>
    public PSet(uint[] data, int offset)
    {
        _d = data;
        _o = offset;
    }

    public ref uint this[int i] => ref _d[_o + i];

    public static bool operator ==(PSet a, PSet b) => a._d == b._d && a._o == b._o;
    public static bool operator !=(PSet a, PSet b) => !(a == b);
    public override bool Equals(object? obj) => obj is PSet p && this == p;
    public override int GetHashCode() =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(_d), _o);
}

// ---------------------------------------------------------------------------
// SetOps — all set-level operations (BPI=32 only).
// Macros from espresso.h / set.h and functions from set.c / setc.c.
// ---------------------------------------------------------------------------

public static class SetOps
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    public const int  Bpi      = 32;
    public const int  LogBpi   = 5;
    public const uint Disjoint = 0x55555555u;

    // Flags stored in the high bits of word[0]
    public const uint Prime    = 0x8000u;
    public const uint NonEssen = 0x4000u;
    public const uint Active   = 0x2000u;
    public const uint Redund   = 0x1000u;
    public const uint Covered  = 0x0800u;
    public const uint RelEssen = 0x0400u;

    // -----------------------------------------------------------------------
    // Macro translations — hot path, all [AggressiveInlining]
    // -----------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WhichWord(int e) => (e >> LogBpi) + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WhichBit(int e) => e & (Bpi - 1);

    /// <summary>Number of uint words needed to hold a set of <paramref name="size"/> elements.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetSize(int size) => size <= Bpi ? 2 : (WhichWord(size - 1) + 1);

    /// <summary>LOOP(set) — index of the last data word.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Loop(PSet set) => (int)(set[0] & 0x03ffu);

    /// <summary>PUTLOOP(set,i) — write the loop count into word[0].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PutLoop(PSet set, int i) =>
        set[0] = (set[0] & ~0x03ffu) | (uint)i;

    /// <summary>LOOPCOPY(set) — BPI=32: same as Loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LoopCopy(PSet set) => Loop(set);

    /// <summary>SIZE(set) — user-maintained element count stored in word[0] bits 31:16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSize(PSet set) => (int)(set[0] >> 16);

    /// <summary>PUTSIZE(set,size) — write element count into word[0] bits 31:16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PutSize(PSet set, int size) =>
        set[0] = (set[0] & 0xffffu) | ((uint)size << 16);

    /// <summary>NELEM(set) = BPI * LOOP(set).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Nelem(PSet set) => Bpi * Loop(set);

    /// <summary>LOOPINIT(size) — initial loop-count for a set of <paramref name="size"/> elements.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LoopInit(int size) => size <= Bpi ? 1 : WhichWord(size - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetFlag(PSet set, uint flag) => set[0] |= flag;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ResetFlag(PSet set, uint flag) => set[0] &= ~flag;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TestP(PSet set, uint flag) => (set[0] & flag) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInSet(PSet set, int e) =>
        (set[WhichWord(e)] & (1u << WhichBit(e))) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetRemove(PSet set, int e) =>
        set[WhichWord(e)] &= ~(1u << WhichBit(e));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetInsert(PSet set, int e) =>
        set[WhichWord(e)] |= 1u << WhichBit(e);

    /// <summary>count_ones — uses hardware popcount via System.Numerics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountOnes(uint v) => BitOperations.PopCount(v);

    // -----------------------------------------------------------------------
    // Inline set operations (INLINEset_* macros)
    // -----------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineCopy(PSet r, PSet a)
    {
        int i = LoopCopy(a);
        do { r[i] = a[i]; } while (--i >= 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineClear(PSet r, int size)
    {
        int i = LoopInit(size);
        r[0] = (uint)i;
        do { r[i] = 0; } while (--i > 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineFill(PSet r, int size)
    {
        int i = LoopInit(size);
        r[0] = (uint)i;
        r[i] = ~0u >> (i * Bpi - size);
        while (--i > 0)
            r[i] = ~0u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineAnd(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] & b[i]; } while (--i > 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineOr(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] | b[i]; } while (--i > 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineDiff(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] & ~b[i]; } while (--i > 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineXor(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] ^ b[i]; } while (--i > 0);
    }

    /// <summary>INLINEset_ndiff: r = fullset &amp; (a | ~b)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineNdiff(PSet r, PSet a, PSet b, PSet f)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = f[i] & (a[i] | ~b[i]); } while (--i > 0);
    }

    /// <summary>INLINEset_xnor: r = fullset &amp; ~(a ^ b)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineXnor(PSet r, PSet a, PSet b, PSet f)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = f[i] & ~(a[i] ^ b[i]); } while (--i > 0);
    }

    /// <summary>INLINEset_merge: r = (a &amp; mask) | (b &amp; ~mask)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InlineMerge(PSet r, PSet a, PSet b, PSet mask)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = (a[i] & mask[i]) | (b[i] & ~mask[i]); } while (--i > 0);
    }

    // -----------------------------------------------------------------------
    // Functions from set.c
    // -----------------------------------------------------------------------

    /// <summary>bit_index — index of lowest set bit (LSB=0), or -1 if zero.</summary>
    public static int BitIndex(uint a)
    {
        if (a == 0) return -1;
        int i = 0;
        for (; (a & 1) == 0; a >>= 1, i++) ;
        return i;
    }

    /// <summary>set_ord — number of 1-bits in the set.</summary>
    public static int SetOrd(PSet a)
    {
        int sum = 0;
        for (int i = Loop(a); i > 0; i--)
        {
            uint val = a[i];
            if (val != 0) sum += CountOnes(val);
        }
        return sum;
    }

    /// <summary>set_dist — number of elements common to both sets.</summary>
    public static int SetDist(PSet a, PSet b)
    {
        int sum = 0;
        for (int i = Loop(a); i > 0; i--)
        {
            uint val = a[i] & b[i];
            if (val != 0) sum += CountOnes(val);
        }
        return sum;
    }

    /// <summary>set_clear — make r the empty set of size elements.</summary>
    public static PSet SetClear(PSet r, int size)
    {
        int i = LoopInit(size);
        r[0] = (uint)i;
        do { r[i] = 0; } while (--i > 0);
        return r;
    }

    /// <summary>set_fill — make r the universal set of size elements.</summary>
    public static PSet SetFill(PSet r, int size)
    {
        int i = LoopInit(size);
        r[0] = (uint)i;
        r[i] = ~0u;
        r[i] >>= i * Bpi - size;
        while (--i > 0)
            r[i] = ~0u;
        return r;
    }

    /// <summary>set_copy — copy a into r.</summary>
    public static PSet SetCopy(PSet r, PSet a)
    {
        int i = LoopCopy(a);
        do { r[i] = a[i]; } while (--i >= 0);
        return r;
    }

    /// <summary>set_and — r = a &amp; b</summary>
    public static PSet SetAnd(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] & b[i]; } while (--i > 0);
        return r;
    }

    /// <summary>set_or — r = a | b</summary>
    public static PSet SetOr(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] | b[i]; } while (--i > 0);
        return r;
    }

    /// <summary>set_diff — r = a &amp; ~b</summary>
    public static PSet SetDiff(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] & ~b[i]; } while (--i > 0);
        return r;
    }

    /// <summary>set_xor — r = a ^ b</summary>
    public static PSet SetXor(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = a[i] ^ b[i]; } while (--i > 0);
        return r;
    }

    /// <summary>set_merge — r = (a &amp; mask) | (b &amp; ~mask)</summary>
    public static PSet SetMerge(PSet r, PSet a, PSet b, PSet mask)
    {
        int i = Loop(a);
        PutLoop(r, i);
        do { r[i] = (a[i] & mask[i]) | (b[i] & ~mask[i]); } while (--i > 0);
        return r;
    }

    /// <summary>set_andp — r = a &amp; b; returns true if non-empty.</summary>
    public static bool SetAndP(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        uint x = 0;
        PutLoop(r, i);
        do { r[i] = a[i] & b[i]; x |= r[i]; } while (--i > 0);
        return x != 0;
    }

    /// <summary>set_orp — r = a | b; returns true if non-empty.</summary>
    public static bool SetOrP(PSet r, PSet a, PSet b)
    {
        int i = Loop(a);
        uint x = 0;
        PutLoop(r, i);
        do { r[i] = a[i] | b[i]; x |= r[i]; } while (--i > 0);
        return x != 0;
    }

    /// <summary>setp_empty — true if set a has no elements.</summary>
    public static bool SetpEmpty(PSet a)
    {
        int i = Loop(a);
        do { if (a[i] != 0) return false; } while (--i > 0);
        return true;
    }

    /// <summary>setp_full — true if a is the full set of size elements.</summary>
    public static bool SetpFull(PSet a, int size)
    {
        int i = Loop(a);
        uint test = ~0u >> (i * Bpi - size);
        if (a[i] != test) return false;
        while (--i > 0)
            if (a[i] != ~0u) return false;
        return true;
    }

    /// <summary>setp_equal — true if a == b element-wise.</summary>
    public static bool SetpEqual(PSet a, PSet b)
    {
        int i = Loop(a);
        do { if (a[i] != b[i]) return false; } while (--i > 0);
        return true;
    }

    /// <summary>setp_disjoint — true if a and b share no elements.</summary>
    public static bool SetpDisjoint(PSet a, PSet b)
    {
        int i = Loop(a);
        do { if ((a[i] & b[i]) != 0) return false; } while (--i > 0);
        return true;
    }

    /// <summary>setp_implies — true if a ⊆ b (b contains a).</summary>
    public static bool SetpImplies(PSet a, PSet b)
    {
        int i = Loop(a);
        do { if ((a[i] & ~b[i]) != 0) return false; } while (--i > 0);
        return true;
    }

    // -----------------------------------------------------------------------
    // Allocation wrappers (set_new / set_full / set_save / set_free)
    // -----------------------------------------------------------------------

    /// <summary>set_new — allocate and clear a set of size elements.</summary>
    public static PSet SetNew(int size)
    {
        var p = new PSet(size);
        return SetClear(p, size);
    }

    /// <summary>set_full — allocate and fill a set of size elements.</summary>
    public static PSet SetFull(int size)
    {
        var p = new PSet(size);
        return SetFill(p, size);
    }

    /// <summary>set_save — allocate and copy r into a new standalone set.</summary>
    public static PSet SetSave(PSet r)
    {
        var p = new PSet(Nelem(r));
        return SetCopy(p, r);
    }

    /// <summary>set_free — no-op; GC handles memory.</summary>
    public static void SetFree(PSet r) { }

    // -----------------------------------------------------------------------
    // set_adjcnt — adjust column count array by weight for each set bit
    // -----------------------------------------------------------------------

    public static void SetAdjcnt(PSet a, int[] count, int weight)
    {
        int i = Loop(a);
        while (i > 0)
        {
            uint val = a[i];
            int b = --i << LogBpi;          // base element index for this word
            for (; val != 0; b++, val >>= 1)
                if ((val & 1) != 0)
                    count[b] += weight;
        }
    }

    // -----------------------------------------------------------------------
    // set_write — write a set in hex (readable by sf_read / set_read)
    // -----------------------------------------------------------------------

    public static void SetWrite(TextWriter fp, PSet a)
    {
        int n = Loop(a);
        for (int j = 0; j <= n; j++)
        {
            fp.Write($"{a[j]:x} ");
            if ((j + 1) % 8 == 0 && j != n)
                fp.Write("\n\t");
        }
        fp.WriteLine();
    }

    // -----------------------------------------------------------------------
    // Comparators from setc.c (those that do NOT require cube global)
    // -----------------------------------------------------------------------

    /// <summary>descend — sort descending by SIZE, then lexicographic.</summary>
    public static int Descend(PSet a, PSet b)
    {
        uint sa = (uint)GetSize(a), sb = (uint)GetSize(b);
        if (sa > sb) return -1;
        else if (sa < sb) return 1;
        else
        {
            int i = Loop(a);
            do
            {
                if (a[i] > b[i]) return -1;
                else if (a[i] < b[i]) return 1;
            } while (--i > 0);
        }
        return 0;
    }

    /// <summary>ascend — sort ascending by SIZE, then lexicographic.</summary>
    public static int Ascend(PSet a, PSet b)
    {
        uint sa = (uint)GetSize(a), sb = (uint)GetSize(b);
        if (sa > sb) return 1;
        else if (sa < sb) return -1;
        else
        {
            int i = Loop(a);
            do
            {
                if (a[i] > b[i]) return 1;
                else if (a[i] < b[i]) return -1;
            } while (--i > 0);
        }
        return 0;
    }

    /// <summary>lex_order — lexicographic comparison (descending by word value).</summary>
    public static int LexOrder(PSet a, PSet b)
    {
        int i = Loop(a);
        do
        {
            if (a[i] > b[i]) return -1;
            else if (a[i] < b[i]) return 1;
        } while (--i > 0);
        return 0;
    }

    /// <summary>d1_order — delegates to SetC.D1Order which reads cube.temp[0].</summary>
    public static int D1Order(PSet a, PSet b) => SetC.D1Order(a, b);

    /// <summary>desc1 — descending sort without pointer indirection; NULL-safe.</summary>
    public static int Desc1(PSet a, PSet b)
    {
        if (a.IsNull) return b.IsNull ? 0 : 1;
        if (b.IsNull) return -1;
        uint sa = (uint)GetSize(a), sb = (uint)GetSize(b);
        if (sa > sb) return -1;
        else if (sa < sb) return 1;
        else
        {
            int i = Loop(a);
            do
            {
                if (a[i] > b[i]) return -1;
                else if (a[i] < b[i]) return 1;
            } while (--i > 0);
        }
        return 0;
    }

    // -----------------------------------------------------------------------
    // Debug / print helpers (ps1, pbv1)
    // -----------------------------------------------------------------------

    /// <summary>ps1 — format set as "[e0,e1,...]".</summary>
    public static string Ps1(PSet a)
    {
        var sb = new StringBuilder();
        int n = Nelem(a);
        sb.Append('[');
        bool first = true;
        for (int i = 0; i < n; i++)
        {
            if (IsInSet(a, i))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(i);
                if (sb.Length > 105)    // leave room like C's largest_string-15 guard
                {
                    sb.Append("...");
                    break;
                }
            }
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>pbv1 — format n bits as a binary string "0110...".</summary>
    public static string Pbv1(PSet s, int n)
    {
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++)
            sb.Append(IsInSet(s, i) ? '1' : '0');
        return sb.ToString();
    }
}
