using System.Buffers;
using System.Runtime.CompilerServices;
namespace Espresso;

using static BitVectorOps;
public struct CoverCost
{
    public int Cubes, In, Out, Mv, Total, Primes;
}
public readonly record struct VariableAnalysis(
    int[] PartZeros, int[] VarZeros, bool[] IsUnate,
    int VarsActive, int VarsUnate, int Best);
public readonly record struct SplitSummary(
    int VarsActive, int VarsUnate, int Best, int BestVarZeros);
public sealed record CubeData
{
    public static CubeData Empty { get; } = new()
    {
        Size = 0,
        NumVars = 0,
        NumBinaryVars = 0,
        FirstPart = Array.Empty<int>(),
        LastPart = Array.Empty<int>(),
        PartSize = Array.Empty<int>(),
        VarMask = Array.Empty<BitVector>(),
        Temp = Array.Empty<BitVector>(),
        FullSet = BitVector.Null,
        EmptySet = BitVector.Null,
        InMask = 0,
        InWord = 0,
    };
    public required int Size { get; init; }
    public required int NumVars { get; init; }
    public required int NumBinaryVars { get; init; }
    public required int[] FirstPart { get; init; }
    public required int[] LastPart { get; init; }
    public required int[] PartSize { get; init; }
    public required BitVector[] VarMask { get; init; }
    public required BitVector[] Temp { get; init; }
    public required BitVector FullSet { get; init; }
    public required BitVector EmptySet { get; init; }
    public required uint InMask { get; init; }
    public required int InWord { get; init; }
    public List<uint[]> CofPool { get; } = new();
    public int NumMvVars => NumVars - NumBinaryVars;
    public BitVector RentCof()
    {
        int words = BitVectorOps.WordCount(Size);
        var pool = CofPool;
        uint[] arr;
        if (pool.Count > 0) { arr = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); }
        else arr = new uint[words + 1];
        arr[0] = 0;
        return new BitVector(arr, 1, words);
    }
    public BitVector RentCofEmpty()
    {
        int words = BitVectorOps.WordCount(Size);
        var pool = CofPool;
        uint[] arr;
        if (pool.Count > 0) { arr = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); Array.Clear(arr, 0, words + 1); }
        else arr = new uint[words + 1];
        return new BitVector(arr, 1, words);
    }
    public void ReturnCof(BitVector b)
    {
        if (b.RawData != null) CofPool.Add(b.RawData);
    }
    public BitVector RentCofCopy(BitVector src)
    {
        int words = BitVectorOps.WordCount(Size);
        var pool = CofPool;
        uint[] arr;
        if (pool.Count > 0) { arr = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); }
        else arr = new uint[words + 1];
        arr[0] = 0;
        src.AsSpan().CopyTo(arr.AsSpan(1, words));
        return new BitVector(arr, 1, words);
    }
    public int Output => NumMvVars > 0 ? NumVars - 1 : -1;
    public int FirstWordOf(int var) => BitVectorOps.WhichWord(FirstPart[var]);
    public int LastWordOf(int var) => BitVectorOps.WhichWord(LastPart[var]);
    public bool IsSparse(int var) => var >= NumBinaryVars;
}
public static class CubeFactory
{
    internal static CubeData Build(int numVars, int numBinaryVars, ReadOnlySpan<int> partSize, int cubeTemp = 10)
    {
        int size = 0;
        var firstPart = new int[numVars];
        var lastPart = new int[numVars];
        var ps = partSize.ToArray();
        for (int var = 0; var < numVars; var++)
        {
            if (var < numBinaryVars) ps[var] = 2;
            firstPart[var] = size;
            size += Math.Abs(ps[var]);
            lastPart[var] = size - 1;
        }
        var varMask = new BitVector[numVars];
        var binaryMask = BitVectorOps.Create(size);
        Span<uint> bmSpan = binaryMask.AsSpan();
        for (int var = 0; var < numVars; var++)
        {
            BitVector p = varMask[var] = BitVectorOps.Create(size);
            Span<uint> sp = p.AsSpan();
            for (int i = firstPart[var]; i <= lastPart[var]; i++) BitVectorOps.Insert(sp, i);
            if (var < numBinaryVars) BitVectorOps.Or(bmSpan, bmSpan, sp);
        }
        int inWord; uint inMask;
        if (numBinaryVars == 0) { inWord = -1; inMask = 0; }
        else
        {
            inWord = BitVectorOps.WhichWord(lastPart[numBinaryVars - 1]);
            inMask = bmSpan[inWord] & BitVectorOps.Disjoint;
        }
        var temp = new BitVector[cubeTemp];
        for (int i = 0; i < cubeTemp; i++) temp[i] = BitVectorOps.Create(size);
        var fullSet = BitVectorOps.Create(size);
        BitVectorOps.Fill(fullSet.AsSpan(), size);
        return new CubeData
        {
            Size = size,
            NumVars = numVars,
            NumBinaryVars = numBinaryVars,
            FirstPart = firstPart,
            LastPart = lastPart,
            PartSize = ps,
            VarMask = varMask,
            Temp = temp,
            FullSet = fullSet,
            EmptySet = BitVectorOps.Create(size),
            InWord = inWord,
            InMask = inMask,
        };
    }
}
public readonly struct CubeList
{
    public readonly BitVector Cof;
    public readonly BitVector[] Cubes;
    public readonly int Count;
    public readonly bool Rented;
    public readonly bool OwnsCof;
    public readonly CubeData Owner;
    public BitVector this[int i] => Cubes[i];
    public Span<uint> GetSpan(int i) => Cubes[i].AsSpan();
    public ReadOnlySpan<uint> CofSpan => Cof.AsSpan();
    public CubeList(BitVector cof, BitVector[] cubes, int count, bool rented = false, bool ownsCof = false, CubeData? owner = null)
    { Cof = cof; Cubes = cubes; Count = count; Rented = rented; OwnsCof = ownsCof; Owner = owner!; }
    public void ReturnCubes()
    {
        if (Rented && Cubes != null) ArrayPool<BitVector>.Shared.Return(Cubes, clearArray: false);
        if (OwnsCof && Owner != null) Owner.ReturnCof(Cof);
    }
}
public static class Cofactor
{
    public static CubeList ComputeCofactor(CubeData cube, CubeList T, ReadOnlySpan<uint> c)
    {
        Span<uint> temp = cube.Temp[0].AsSpan();
        AndNot(temp, cube.FullSet.AsSpan(), c);
        BitVector newCube = cube.RentCof();
        Or(newCube.AsSpan(), T.CofSpan, temp);
        var cubes = ArrayPool<BitVector>.Shared.Rent(Math.Max(T.Count, 1));
        int count = 0;
        for (int i = 0; i < T.Count; i++)
        {
            ReadOnlySpan<uint> sp = T.GetSpan(i);
            if (!sp.Overlaps(c) && CubeDistance.AreDistance0(cube, sp, c)) cubes[count++] = T[i];
        }
        return new CubeList(newCube, cubes, count, rented: true, ownsCof: true, owner: cube);
    }
    public static CubeList SingleVariableCofactor(CubeData cube, CubeList T, ReadOnlySpan<uint> c, int var)
    {
        Span<uint> mask = cube.Temp[1].AsSpan();
        AndNot(mask, cube.FullSet.AsSpan(), c);
        BitVector newCube = cube.RentCof();
        Or(newCube.AsSpan(), T.CofSpan, mask);
        int first = cube.FirstWordOf(var), last = cube.LastWordOf(var);
        And(mask, cube.VarMask[var].AsSpan(), c);
        ReadOnlySpan<uint> sm = mask;
        var cubes = ArrayPool<BitVector>.Shared.Rent(Math.Max(T.Count, 1));
        int count = 0;
        for (int i = 0; i < T.Count; i++)
        {
            ReadOnlySpan<uint> sp = T.GetSpan(i);
            if (!sp.Overlaps(c))
                for (int w = first; w <= last; w++)
                    if ((sp[w] & sm[w]) != 0) { cubes[count++] = T[i]; break; }
        }
        return new CubeList(newCube, cubes, count, rented: true, ownsCof: true, owner: cube);
    }
    public static SplitSummary AnalyzeSplitVariable(CubeData cube, CubeList T)
    {
        Span<int> buffer = cube.Size <= 256 ? stackalloc int[cube.Size] : new int[cube.Size];
        buffer.Clear();
        FillPartZeros(T, cube, buffer);
        return Summarize(cube, buffer, null, null);
    }
    public static VariableAnalysis AnalyzeAllVariables(CubeData cube, CubeList T)
    {
        int[] partZeros = ArrayPool<int>.Shared.Rent(cube.Size);
        partZeros.AsSpan(0, cube.Size).Clear();
        FillPartZeros(T, cube, partZeros.AsSpan(0, cube.Size));
        int[] varZeros = ArrayPool<int>.Shared.Rent(cube.NumVars);
        varZeros.AsSpan(0, cube.NumVars).Clear();
        bool[] isUnate = ArrayPool<bool>.Shared.Rent(cube.NumVars);
        isUnate.AsSpan(0, cube.NumVars).Clear();
        var summary = Summarize(cube, partZeros.AsSpan(0, cube.Size), varZeros, isUnate);
        return new VariableAnalysis(partZeros, varZeros, isUnate, summary.VarsActive, summary.VarsUnate, summary.Best);
    }
    public static void ReturnAnalysis(VariableAnalysis a)
    {
        ArrayPool<int>.Shared.Return(a.PartZeros, clearArray: false);
        ArrayPool<int>.Shared.Return(a.VarZeros, clearArray: false);
        ArrayPool<bool>.Shared.Return(a.IsUnate, clearArray: false);
    }
    private static void FillPartZeros(CubeList T, CubeData cube, Span<int> partZeros)
    {
        ReadOnlySpan<uint> sc = T.CofSpan, sf = cube.FullSet.AsSpan();
        for (int t1 = 0; t1 < T.Count; t1++)
        {
            ReadOnlySpan<uint> sp = T.GetSpan(t1);
            for (int i = sp.Length - 1; i >= 0; i--)
            {
                uint val = sf[i] & ~(sp[i] | sc[i]);
                if (val == 0) continue;
                int cb = i << LogBpi;
                while (val != 0)
                {
                    partZeros[cb + System.Numerics.BitOperations.TrailingZeroCount(val)]++;
                    val &= val - 1;
                }
            }
        }
    }
    private static SplitSummary Summarize(CubeData cube, ReadOnlySpan<int> partZeros, int[]? varZeros, bool[]? isUnate)
    {
        int best = -1, mostActive = 0, mostZero = 0, mostBalanced = 32000, varsUnate = 0, varsActive = 0;
        for (int var = 0; var < cube.NumVars; var++)
        {
            int active, maxActive, zeroCount;
            if (var < cube.NumBinaryVars)
            {
                int ii = partZeros[var * 2], lastbit = partZeros[var * 2 + 1];
                active = (ii > 0 ? 1 : 0) + (lastbit > 0 ? 1 : 0);
                zeroCount = ii + lastbit;
                maxActive = Math.Max(ii, lastbit);
            }
            else
            {
                active = maxActive = zeroCount = 0;
                int lastbit = cube.LastPart[var];
                for (int i = cube.FirstPart[var]; i <= lastbit; i++)
                {
                    zeroCount += partZeros[i];
                    active += partZeros[i] > 0 ? 1 : 0;
                    if (active > maxActive) maxActive = active;
                }
            }
            if (varZeros != null) varZeros[var] = zeroCount;
            if (active > mostActive) { best = var; mostActive = active; mostZero = zeroCount; mostBalanced = maxActive; }
            else if (active == mostActive)
            {
                if (zeroCount > mostZero) { best = var; mostZero = zeroCount; mostBalanced = maxActive; }
                else if (zeroCount == mostZero && maxActive < mostBalanced) { best = var; mostBalanced = maxActive; }
            }
            if (isUnate != null) isUnate[var] = (active == 1);
            varsActive += active > 0 ? 1 : 0;
            varsUnate += active == 1 ? 1 : 0;
        }
        return new SplitSummary(varsActive, varsUnate, best, mostZero);
    }
    public static void BuildSplitCubes(CubeData cube, CubeList T, int best, Span<uint> cleft, Span<uint> cright)
    {
        int lastbit = cube.LastPart[best];
        ReadOnlySpan<uint> cof = T.CofSpan, full = cube.FullSet.AsSpan(), vmask = cube.VarMask[best].AsSpan();
        AndNot(cleft, full, vmask);
        AndNot(cright, full, vmask);
        int halfbit = 0;
        for (int i = cube.FirstPart[best]; i <= lastbit; i++)
            if (!Contains(cof, i)) halfbit++;
        halfbit /= 2;
        int j = cube.FirstPart[best];
        for (; halfbit > 0; j++)
            if (!Contains(cof, j)) { halfbit--; Insert(cleft, j); }
        for (; j <= lastbit; j++)
            if (!Contains(cof, j)) Insert(cright, j);
    }
    public static CubeList BuildCubeList(CubeData cube, BitVectorFamily f0)
    {
        int total = f0.Count;
        var cubes = ArrayPool<BitVector>.Shared.Rent(Math.Max(total, 1));
        int idx = 0;
        for (int si = 0; si < f0.Count; si++) cubes[idx++] = f0.GetSet(si);
        return new CubeList(cube.RentCofEmpty(), cubes, idx, rented: true, ownsCof: true, owner: cube);
    }
    public static CubeList BuildCubeList(CubeData cube, BitVectorFamily f0, BitVectorFamily f1)
    {
        int total = f0.Count + f1.Count;
        var cubes = ArrayPool<BitVector>.Shared.Rent(Math.Max(total, 1));
        int idx = 0;
        for (int si = 0; si < f0.Count; si++) cubes[idx++] = f0.GetSet(si);
        for (int si = 0; si < f1.Count; si++) cubes[idx++] = f1.GetSet(si);
        return new CubeList(cube.RentCofEmpty(), cubes, idx, rented: true, ownsCof: true, owner: cube);
    }
    public static CubeList BuildCubeList(CubeData cube, BitVectorFamily f0, BitVectorFamily f1, BitVectorFamily f2)
    {
        int total = f0.Count + f1.Count + f2.Count;
        var cubes = ArrayPool<BitVector>.Shared.Rent(Math.Max(total, 1));
        int idx = 0;
        for (int si = 0; si < f0.Count; si++) cubes[idx++] = f0.GetSet(si);
        for (int si = 0; si < f1.Count; si++) cubes[idx++] = f1.GetSet(si);
        for (int si = 0; si < f2.Count; si++) cubes[idx++] = f2.GetSet(si);
        return new CubeList(cube.RentCofEmpty(), cubes, idx, rented: true, ownsCof: true, owner: cube);
    }
}
public static class CubeDistance
{
    public static bool IsFullCoverage(CubeData cube, ReadOnlySpan<uint> p, ReadOnlySpan<uint> cof)
    {
        var sf = cube.FullSet.AsSpan();
        for (int i = p.Length - 1; i >= 0; i--)
            if ((p[i] | cof[i]) != sf[i]) return false;
        return true;
    }
    private static int CountBinaryDisjoint(CubeData cube, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int last = cube.InWord;
        if (last == -1) return 0;
        uint x = a[last] & b[last];
        x = ~(x | (x >> 1)) & cube.InMask;
        int dist = x != 0 ? CountOnes(x) : 0;
        for (int w = 0; w < last; w++)
        {
            x = a[w] & b[w];
            x = ~(x | (x >> 1)) & Disjoint;
            if (x != 0) dist += CountOnes(x);
        }
        return dist;
    }
    private static bool IsMvVariableDisjoint(CubeData cube, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, int var)
    {
        ReadOnlySpan<uint> sm = cube.VarMask[var].AsSpan();
        int last = cube.LastWordOf(var);
        for (int w = cube.FirstWordOf(var); w <= last; w++)
            if ((a[w] & b[w] & sm[w]) != 0) return false;
        return true;
    }
    public static bool AreDistance0(CubeData cube, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        if (CountBinaryDisjoint(cube, a, b) != 0) return false;
        for (int var = cube.NumBinaryVars; var < cube.NumVars; var++)
            if (IsMvVariableDisjoint(cube, a, b, var)) return false;
        return true;
    }
    public static int DistanceCapped(CubeData cube, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int dist = 0, last = cube.InWord;
        if (last != -1)
        {
            uint x = a[last] & b[last];
            x = ~(x | (x >> 1)) & cube.InMask;
            if (x != 0) { dist = CountOnes(x); if (dist > 1) return 2; }
            for (int w = 0; w < last; w++)
            {
                x = a[w] & b[w];
                x = ~(x | (x >> 1)) & Disjoint;
                if (x != 0 && (dist == 1 || (dist += CountOnes(x)) > 1)) return 2;
            }
        }
        for (int var = cube.NumBinaryVars; var < cube.NumVars; var++)
            if (IsMvVariableDisjoint(cube, a, b, var) && ++dist > 1) return 2;
        return dist;
    }
    public static int Distance(CubeData cube, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int dist = CountBinaryDisjoint(cube, a, b);
        for (int var = cube.NumBinaryVars; var < cube.NumVars; var++)
            if (IsMvVariableDisjoint(cube, a, b, var)) dist++;
        return dist;
    }
    public static void FindDisjointParts(CubeData cube, Span<uint> xlower, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int last = cube.InWord;
        if (last != -1)
        {
            uint x = a[last] & b[last];
            x = ~(x | (x >> 1)) & cube.InMask;
            if (x != 0) xlower[last] |= (x | (x << 1)) & a[last];
            for (int w = 0; w < last; w++)
            {
                x = a[w] & b[w];
                x = ~(x | (x >> 1)) & Disjoint;
                if (x != 0) xlower[w] |= (x | (x << 1)) & a[w];
            }
        }
        for (int var = cube.NumBinaryVars; var < cube.NumVars; var++)
            if (IsMvVariableDisjoint(cube, a, b, var))
            {
                ReadOnlySpan<uint> sm = cube.VarMask[var].AsSpan();
                for (int w = cube.FirstWordOf(var); w <= cube.LastWordOf(var); w++) xlower[w] |= a[w] & sm[w];
            }
    }
    public static void Consensus(CubeData cube, Span<uint> r, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        r.Clear();
        int last = cube.InWord;
        if (last != -1)
        {
            uint x = a[last] & b[last];
            r[last] = x;
            x = ~(x | (x >> 1)) & cube.InMask;
            if (x != 0) r[last] |= (x | (x << 1)) & (a[last] | b[last]);
            for (int w = 0; w < last; w++)
            {
                x = a[w] & b[w];
                r[w] = x;
                x = ~(x | (x >> 1)) & Disjoint;
                if (x != 0) r[w] |= (x | (x << 1)) & (a[w] | b[w]);
            }
        }
        for (int var = cube.NumBinaryVars; var < cube.NumVars; var++)
        {
            ReadOnlySpan<uint> sm = cube.VarMask[var].AsSpan();
            int mvLast = cube.LastWordOf(var);
            if (IsMvVariableDisjoint(cube, a, b, var))
                for (int w = cube.FirstWordOf(var); w <= mvLast; w++) r[w] |= sm[w] & (a[w] | b[w]);
            else
                for (int w = cube.FirstWordOf(var); w <= mvLast; w++) r[w] |= a[w] & b[w] & sm[w];
        }
    }
    public static int Distance1Order(CubeData cube, BitVector a, BitVector b)
    {
        ReadOnlySpan<uint> sa = a.AsSpan(), sb = b.AsSpan(), sc = cube.Temp[0].AsSpan();
        for (int i = sa.Length - 1; i >= 0; i--)
        {
            uint x1 = sa[i] | sc[i], x2 = sb[i] | sc[i];
            if (x1 > x2) return -1;
            if (x1 < x2) return 1;
        }
        return 0;
    }
}
public static class CoverManipulation
{
    public static void CalculateCost(CubeData cube, BitVectorFamily F, out CoverCost cost)
    {
        cost = default;
        CubeList T = Cofactor.BuildCubeList(cube, F);
        var analysis = Cofactor.AnalyzeAllVariables(cube, T);
        T.ReturnCubes();
        cost.Cubes = F.Count;
        for (int var = 0; var < cube.NumBinaryVars; var++) cost.In += analysis.VarZeros[var];
        for (int var = cube.NumBinaryVars; var < cube.NumVars - 1; var++)
            cost.Mv += cube.IsSparse(var)
                ? F.Count * cube.PartSize[var] - analysis.VarZeros[var]
                : analysis.VarZeros[var];
        if (cube.NumBinaryVars != cube.NumVars)
            cost.Out = F.Count * cube.PartSize[cube.NumVars - 1] - analysis.VarZeros[cube.NumVars - 1];
        Cofactor.ReturnAnalysis(analysis);
        for (int si = 0; si < F.Count; si++)
            if (BitVectorOps.HasFlag(F.GetSet(si), CubeFlags.Prime)) cost.Primes++;
        cost.Total = cost.In + cost.Out + cost.Mv;
    }
    public static BitVectorFamily ExpandMultiValued(CubeData cube, BitVectorFamily B, int start) =>
        UnravelRange(cube, B, start, cube.NumVars - 1);
    public static BitVectorFamily SortByCoverage(CubeData cube, BitVectorFamily F, Comparison<BitVector> compare)
    {
        int n = cube.Size;
        // --- inlined ColumnCounts ---
        int[] count;
        {
            int ccWords = F.Words;
            count = ArrayPool<int>.Shared.Rent(F.SfSize);
            Array.Clear(count, 0, F.SfSize);
            for (int si = 0; si < F.Count; si++)
            {
                var cp = F.GetSpan(si);
                for (int ci = 0; ci < ccWords; ci++)
                {
                    uint val = cp[ci];
                    if (val != 0)
                    {
                        int b = ci << LogBpi;
                        while (val != 0)
                        {
                            count[b + System.Numerics.BitOperations.TrailingZeroCount(val)]++;
                            val &= val - 1;
                        }
                    }
                }
            }
        }
        // --- end inlined ColumnCounts ---
        int fWords = F.Words;
        for (int si = 0; si < F.Count; si++)
        {
            ReadOnlySpan<uint> sp = F.GetSpan(si);
            int cnt = 0;
            for (int ci = 0; ci < fWords; ci++)
            {
                uint val = sp[ci];
                int baseBit = ci << LogBpi;
                while (val != 0)
                {
                    cnt += count[baseBit + System.Numerics.BitOperations.TrailingZeroCount(val)];
                    val &= val - 1;
                }
            }
            SetSortKey(F.GetSet(si), cnt);
        }
        ArrayPool<int>.Shared.Return(count, clearArray: false);
        int[] order = ArrayPool<int>.Shared.Rent(F.Count);
        for (int i = 0; i < F.Count; i++) order[i] = i;
        order.AsSpan(0, F.Count).Sort((a, b) => compare(F.GetSet(a), F.GetSet(b)));
        var result = BitVectorFamily.FromSortedOrder(F, order, F.Count);
        ArrayPool<int>.Shared.Return(order, clearArray: false);
        return result;
    }
    public static int PartitionCubeList(CubeData cube, CubeList T, out CubeList A, out CubeList B)
    {
        int n = T.Count;
        bool[] covered = ArrayPool<bool>.Shared.Rent(n);
        covered.AsSpan(0, n).Clear();
        BitVector seed = cube.RentCof();
        T[0].AsSpan().CopyTo(seed.AsSpan());
        covered[0] = true;
        int count = 1;
        Span<uint> seedSpan = seed.AsSpan();
        ReadOnlySpan<uint> cofSpan = T.CofSpan;
        bool change;
        do
        {
            change = false;
            for (int i = 0; i < n; i++)
            {
                if (covered[i]) continue;
                ReadOnlySpan<uint> sp = T.GetSpan(i);
                // Inline HaveCommonActive
                {
                    int hcLast = cube.InWord;
                    if (hcLast != -1)
                    {
                        uint x = sp[hcLast] | cofSpan[hcLast], y = seedSpan[hcLast] | cofSpan[hcLast];
                        if ((~(x & (x >> 1)) & ~(y & (y >> 1)) & cube.InMask) != 0) goto haveCommon;
                        for (int w = 0; w < hcLast; w++)
                        {
                            x = sp[w] | cofSpan[w]; y = seedSpan[w] | cofSpan[w];
                            if ((~(x & (x >> 1)) & ~(y & (y >> 1)) & Disjoint) != 0) goto haveCommon;
                        }
                    }
                    for (int var2 = cube.NumBinaryVars; var2 < cube.NumVars; var2++)
                    {
                        ReadOnlySpan<uint> sm = cube.VarMask[var2].AsSpan();
                        int mvLast = cube.LastWordOf(var2);
                        for (int w = cube.FirstWordOf(var2); w <= mvLast; w++)
                            if ((sm[w] & ~sp[w] & ~cofSpan[w]) != 0)
                            {
                                for (int w2 = cube.FirstWordOf(var2); w2 <= mvLast; w2++)
                                    if ((sm[w2] & ~seedSpan[w2] & ~cofSpan[w2]) != 0) goto haveCommon;
                                break;
                            }
                    }
                    continue;
                }
                haveCommon:
                And(seedSpan, seedSpan, sp);
                covered[i] = true;
                change = true;
                count++;
            }
        } while (change);
        if (count != n)
        {
            var aCubes = ArrayPool<BitVector>.Shared.Rent(Math.Max(count, 1));
            var bCubes = ArrayPool<BitVector>.Shared.Rent(Math.Max(n - count, 1));
            int ai = 0, bi = 0;
            for (int i = 0; i < n; i++)
            {
                if (covered[i]) aCubes[ai++] = T[i];
                else bCubes[bi++] = T[i];
            }
            BitVector cofA = cube.RentCof();
            BitVector cofB = cube.RentCof();
            T.Cof.AsSpan().CopyTo(cofA.AsSpan());
            T.Cof.AsSpan().CopyTo(cofB.AsSpan());
            A = new CubeList(cofA, aCubes, ai, rented: true, ownsCof: true, owner: cube);
            B = new CubeList(cofB, bCubes, bi, rented: true, ownsCof: true, owner: cube);
        }
        else { A = new(BitVector.Null, [], 0); B = new(BitVector.Null, [], 0); }
        cube.ReturnCof(seed);
        ArrayPool<bool>.Shared.Return(covered);
        return T.Count - count;
    }
    private static BitVectorFamily UnravelRange(CubeData cube, BitVectorFamily B, int start, int end)
    {
        Span<uint> sbSpan = cube.Temp![1].AsSpan();
        Copy(sbSpan, cube.EmptySet.AsSpan());
        for (int var = 0; var < start; var++) Or(sbSpan, sbSpan, cube.VarMask![var].AsSpan());
        for (int var = end + 1; var < cube.NumVars; var++) Or(sbSpan, sbSpan, cube.VarMask![var].AsSpan());
        int totalSize = 0;
        for (int si = 0; si < B.Count; si++)
        {
            ReadOnlySpan<uint> sp = B.GetSpan(si);
            int expansion = 1;
            for (int var = start; var <= end; var++)
            {
                int size = IntersectionCount(sp, cube.VarMask![var].AsSpan());
                if (size >= 2)
                {
                    expansion *= size;
                    if (expansion > 1000000) throw new InvalidOperationException("unreasonable expansion in unravel");
                }
            }
            totalSize += expansion;
        }
        if (totalSize == B.Count) return B;  // No expansion needed
        var B1 = BitVectorFamily.Create(totalSize, cube.Size);
        for (int si = 0; si < B.Count; si++)
        {
            ReadOnlySpan<uint> c = B.GetSpan(si);
            Span<uint> baseSpan = cube.Temp![0].AsSpan();
            int expansion = 1, place, skip, size;
            Copy(baseSpan, sbSpan);
            for (int var = start; var <= end; var++)
            {
                ReadOnlySpan<uint> vm = cube.VarMask![var].AsSpan();
                if ((size = IntersectionCount(c, vm)) < 2) Or(baseSpan, baseSpan, vm);
                else expansion *= size;
            }
            And(baseSpan, c, baseSpan);
            int offset = B1.Count;
            B1.Count += expansion;
            for (int pi = offset; pi < B1.Count; pi++) Copy(B1.GetSpan(pi), baseSpan);
            place = expansion;
            for (int var = start; var <= end; var++)
            {
                ReadOnlySpan<uint> vm = cube.VarMask![var].AsSpan();
                if ((size = IntersectionCount(c, vm)) <= 1) continue;
                skip = place;
                place /= size;
                int n = 0;
                for (int i = cube.FirstPart![var]; i <= cube.LastPart![var]; i++)
                {
                    if (!Contains(c, i)) continue;
                    for (int j = n; j < expansion; j += skip)
                        for (int k = 0; k < place; k++) Insert(B1.GetSpan(j + k + offset), i);
                    n += place;
                }
            }
        }
        return B1;
    }
}