using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
namespace Espresso;
using static BitVectorOps;
[Flags]
public enum CubeFlags : byte
{
    None     = 0,
    Prime    = 0x01,
    NonEssen = 0x02,
    Active   = 0x04,
    Redund   = 0x08,
    Covered  = 0x10,
    RelEssen = 0x20,
}
public readonly struct BitVector
{
    private readonly uint[] _data;
    private readonly int    _dataOffset;
    private readonly int    _wordCount;
    public bool IsNull => _data is null;
    public static BitVector Null => default;
    public int Words => _wordCount;
    internal uint[] RawData => _data;
    public BitVector(uint[] data, int offset, int words)
    {
        _data = data;
        _dataOffset = offset;
        _wordCount = words;
    }
    public Span<uint> AsSpan() => _data.AsSpan(_dataOffset, _wordCount);
    public ref uint Meta => ref _data[_dataOffset - 1];
    public void CopyWithMetaTo(uint[] destArray, int destOffset, int stride) =>
        Array.Copy(_data, _dataOffset - 1, destArray, destOffset, stride);
    public static bool Overlaps(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b) => a.Overlaps(b);
}
public static class BitVectorOps
{
    public const int  Bpi      = 32;
    public const int  LogBpi   = 5;
    public const uint Disjoint = 0x55555555u;
    public static int WhichWord(int e) => e >> LogBpi;
    public static int WhichBit(int e) => e & (Bpi - 1);
    public static int WordCount(int size) => (size + Bpi - 1) >> LogBpi;
    public static void AddFlag(BitVector set, CubeFlags flag) { if (!set.IsNull) set.Meta |= (uint)flag; }
    public static void ClearFlag(BitVector set, CubeFlags flag) { if (!set.IsNull) set.Meta &= ~(uint)flag; }
    public static bool HasFlag(BitVector set, CubeFlags flag) =>
        !set.IsNull && (set.Meta & (uint)flag) != 0;
    public static CubeFlags GetFlags(BitVector set) =>
        set.IsNull ? CubeFlags.None : (CubeFlags)(byte)set.Meta;
    public static int GetSortKey(BitVector set) =>
        set.IsNull ? 0 : (int)(set.Meta >> 16);
    public static void SetSortKey(BitVector set, int size) { if (!set.IsNull) set.Meta = (set.Meta & 0xFFFFu) | ((uint)size << 16); }
    public static bool Contains(ReadOnlySpan<uint> set, int e) =>
        (set[e >> LogBpi] & (1u << (e & (Bpi - 1)))) != 0;
    public static void Remove(Span<uint> set, int e) =>
        set[e >> LogBpi] &= ~(1u << (e & (Bpi - 1)));
    public static void Insert(Span<uint> set, int e) =>
        set[e >> LogBpi] |= 1u << (e & (Bpi - 1));
    public static int CountOnes(uint v) => BitOperations.PopCount(v);
    public static void Copy(Span<uint> r, ReadOnlySpan<uint> a) => a.CopyTo(r);
    public static void Fill(Span<uint> r, int size)
    {
        r.Fill(~0u);
        r[^1] = ~0u >> (r.Length * Bpi - size);
    }
    public static void And(Span<uint> r, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        for (int i = 0; i < r.Length; i++) r[i] = a[i] & b[i];
    }
    public static void Or(Span<uint> r, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        for (int i = 0; i < r.Length; i++) r[i] = a[i] | b[i];
    }
    public static void AndNot(Span<uint> r, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        for (int i = 0; i < r.Length; i++) r[i] = a[i] & ~b[i];
    }
    public static void Xor(Span<uint> r, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        for (int i = 0; i < r.Length; i++) r[i] = a[i] ^ b[i];
    }
    public static void MergeWithMask(Span<uint> r, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, ReadOnlySpan<uint> mask)
    {
        for (int i = 0; i < r.Length; i++) r[i] = (a[i] & mask[i]) | (b[i] & ~mask[i]);
    }
    public static int BitIndex(uint a) =>
        a == 0 ? -1 : BitOperations.TrailingZeroCount(a);
    public static int PopCount(ReadOnlySpan<uint> a)
    {
        int sum = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != 0) sum += CountOnes(a[i]);
        return sum;
    }
    public static int IntersectionCount(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int sum = 0;
        for (int i = 0; i < a.Length; i++) { uint val = a[i] & b[i]; if (val != 0) sum += CountOnes(val); }
        return sum;
    }
    public static bool IsEmpty(ReadOnlySpan<uint> a) => !a.ContainsAnyExcept(0u);
    public static bool AreEqual(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b) => a.SequenceEqual(b);
    public static bool AreDisjoint(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        for (int i = 0; i < a.Length; i++)
            if ((a[i] & b[i]) != 0) return false;
        return true;
    }
    public static bool IsSubsetOf(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        for (int i = 0; i < a.Length; i++)
            if ((a[i] & ~b[i]) != 0) return false;
        return true;
    }
    public static BitVector Create(int size)
    {
        int words = WordCount(size);
        return new BitVector(new uint[words + 1], 1, words);
    }
    public static BitVector Clone(BitVector r)
    {
        var p = new BitVector(new uint[r.Words + 1], 1, r.Words);
        r.AsSpan().CopyTo(p.AsSpan());
        return p;
    }
    public static int CompareDescending(BitVector a, BitVector b)
    {
        uint sa = (uint)GetSortKey(a), sb = (uint)GetSortKey(b);
        if (sa > sb) return -1;
        if (sa < sb) return 1;
        var spa = a.AsSpan(); var spb = b.AsSpan();
        for (int i = spa.Length - 1; i >= 0; i--)
        {
            if (spa[i] > spb[i]) return -1;
            if (spa[i] < spb[i]) return 1;
        }
        return 0;
    }
    public static int CompareAscending(BitVector a, BitVector b) => -CompareDescending(a, b);
}
public readonly struct SplitStack
{
    private readonly uint[] _data;
    private readonly int _words;
    private readonly int _stride;
    public SplitStack(int size, int maxDepth)
    {
        _words = BitVectorOps.WordCount(size);
        _stride = _words + 1;
        _data = new uint[maxDepth * 2 * _stride];
    }
    public void GetPair(int depth, out BitVector cl, out BitVector cr)
    {
        int offset = depth * 2 * _stride;
        cl = new BitVector(_data, offset + 1, _words);
        cr = new BitVector(_data, offset + _stride + 1, _words);
    }
}
public class BitVectorFamily
{
    public uint[] Data;
    public int    Words;
    public int    Stride;
    public int    SfSize;
    public int    Capacity;
    public int    Count;
    public int    ActiveCount;
    public BitVectorFamily() { Data = Array.Empty<uint>(); }
    public BitVector GetSet(int index) => new(Data, index * Stride + 1, Words);
    public Span<uint> GetSpan(int index) => Data.AsSpan(index * Stride + 1, Words);
    public void EnsureCapacity(int capacity) { Array.Resize(ref Data, capacity * Stride); }
    public static BitVectorFamily Create(int num, int size)
    {
        int words = WordCount(size);
        return new BitVectorFamily
        {
            SfSize = size, Words = words, Stride = words + 1,
            Capacity = num, Data = new uint[num * (words + 1)],
            Count = 0, ActiveCount = 0,
        };
    }
    public static BitVectorFamily Clone(BitVectorFamily a) =>
        CopyInto(Create(a.Count, a.SfSize), a);
    public static BitVectorFamily CopyInto(BitVectorFamily r, BitVectorFamily a)
    {
        r.SfSize = a.SfSize; r.Words = a.Words; r.Stride = a.Stride;
        r.Count = a.Count; r.ActiveCount = a.ActiveCount;
        Array.Copy(a.Data, 0, r.Data, 0, a.Stride * a.Count);
        return r;
    }
    public static BitVectorFamily Join(BitVectorFamily a, BitVectorFamily b)
    {
        if (a.SfSize != b.SfSize) throw new InvalidOperationException("sf_join: sf_size mismatch");
        int asize = a.Count * a.Stride;
        int bsize = b.Count * b.Stride;
        var r = Create(a.Count + b.Count, a.SfSize);
        r.Count       = a.Count + b.Count;
        r.ActiveCount = a.ActiveCount + b.ActiveCount;
        Array.Copy(a.Data, 0, r.Data, 0,     asize);
        Array.Copy(b.Data, 0, r.Data, asize, bsize);
        return r;
    }
    public static BitVectorFamily Append(BitVectorFamily a, BitVectorFamily b)
    {
        if (a.SfSize != b.SfSize) throw new InvalidOperationException("sf_append: sf_size mismatch");
        int asize = a.Count * a.Stride;
        int bsize = b.Count * b.Stride;
        int newCap = a.Count + b.Count;
        if (newCap > a.Capacity)
        {
            a.Capacity = newCap;
            a.EnsureCapacity(a.Capacity);
        }
        Array.Copy(b.Data, 0, a.Data, asize, bsize);
        a.Count       += b.Count;
        a.ActiveCount += b.ActiveCount;
        return a;
    }
    public static BitVectorFamily Add(BitVectorFamily a, BitVector s)
    {
        if (a.Count >= a.Capacity)
        {
            a.Capacity = a.Capacity + a.Capacity / 2 + 1;
            a.EnsureCapacity(a.Capacity);
        }
        s.CopyWithMetaTo(a.Data, a.Count * a.Stride, a.Stride);
        a.Count++;
        return a;
    }
    public static void SetAllFlags(BitVectorFamily a, CubeFlags flag)
    {
        uint mask = (uint)flag;
        for (int si = 0; si < a.Count; si++)
            a.Data[si * a.Stride] |= mask;
    }
    public static void ClearAllFlags(BitVectorFamily a, CubeFlags flag)
    {
        uint mask = (uint)flag;
        for (int si = 0; si < a.Count; si++)
            a.Data[si * a.Stride] &= ~mask;
    }
    public static void ActivateAll(BitVectorFamily a)
    {
        SetAllFlags(a, CubeFlags.Active);
        a.ActiveCount = a.Count;
    }
    public static BitVectorFamily CompactInactive(BitVectorFamily a)
    {
        int destIdx = 0, originalCount = a.Count, stride = a.Stride;
        for (int si = 0; si < originalCount; si++)
        {
            if ((a.Data[si * stride] & (uint)CubeFlags.Active) != 0)
            {
                if (destIdx != si) Array.Copy(a.Data, si * stride, a.Data, destIdx * stride, stride);
                destIdx++;
            }
            else a.Count--;
        }
        return a;
    }
    public static BitVectorFamily FromSortedArray(BitVector[] A1, int totcnt, int size)
    {
        var R = Create(totcnt, size);
        R.Count = totcnt;
        int stride = R.Stride;
        for (int i = 0; i < totcnt; i++)
            A1[i].CopyWithMetaTo(R.Data, i * stride, stride);
        return R;
    }
    public static BitVectorFamily FromSortedOrder(BitVectorFamily src, int[] order, int count)
    {
        var R = Create(count, src.SfSize);
        R.Count = count;
        int stride = R.Stride;
        for (int i = 0; i < count; i++)
            Array.Copy(src.Data, order[i] * stride, R.Data, i * stride, stride);
        return R;
    }
    public static BitVector[] ToSortedArray(BitVectorFamily A, Comparison<BitVector> compare)
    {
        var A1 = ArrayPool<BitVector>.Shared.Rent(Math.Max(A.Count, 1));
        for (int i = 0; i < A.Count; i++)
        {
            var p = A.GetSet(i);
            SetSortKey(p, PopCount(p.AsSpan()));
            A1[i] = p;
        }
        A1.AsSpan(0, A.Count).Sort(compare);
        return A1;
    }
    public static void ReturnSortedArray(BitVector[] A1) =>
        ArrayPool<BitVector>.Shared.Return(A1, clearArray: false);

    internal static int RmEqual(BitVector[] A1, int len, Comparison<BitVector> compare)
    {
        if (len == 0) return 0;
        int pdest = 0;
        for (int p = 1; p < len; p++)
            if (compare(A1[p], A1[p - 1]) != 0)
                A1[pdest++] = A1[p - 1];
        A1[pdest++] = A1[len - 1];
        return pdest;
    }

}
