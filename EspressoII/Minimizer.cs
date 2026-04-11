using System.Buffers;
namespace Espresso;
using static BitVectorOps;
using static BitVectorFamily;
using static CubeDistance;
public static class EspressoMinimizer
{
    public static BitVectorFamily Minimize(CubeData cube, BitVectorFamily F, BitVectorFamily D1, BitVectorFamily R)
    {
        MemoCache.Key minKey = default;
        bool minCacheActive = MemoCache.Enabled;
        if (minCacheActive)
        {
            minKey = MemoCache.BuildMinimizeKey(cube, F, D1, R);
            if (MemoCache.TryGetFamily(minKey, cube.Size, out var cached)) return cached;
        }
        BitVectorFamily result = MinimizeUncached(cube, F, D1, R);
        if (minCacheActive) MemoCache.PutFamily(minKey, result);
        return result;
    }

    private static BitVectorFamily MinimizeUncached(CubeData cube, BitVectorFamily F, BitVectorFamily D1, BitVectorFamily R)
    {
        var stack = new SplitStack(cube.Size, cube.Size);
        bool unwrapOnset = cube.PartSize[cube.NumVars - 1] > 1;

        while (true)
        {
            BitVectorFamily Fsave = Clone(F);
            BitVectorFamily D = Clone(D1);

            if (unwrapOnset)
            {
                CoverManipulation.CalculateCost(cube, F, out CoverCost initialCost);
                bool worthUnwrapping =
                    initialCost.Out != initialCost.Cubes * cube.PartSize[cube.NumVars - 1] &&
                    initialCost.Out < 5000;
                if (worthUnwrapping) F = RemoveContained(cube, F);
            }

            ClearAllFlags(F, CubeFlags.Prime);
            F = Expander.ExpandCover(cube, F, R, 0);
            F = Irredundant.FindIrredundant(cube, F, D, stack);

            BitVectorFamily E = FindEssentials(cube, ref F, D, stack);
            D = Join(D, E);

            CoverManipulation.CalculateCost(cube, F, out CoverCost cost);
            bool useSortReduce = true;
            CoverCost bestCost;
            do
            {
                do
                {
                    bestCost = cost;
                    F = ReduceCover(cube, F, D, stack, ref useSortReduce);
                    F = Expander.ExpandCover(cube, F, R, 0);
                    F = Irredundant.FindIrredundant(cube, F, D, stack);
                    CoverManipulation.CalculateCost(cube, F, out cost);
                } while (cost.Cubes < bestCost.Cubes);
                bestCost = cost;
                F = LastGasp(cube, F, D, R, stack);
                CoverManipulation.CalculateCost(cube, F, out cost);
            } while (cost.Cubes < bestCost.Cubes ||
                     (cost.Cubes == bestCost.Cubes && cost.Total < bestCost.Total));

            F = Append(F, E);
            F = MakeSparse(cube, F, D1, R, stack);

            if (Fsave.Count >= F.Count) return F;

            // Retry without unwrapping when unwrap hurt us.
            F = Fsave;
            unwrapOnset = false;
        }
    }

    // Remove cubes contained in others after expanding the output variable.
    private static BitVectorFamily RemoveContained(CubeData cube, BitVectorFamily F)
    {
        BitVectorFamily expanded = CoverManipulation.ExpandMultiValued(cube, F, cube.NumVars - 1);
        BitVector[] sorted = ToSortedArray(expanded, CompareDescending);
        int len = RmEqual(sorted, expanded.Count, CompareDescending);

        int dest = 0, checkLimit = 0, lastSize = -1;
        for (int i = 0; i < len; i++)
        {
            BitVector a = sorted[i];
            int aKey = GetSortKey(a);
            if (aKey != lastSize) { lastSize = aKey; checkLimit = dest; }

            bool contained = false;
            for (int j = 0; j < checkLimit; j++)
            {
                if (GetSortKey(sorted[j]) < aKey) continue;
                if (IsSubsetOf(a.AsSpan(), sorted[j].AsSpan())) { contained = true; break; }
            }
            if (!contained) sorted[dest++] = a;
        }
        BitVectorFamily result = FromSortedArray(sorted, dest, expanded.SfSize);
        BitVectorFamily.ReturnSortedArray(sorted);
        return result;
    }

    // Find essential primes: each prime whose consensus-with-neighbors doesn't cover it is essential.
    // Deactivates essentials in F (compacted on exit) and returns them in E.
    private static BitVectorFamily FindEssentials(CubeData cube, ref BitVectorFamily F, BitVectorFamily D, SplitStack stack)
    {
        ActivateAll(F);
        BitVectorFamily E = Create(Math.Max(10, F.Count / 4), cube.Size);
        BitVectorFamily FD = Join(F, D);
        BitVectorFamily consensusR = Create(FD.Count * 2, cube.Size);

        // Scratch BitVectors reused across the outer loop.
        BitVector consensusTmp = Create(cube.Size);
        BitVector dist0Tmp = Create(cube.Size);
        Span<uint> sConsensus = consensusTmp.AsSpan();
        Span<uint> sDist0 = dist0Tmp.AsSpan();

        for (int fi = 0; fi < F.Count; fi++)
        {
            BitVector fp = F.GetSet(fi);
            if (HasFlag(fp, CubeFlags.NonEssen) || !HasFlag(fp, CubeFlags.RelEssen)) continue;

            ReadOnlySpan<uint> ec = fp.AsSpan();
            consensusR.Count = 0; consensusR.ActiveCount = 0;

            for (int ti = 0; ti < FD.Count; ti++)
            {
                ReadOnlySpan<uint> sp = FD.GetSpan(ti);
                if (sp.Overlaps(ec)) continue;

                int d = DistanceCapped(cube, sp, ec);
                if (d == 0)
                {
                    if (IsSubsetOf(sp, ec)) continue;
                    Span<uint> spDiff = cube.Temp[0].AsSpan();
                    Span<uint> spAnd = cube.Temp[1].AsSpan();
                    AndNot(spDiff, sp, ec);
                    And(spAnd, sp, ec);
                    bool gotOne = false;
                    for (int v = cube.NumBinaryVars; v < cube.NumVars; v++)
                    {
                        ReadOnlySpan<uint> varMask = cube.VarMask[v].AsSpan();
                        if (!AreDisjoint(spDiff, varMask))
                        {
                            MergeWithMask(sDist0, ec, spAnd, varMask);
                            consensusR = Add(consensusR, dist0Tmp);
                            gotOne = true;
                        }
                    }
                    if (!gotOne && cube.NumBinaryVars > 0)
                    {
                        And(sDist0, sp, ec);
                        consensusR = Add(consensusR, dist0Tmp);
                    }
                }
                else if (d == 1)
                {
                    Consensus(cube, sConsensus, sp, ec);
                    consensusR = Add(consensusR, consensusTmp);
                }
            }

            var cubeList = Cofactor.BuildCubeList(cube, consensusR, D);
            bool covered = Irredundant.IsCubeCovered(cube, cubeList, fp, stack);
            cubeList.ReturnCubes();
            if (!covered)
            {
                E = Add(E, fp);
                ClearFlag(fp, CubeFlags.Active);
                F.ActiveCount--;
            }
        }
        F = CompactInactive(F);
        return E;
    }

    // One reduce pass: sort F, then replace each cube with its minimum reduction.
    // Alternates between "by-coverage" and "by-distance-to-largest" sort each call.
    private static BitVectorFamily ReduceCover(CubeData cube, BitVectorFamily F, BitVectorFamily D, SplitStack stack, ref bool useSortReduce)
    {
        if (useSortReduce) F = SortForReduction(cube, F);
        else F = CoverManipulation.SortByCoverage(cube, F, CompareDescending);
        useSortReduce = !useSortReduce;

        CubeList FD = Cofactor.BuildCubeList(cube, F, D);
        for (int ri = 0; ri < F.Count; ri++)
        {
            BitVector rp = F.GetSet(ri);
            BitVector reduced = Reducer.ReduceOneCube(cube, FD, rp, stack);
            Span<uint> sReduced = reduced.AsSpan();

            if (AreEqual(sReduced, F.GetSpan(ri)))
            {
                AddFlag(rp, CubeFlags.Active);
                AddFlag(rp, CubeFlags.Prime);
            }
            else
            {
                Copy(F.GetSpan(ri), sReduced);
                ClearFlag(rp, CubeFlags.Prime);
                if (IsEmpty(sReduced)) ClearFlag(rp, CubeFlags.Active);
                else AddFlag(rp, CubeFlags.Active);
            }
            cube.ReturnCof(reduced);
        }
        FD.ReturnCubes();
        return CompactInactive(F);
    }

    // Sort F so that cubes close to the largest come first.
    private static BitVectorFamily SortForReduction(CubeData cube, BitVectorFamily F)
    {
        if (F.Count == 0) return F;

        int bestSize = -1;
        BitVector largest = BitVector.Null;
        for (int si = 0; si < F.Count; si++)
        {
            int size = PopCount(F.GetSpan(si));
            if (size > bestSize) { largest = F.GetSet(si); bestSize = size; }
        }
        ReadOnlySpan<uint> largestSpan = largest.AsSpan();
        for (int si = 0; si < F.Count; si++)
        {
            ReadOnlySpan<uint> sp = F.GetSpan(si);
            int key = ((cube.NumVars - CubeDistance.Distance(cube, largestSpan, sp)) << 7)
                      + Math.Min(PopCount(sp), 127);
            SetSortKey(F.GetSet(si), key);
        }
        int[] order = ArrayPool<int>.Shared.Rent(F.Count);
        for (int i = 0; i < F.Count; i++) order[i] = i;
        order.AsSpan(0, F.Count).Sort((a, b) => CompareDescending(F.GetSet(a), F.GetSet(b)));
        BitVectorFamily result = BitVectorFamily.FromSortedOrder(F, order, F.Count);
        ArrayPool<int>.Shared.Return(order, clearArray: false);
        return result;
    }

    // Last-gasp reduction: try super-cubes by pairwise combination; add resulting primes back into F.
    private static BitVectorFamily LastGasp(CubeData cube, BitVectorFamily F, BitVectorFamily D, BitVectorFamily R, SplitStack stack)
    {
        BitVectorFamily lgG = Create(F.Count, F.SfSize);
        {
            CubeList FD = Cofactor.BuildCubeList(cube, F, D);
            for (int lgi = 0; lgi < F.Count; lgi++)
            {
                BitVector lgp = F.GetSet(lgi);
                BitVector reduced = Reducer.ReduceOneCube(cube, FD, lgp, stack);
                if (IsEmpty(reduced.AsSpan())) throw new InvalidOperationException("empty reduction in reduce_gasp");
                if (AreEqual(reduced.AsSpan(), F.GetSpan(lgi))) lgG = Add(lgG, lgp);
                else
                {
                    ClearFlag(reduced, CubeFlags.Prime);
                    lgG = Add(lgG, reduced);
                }
                cube.ReturnCof(reduced);
            }
            FD.ReturnCubes();
        }

        BitVectorFamily lgG1 = Create(10, F.SfSize);
        // Scratch pair (RAISE, temp) sharing a single backing buffer.
        BitVector lgRAISE, lgTemp;
        {
            int words = WordCount(cube.Size);
            int stride = words + 1;
            var pairData = new uint[2 * stride];
            lgRAISE = new BitVector(pairData, 1, words);
            lgTemp = new BitVector(pairData, stride + 1, words);
        }
        Span<uint> sRAISE = lgRAISE.AsSpan(), sTemp = lgTemp.AsSpan();

        for (int c1 = 0; c1 < lgG.Count; c1++)
        {
            ActivateAll(R);
            ActivateAll(lgG);
            for (int c2 = 0; c2 < lgG.Count; c2++)
            {
                BitVector c2p = lgG.GetSet(c2);
                if (c1 == c2 || HasFlag(c2p, CubeFlags.Prime))
                {
                    lgG.ActiveCount--;
                    ClearFlag(c2p, CubeFlags.Active);
                }
            }
            Copy(sRAISE, lgG.GetSpan(c1));

            BitVector lgFREESET = cube.Temp[2];
            Span<uint> sFREESET = lgFREESET.AsSpan();
            AndNot(sFREESET, cube.FullSet.AsSpan(), sRAISE);
            Expander.DetermineEssentialParts(cube, R, lgG, lgRAISE, lgFREESET);

            // Xraise: union of active offsets AND freeset, then add to RAISE.
            Span<uint> xraise = cube.Temp[0].AsSpan();
            Copy(xraise, cube.EmptySet.AsSpan());
            for (int ri = 0; ri < R.Count; ri++)
                if (HasFlag(R.GetSet(ri), CubeFlags.Active))
                    Or(xraise, xraise, R.GetSpan(ri));
            AndNot(xraise, sFREESET, xraise);
            Or(sRAISE, sRAISE, xraise);
            AndNot(sFREESET, sFREESET, xraise);

            int slotOffset = c1 * F.Stride;
            uint[]? savedSlot = null;
            CubeList fdSwapped = default;
            bool fSwapped = false;

            for (int c2 = 0; c2 < lgG.Count; c2++)
            {
                BitVector c2p = lgG.GetSet(c2);
                if (!HasFlag(c2p, CubeFlags.Active)) continue;
                if (!IsSubsetOf(lgG.GetSpan(c2), sRAISE) &&
                    !GaspOptimizer.IsFeasiblyCovered(cube, R, c2p, lgRAISE)) continue;

                if (!fSwapped)
                {
                    savedSlot = ArrayPool<uint>.Shared.Rent(F.Stride);
                    Array.Copy(F.Data, slotOffset, savedSlot, 0, F.Stride);
                    Array.Copy(lgG.Data, c1 * lgG.Stride, F.Data, slotOffset, F.Stride);
                    fdSwapped = Cofactor.BuildCubeList(cube, F, D);
                    fSwapped = true;
                }
                BitVector essential = Reducer.ReduceOneCube(cube, fdSwapped, F.GetSet(c2), stack);
                if (GaspOptimizer.IsFeasiblyCovered(cube, R, essential, lgRAISE))
                {
                    Or(sTemp, sRAISE, essential.AsSpan());
                    ClearFlag(lgTemp, CubeFlags.Prime);
                    lgG1 = Add(lgG1, lgTemp);
                }
                cube.ReturnCof(essential);
            }
            if (fSwapped)
            {
                Array.Copy(savedSlot!, 0, F.Data, slotOffset, F.Stride);
                ArrayPool<uint>.Shared.Return(savedSlot!, clearArray: false);
                fdSwapped.ReturnCubes();
            }
        }

        // RemoveDuplicates
        {
            BitVector[] sorted = ToSortedArray(lgG1, CompareDescending);
            lgG1 = FromSortedArray(sorted, RmEqual(sorted, lgG1.Count, CompareDescending), lgG1.SfSize);
            BitVectorFamily.ReturnSortedArray(sorted);
        }
        lgG1 = Expander.ExpandCover(cube, lgG1, R, 0);
        if (lgG1.Count != 0) F = Irredundant.FindIrredundant(cube, Append(F, lgG1), D, stack);
        return F;
    }

    // Sparsify the output: drop inputs that aren't needed to cover each output.
    private static BitVectorFamily MakeSparse(CubeData cube, BitVectorFamily F, BitVectorFamily D1, BitVectorFamily R, SplitStack stack)
    {
        MemoCache.Key key = default;
        bool cacheActive = MemoCache.Enabled;
        if (cacheActive)
        {
            key = MemoCache.BuildFamiliesKey(MemoCache.TagMakeSparse, cube, F, D1, R, 0);
            if (MemoCache.TryGetFamily(key, cube.Size, out var cached)) return cached;
        }
        var result = MakeSparseImpl(cube, F, D1, R, stack);
        if (cacheActive) MemoCache.PutFamily(key, result);
        return result;
    }

    private static BitVectorFamily MakeSparseImpl(CubeData cube, BitVectorFamily F, BitVectorFamily D1, BitVectorFamily R, SplitStack stack)
    {
        CoverManipulation.CalculateCost(cube, F, out CoverCost bestCost);
        int[] fCubeIdxBuf = ArrayPool<int>.Shared.Rent(Math.Max(256, F.Count));
        while (true)
        {
            Span<int> fCubeIdx = fCubeIdxBuf;
            BitVectorFamily msF1 = Create(F.Count, cube.Size);
            BitVectorFamily msD1 = Create(D1.Count, cube.Size);

            for (int var = 0; var < cube.NumVars; var++)
            {
                if (!cube.IsSparse(var)) continue;
                ReadOnlySpan<uint> sVarMask = cube.VarMask[var].AsSpan();

                for (int ii = cube.FirstPart[var]; ii <= cube.LastPart[var]; ii++)
                {
                    msF1.Count = 0;
                    for (int fi = 0; fi < F.Count; fi++)
                    {
                        Span<uint> sp = F.GetSpan(fi);
                        if (!Contains(sp, ii)) continue;
                        fCubeIdx[msF1.Count] = fi;
                        Span<uint> sp1 = msF1.GetSpan(msF1.Count++);
                        AndNot(sp1, sp, sVarMask);
                        Insert(sp1, ii);
                    }
                    msD1.Count = 0;
                    for (int di = 0; di < D1.Count; di++)
                    {
                        Span<uint> sp = D1.GetSpan(di);
                        if (!Contains(sp, ii)) continue;
                        Span<uint> sp1 = msD1.GetSpan(msD1.Count++);
                        AndNot(sp1, sp, sVarMask);
                        Insert(sp1, ii);
                    }
                    Irredundant.MarkIrredundant(cube, msF1, msD1, stack);
                    for (int fi = 0; fi < msF1.Count; fi++)
                    {
                        BitVector p1 = msF1.GetSet(fi);
                        if (HasFlag(p1, CubeFlags.Active)) continue;
                        Span<uint> sp = F.GetSpan(fCubeIdx[fi]);
                        if (var == cube.NumVars - 1 || !IsSubsetOf(sVarMask, sp))
                            Remove(sp, ii);
                        ClearFlag(F.GetSet(fCubeIdx[fi]), CubeFlags.Prime);
                    }
                }
            }

            ActivateAll(F);
            for (int var = 0; var < cube.NumVars; var++)
            {
                if (!cube.IsSparse(var)) continue;
                ReadOnlySpan<uint> sVarMask = cube.VarMask[var].AsSpan();
                for (int fi = 0; fi < F.Count; fi++)
                {
                    BitVector msp = F.GetSet(fi);
                    if (HasFlag(msp, CubeFlags.Active) && AreDisjoint(F.GetSpan(fi), sVarMask))
                    {
                        ClearFlag(msp, CubeFlags.Active);
                        F.ActiveCount--;
                    }
                }
            }
            if (F.Count != F.ActiveCount) F = CompactInactive(F);

            CoverManipulation.CalculateCost(cube, F, out CoverCost cost);
            if (cost.Total == bestCost.Total) break;
            bestCost = cost;
            F = Expander.ExpandCover(cube, F, R, 1);
            CoverManipulation.CalculateCost(cube, F, out cost);
            if (cost.Total == bestCost.Total) break;
            bestCost = cost;
        }
        ArrayPool<int>.Shared.Return(fCubeIdxBuf, clearArray: false);
        return F;
    }
}
public static class Expander
{
    public static BitVectorFamily ExpandCover(CubeData cube, BitVectorFamily F, BitVectorFamily R, int nonsparse)
    {
        MemoCache.Key key = default;
        bool cacheActive = MemoCache.Enabled;
        if (cacheActive)
        {
            key = MemoCache.BuildFamiliesKey(MemoCache.TagExpandCover, cube, F, R, null, nonsparse);
            if (MemoCache.TryGetFamily(key, cube.Size, out var cached)) return cached;
        }
        var result = ExpandCoverImpl(cube, F, R, nonsparse);
        if (cacheActive) MemoCache.PutFamily(key, result);
        return result;
    }

    private static BitVectorFamily ExpandCoverImpl(CubeData cube, BitVectorFamily F, BitVectorFamily R, int nonsparse)
    {
        int[] countBuf = ArrayPool<int>.Shared.Rent(cube.Size);
        F = CoverManipulation.SortByCoverage(cube, F, CompareAscending);
        // --- inlined CreateBatch ---
        BitVector[] scratch;
        {
            int cbWords = WordCount(cube.Size);
            int cbStride = cbWords + 1;
            var cbData = new uint[5 * cbStride];
            scratch = new BitVector[5];
            for (int cbi = 0; cbi < 5; cbi++)
                scratch[cbi] = new BitVector(cbData, cbi * cbStride + 1, cbWords);
        }
        // --- end inlined CreateBatch ---
        BitVector RAISE = scratch[0], FREESET = scratch[1], INIT_LOWER = scratch[2],
             SUPER_CUBE = scratch[3], OVEREXPANDED_CUBE = scratch[4];
        Span<uint> sRAISE = RAISE.AsSpan(), sFREESET = FREESET.AsSpan(), sINIT_LOWER = INIT_LOWER.AsSpan(),
             sSUPER_CUBE = SUPER_CUBE.AsSpan(), sOVEREXPANDED_CUBE = OVEREXPANDED_CUBE.AsSpan();
        if (nonsparse != 0)
            for (int var = 0; var < cube.NumVars; var++)
                if (cube.IsSparse(var))
                    Or(sINIT_LOWER, sINIT_LOWER, cube.VarMask![var].AsSpan());
        ClearAllFlags(F, CubeFlags.Covered | CubeFlags.NonEssen);
        var newLowerBuf = Create(Math.Max(F.Count, 16), cube.Size);
        int[] feasIdxBuf = ArrayPool<int>.Shared.Rent(Math.Max(256, F.Count));
        for (int i = 0; i < F.Count; i++)
        {
            BitVector p = F.GetSet(i);
            if (!HasFlag(p, CubeFlags.Prime) && !HasFlag(p, CubeFlags.Covered))
            {
                // --- inlined ExpandOneCube ---
                BitVectorFamily BB = R, CC = F;
                ReadOnlySpan<uint> sFull = cube.FullSet.AsSpan();
                AddFlag(p, CubeFlags.Prime);
                ActivateAll(BB);
                ActivateAll(CC);
                for (int ei = 0; ei < CC.Count; ei++)
                {
                    BitVector sbcp = CC.GetSet(ei);
                    if (HasFlag(sbcp, CubeFlags.Covered) || HasFlag(sbcp, CubeFlags.Prime))
                    {
                        CC.ActiveCount--;
                        ClearFlag(sbcp, CubeFlags.Active);
                    }
                }
                int num_covered = 0;
                ReadOnlySpan<uint> sc = p.AsSpan();
                Copy(sSUPER_CUBE, sc);
                Copy(sRAISE, sc);
                AndNot(sFREESET, sFull, sRAISE);
                if (!IsEmpty(sINIT_LOWER))
                {
                    AndNot(sFREESET, sFREESET, sINIT_LOWER);
                    EliminateLowering(cube, BB, CC, RAISE, FREESET);
                }
                DetermineEssentialParts(cube, BB, CC, RAISE, FREESET);
                Or(sOVEREXPANDED_CUBE, sRAISE, sFREESET);
                if (CC.ActiveCount > 0)
                {
                    ReadOnlySpan<uint> sEmpty = cube.EmptySet.AsSpan();
                    Span<int> feasIdx = feasIdxBuf;
                    int numfeas = 0;
                    for (int ii = 0; ii < CC.Count; ii++)
                        if (HasFlag(CC.GetSet(ii), CubeFlags.Active)) feasIdx[numfeas++] = ii;
                    BitVectorFamily new_lower = numfeas <= newLowerBuf.Capacity ? newLowerBuf : Create(numfeas, cube.Size);
                    if (numfeas <= newLowerBuf.Capacity) new_lower.Count = 0;
                    while (true)
                    {
                        Span<uint> sxraise = cube.Temp![0].AsSpan();
                        Copy(sxraise, sEmpty);
                        for (int j = 0; j < BB.Count; j++)
                            if (HasFlag(BB.GetSet(j), CubeFlags.Active)) Or(sxraise, sxraise, BB.GetSpan(j));
                        AndNot(sxraise, sFREESET, sxraise);
                        Or(sRAISE, sRAISE, sxraise);
                        AndNot(sFREESET, sFREESET, sxraise);
                        int lastfeas = numfeas;
                        numfeas = 0;
                        for (int fi = 0; fi < lastfeas; fi++)
                        {
                            BitVector fp = CC.GetSet(feasIdx[fi]);
                            if (HasFlag(fp, CubeFlags.Active))
                            {
                                ReadOnlySpan<uint> sp = CC.GetSpan(feasIdx[fi]);
                                if (IsSubsetOf(sp, sRAISE))
                                {
                                    num_covered++;
                                    Or(sSUPER_CUBE, sSUPER_CUBE, sp);
                                    CC.ActiveCount--;
                                    ClearFlag(fp, CubeFlags.Active);
                                    AddFlag(fp, CubeFlags.Covered);
                                }
                                else
                                {
                                    int ifcResult;
                                    {
                                        Span<uint> sifcr = cube.Temp![0].AsSpan();
                                        Or(sifcr, RAISE.AsSpan(), fp.AsSpan());
                                        Copy(new_lower.GetSet(numfeas).AsSpan(), cube.EmptySet.AsSpan());
                                        ifcResult = 1;
                                        for (int bi = 0; bi < BB.Count; bi++)
                                        {
                                            if (!HasFlag(BB.GetSet(bi), CubeFlags.Active)) continue;
                                            int dist = DistanceCapped(cube, BB.GetSpan(bi), sifcr);
                                            if (dist > 1) continue;
                                            if (dist == 0) { ifcResult = 0; break; }
                                            CubeDistance.FindDisjointParts(cube, new_lower.GetSet(numfeas).AsSpan(), BB.GetSpan(bi), sifcr);
                                        }
                                    }
                                    if (ifcResult != 0)
                                    {
                                        feasIdx[numfeas] = feasIdx[fi];
                                        numfeas++;
                                    }
                                }
                            }
                        }
                        if (numfeas == 0) break;
                        int bestcount = 0, bestsize = 9999, bestFeasIdx = -1;
                        for (int fi = 0; fi < numfeas; fi++)
                        {
                            int size = IntersectionCount(CC.GetSpan(feasIdx[fi]), sFREESET);
                            int count = 0;
                            for (int fj = 0; fj < numfeas; fj++)
                                if (AreDisjoint(new_lower.GetSpan(fi), CC.GetSpan(feasIdx[fj]))) count++;
                            if (count > bestcount)
                            {
                                bestcount = count;
                                bestFeasIdx = feasIdx[fi];
                                bestsize = size;
                            }
                            else if (count == bestcount && size < bestsize)
                            {
                                bestFeasIdx = feasIdx[fi];
                                bestsize = size;
                            }
                        }
                        Or(sRAISE, sRAISE, CC.GetSpan(bestFeasIdx));
                        AndNot(sFREESET, sFREESET, sRAISE);
                        DetermineEssentialParts(cube, BB, CC, RAISE, FREESET);
                    }
                }
                while (CC.ActiveCount > 0)
                {
                    int ebi = SelectMostFrequent(cube, CC, FREESET, countBuf);
                    Insert(sRAISE, ebi);
                    Remove(sFREESET, ebi);
                    DetermineEssentialParts(cube, BB, CC, RAISE, FREESET);
                }
                while (BB.ActiveCount > 0)
                {
                    Span<uint> mcxraise = cube.Temp![0].AsSpan();
                    ReadOnlySpan<uint> mcEmpty = cube.EmptySet.AsSpan();
                    var B = Create(BB.ActiveCount, cube.Size);
                    for (int mi = 0; mi < BB.Count; mi++)
                    {
                        if (HasFlag(BB.GetSet(mi), CubeFlags.Active))
                        {
                            Span<uint> splower = B.GetSpan(B.Count++);
                            Copy(splower, mcEmpty);
                            CubeDistance.FindDisjointParts(cube, splower, BB.GetSpan(mi), sRAISE);
                        }
                    }
                    int nset = 0;
                    bool useHeuristic = false;
                    for (int mi = 0; mi < B.Count; mi++)
                    {
                        Span<uint> bsp = B.GetSpan(mi);
                        int expansion = 1;
                        for (int v = cube.NumBinaryVars; v < cube.NumVars; v++)
                        {
                            int edist = IntersectionCount(bsp, cube.VarMask![v].AsSpan());
                            if (edist > 1)
                            {
                                expansion *= edist;
                                if (expansion > 500) { useHeuristic = true; break; }
                            }
                        }
                        if (useHeuristic) break;
                        nset += expansion;
                        if (nset > 500) { useHeuristic = true; break; }
                    }
                    if (!useHeuristic)
                    {
                        B = CoverManipulation.ExpandMultiValued(cube, B, cube.NumBinaryVars);
                        // --- inlined SolveFromFamily ---
                        BitVector xlower;
                        {
                            var sfM = new SparseMatrix();
                            for (int _pi = 0; _pi < B.Count; _pi++)
                            {
                                var sfsp = B.GetSpan(_pi);
                                for (int sfi = sfsp.Length - 1; sfi >= 0; sfi--)
                                {
                                    uint sfval = sfsp[sfi];
                                    int sfBase = sfi << BitVectorOps.LogBpi;
                                    while (sfval != 0)
                                    {
                                        SparseMatrix.Insert(sfM, _pi, sfBase + System.Numerics.BitOperations.TrailingZeroCount(sfval));
                                        sfval &= sfval - 1;
                                    }
                                }
                            }
                            var sfCover = MinimumCoverSolver.Solve(sfM);
                            xlower = BitVectorOps.Create(B.SfSize);
                            Span<uint> sfsc = xlower.AsSpan();
                            foreach (int col in sfCover.Refs) BitVectorOps.Insert(sfsc, col);
                        }
                        // --- end inlined SolveFromFamily ---
                        AndNot(mcxraise, sFREESET, xlower.AsSpan());
                        Or(sRAISE, sRAISE, mcxraise);
                        Copy(sFREESET, mcEmpty);
                        BB.ActiveCount = 0;
                    }
                    else
                    {
                        Insert(sRAISE, SelectMostFrequent(cube, null, FREESET, countBuf));
                        AndNot(sFREESET, sFREESET, sRAISE);
                        DetermineEssentialParts(cube, BB, null, RAISE, FREESET);
                    }
                }
                Or(sRAISE, sRAISE, sFREESET);
                // --- end inlined ExpandOneCube ---
                Copy(F.GetSpan(i), sRAISE);
                AddFlag(p, CubeFlags.Prime);
                if (num_covered == 0 && !AreEqual(F.GetSpan(i), sOVEREXPANDED_CUBE))
                    AddFlag(p, CubeFlags.NonEssen);
            }
        }
        F.ActiveCount = 0;
        bool change = false;
        for (int i = 0; i < F.Count; i++)
        {
            BitVector p = F.GetSet(i);
            if (HasFlag(p, CubeFlags.Covered))
            {
                ClearFlag(p, CubeFlags.Active);
                change = true;
            }
            else
            {
                AddFlag(p, CubeFlags.Active);
                F.ActiveCount++;
            }
        }
        if (change) F = BitVectorFamily.CompactInactive(F);
        ArrayPool<int>.Shared.Return(feasIdxBuf, clearArray: false);
        ArrayPool<int>.Shared.Return(countBuf, clearArray: false);
        return F;
    }
    internal static void DetermineEssentialParts(CubeData cube, BitVectorFamily BB, BitVectorFamily? CC, BitVector RAISE, BitVector FREESET)
    {
        Span<uint> sRAISE = RAISE.AsSpan(), sFREESET = FREESET.AsSpan();
        Span<uint> sxlower = cube.Temp![0].AsSpan();
        Copy(sxlower, cube.EmptySet.AsSpan());
        for (int i = 0; i < BB.Count; i++)
        {
            BitVector p = BB.GetSet(i);
            if (!HasFlag(p, CubeFlags.Active)) continue;
            int dist = DistanceCapped(cube, BB.GetSpan(i), sRAISE);
            if (dist > 1) continue;
            if (dist == 0) throw new InvalidOperationException("ON-set and OFF-set are not orthogonal");
            CubeDistance.FindDisjointParts(cube, sxlower, BB.GetSpan(i), sRAISE);
            BB.ActiveCount--;
            ClearFlag(p, CubeFlags.Active);
        }
        if (!IsEmpty(sxlower))
        {
            AndNot(sFREESET, sFREESET, sxlower);
            EliminateLowering(cube, BB, CC, RAISE, FREESET);
        }
    }
    private static void EliminateLowering(CubeData cube, BitVectorFamily BB, BitVectorFamily? CC, BitVector RAISE, BitVector FREESET)
    {
        Span<uint> sRAISE = RAISE.AsSpan(), sFREESET = FREESET.AsSpan();
        Span<uint> sr = cube.Temp![0].AsSpan();
        Or(sr, sRAISE, sFREESET);
        for (int i = 0; i < BB.Count; i++)
        {
            BitVector p = BB.GetSet(i);
            if (!HasFlag(p, CubeFlags.Active)) continue;
            if (!AreDistance0(cube, BB.GetSpan(i), sr))
            {
                BB.ActiveCount--;
                ClearFlag(p, CubeFlags.Active);
            }
        }
        if (CC != null)
            for (int i = 0; i < CC.Count; i++)
            {
                BitVector p = CC.GetSet(i);
                if (!HasFlag(p, CubeFlags.Active)) continue;
                if (!IsSubsetOf(CC.GetSpan(i), sr))
                {
                    CC.ActiveCount--;
                    ClearFlag(p, CubeFlags.Active);
                }
            }
    }
    private static int SelectMostFrequent(CubeData cube, BitVectorFamily? CC, BitVector FREESET, int[] count)
    {
        Span<uint> sFREESET = FREESET.AsSpan();
        Array.Clear(count);
        if (CC != null)
            for (int j = 0; j < CC.Count; j++)
                if (HasFlag(CC.GetSet(j), CubeFlags.Active))
                {
                    ReadOnlySpan<uint> acSpan = CC.GetSpan(j);
                    for (int aci = 0; aci < acSpan.Length; aci++)
                    {
                        uint acVal = acSpan[aci];
                        int acb = aci << LogBpi;
                        while (acVal != 0)
                        {
                            count[acb + System.Numerics.BitOperations.TrailingZeroCount(acVal)] += 1;
                            acVal &= acVal - 1;
                        }
                    }
                }
        int best_count = -1, best_part = -1;
        for (int i = 0; i < cube.Size; i++)
            if (Contains(sFREESET, i) && count[i] > best_count)
            {
                best_part = i;
                best_count = count[i];
            }
        return best_part;
    }
}
public static class Reducer
{
    private const int TRUE = 1, MAYBE = 0;
    public static BitVector ReduceOneCube(CubeData cube, CubeList FD, BitVector p, SplitStack stack)
    {
        ReadOnlySpan<uint> sp = p.AsSpan();
        var cof = Cofactor.ComputeCofactor(cube, FD, sp);
        BitVector cunder = ContainmentCube(cube, cof, stack, 0);
        cof.ReturnCubes();
        And(cunder.AsSpan(), cunder.AsSpan(), sp);
        return cunder;
    }
    private static BitVector ContainmentCube(CubeData cube, CubeList T, SplitStack stack, int depth)
    {
        BitVector r;
        SplitSummary summary;
        int ccscResult;
        // --- inlined ContainmentCubeSpecialCases ---
        {
            summary = default;
            Span<uint> stemp = cube.Temp[1].AsSpan();
            ReadOnlySpan<uint> scof = T.CofSpan, sFull = cube.FullSet.AsSpan();
            r = BitVector.Null;
            ccscResult = MAYBE;
            if (T.Count == 0)
            {
                r = cube.RentCofCopy(cube.FullSet);
                ccscResult = TRUE;
            }
            if (ccscResult == MAYBE)
            {
                for (int t1 = 0; t1 < T.Count; t1++)
                    if (IsFullCoverage(cube, T.GetSpan(t1), scof))
                    {
                        r = cube.RentCofEmpty();
                        ccscResult = TRUE;
                        break;
                    }
            }
            if (ccscResult == MAYBE)
            {
                summary = Cofactor.AnalyzeSplitVariable(cube, T);
                if (summary.VarsUnate == summary.VarsActive || T.Count <= 1)
                {
                    r = cube.RentCofCopy(cube.FullSet);
                    for (int t1 = 0; t1 < T.Count; t1++)
                    {
                        Or(stemp, T.GetSpan(t1), scof);
                        SingleCubeContainment(cube, r, cube.Temp[1]);
                    }
                    ccscResult = TRUE;
                }
            }
            if (ccscResult == MAYBE)
            {
                BitVector ceil = cube.Temp[3];
                Span<uint> sceil = ceil.AsSpan();
                Copy(sceil, T.CofSpan);
                for (int t1 = 0; t1 < T.Count; t1++)
                    Or(sceil, sceil, T.GetSpan(t1));
                if (!AreEqual(sceil, sFull))
                {
                    r = SingleCubeContainment(cube, cube.RentCofCopy(cube.FullSet), ceil);
                    Span<uint> sr = r.AsSpan();
                    if (!AreEqual(sr, sFull))
                    {
                        var cof = Cofactor.ComputeCofactor(cube, T, sceil);
                        var leftArg = ContainmentCube(cube, cof, stack, depth);
                        var rightArg = cube.RentCofCopy(cube.FullSet);
                        var oldR = r;
                        r = MergeContainmentCubes(cube, leftArg, rightArg, ceil, oldR);
                        cube.ReturnCof(oldR);
                        cof.ReturnCubes();
                    }
                    ccscResult = TRUE;
                }
                else if (summary.VarsActive == 1)
                {
                    r = cube.RentCofEmpty();
                    ccscResult = TRUE;
                }
                else if (summary.BestVarZeros < T.Count / 2)
                {
                    if (CoverManipulation.PartitionCubeList(cube, T, out CubeList A, out CubeList B) != 0)
                    {
                        r = ContainmentCube(cube, A, stack, depth);
                        var rB = ContainmentCube(cube, B, stack, depth);
                        And(r.AsSpan(), r.AsSpan(), rB.AsSpan());
                        cube.ReturnCof(rB);
                        A.ReturnCubes(); B.ReturnCubes();
                        ccscResult = TRUE;
                    }
                }
            }
        }
        // --- end inlined ContainmentCubeSpecialCases ---
        if (ccscResult == MAYBE)
        {
            stack.GetPair(depth, out BitVector cl, out BitVector cr);
            Span<uint> scl = cl.AsSpan(), scr = cr.AsSpan();
            int best = summary.Best;
            Cofactor.BuildSplitCubes(cube, T, best, scl, scr);
            var cofL = Cofactor.SingleVariableCofactor(cube, T, scl, best);
            var cofR = Cofactor.SingleVariableCofactor(cube, T, scr, best);
            r = MergeContainmentCubes(cube,
                ContainmentCube(cube, cofL, stack, depth + 1),
                ContainmentCube(cube, cofR, stack, depth + 1),
                cl, cr);
            cofL.ReturnCubes();
            cofR.ReturnCubes();
        }
        return r;
    }
    private static BitVector MergeContainmentCubes(CubeData cube, BitVector left, BitVector right, BitVector cl, BitVector cr)
    {
        Span<uint> sleft = left.AsSpan(), sright = right.AsSpan();
        And(sleft, sleft, cl.AsSpan());
        And(sright, sright, cr.AsSpan());
        Or(sleft, sleft, sright);
        cube.ReturnCof(right);
        return left;
    }
    private static BitVector SingleCubeContainment(CubeData cube, BitVector result, BitVector p)
    {
        Span<uint> stemp = cube.Temp[0].AsSpan(), sresult = result.AsSpan();
        ReadOnlySpan<uint> sp = p.AsSpan();
        // --- inlined SingleActiveVariable ---
        int var;
        {
            int savActive = -1, savDist = 0, savLast = cube.InWord;
            if (savLast != -1)
            {
                uint x = sp[savLast];
                x = ~(x & (x >> 1)) & cube.InMask;
                if (x != 0)
                {
                    savDist = CountOnes(x);
                    if (savDist > 1) { var = -1; goto savDone; }
                    savActive = savLast * (Bpi / 2) + BitIndex(x) / 2;
                }
                for (int w = 0; w < savLast; w++)
                {
                    x = sp[w];
                    x = ~(x & (x >> 1)) & Disjoint;
                    if (x != 0)
                    {
                        savDist += CountOnes(x);
                        if (savDist > 1) { var = -1; goto savDone; }
                        savActive = w * (Bpi / 2) + BitIndex(x) / 2;
                    }
                }
            }
            for (int savVar = cube.NumBinaryVars; savVar < cube.NumVars; savVar++)
            {
                ReadOnlySpan<uint> sm = cube.VarMask[savVar].AsSpan();
                int mvLast = cube.LastWordOf(savVar);
                for (int w = cube.FirstWordOf(savVar); w <= mvLast; w++)
                    if ((sm[w] & ~sp[w]) != 0) { if (++savDist > 1) { var = -1; goto savDone; } savActive = savVar; break; }
            }
            var = savActive;
        savDone:;
        }
        // --- end inlined SingleActiveVariable ---
        if (var >= 0)
        {
            Xor(stemp, sp, cube.VarMask[var].AsSpan());
            And(sresult, sresult, stemp);
        }
        return result;
    }
}
public static class Irredundant
{
    private const int TautFalse = 0, TautTrue = 1, TautMaybe = 2;
    public static BitVectorFamily FindIrredundant(CubeData cube, BitVectorFamily F, BitVectorFamily D, SplitStack stack)
    {
        MemoCache.Key key = default;
        bool cacheActive = MemoCache.Enabled;
        if (cacheActive)
        {
            key = MemoCache.BuildFamiliesKey(MemoCache.TagFindIrredundant, cube, F, D, null, 0);
            if (MemoCache.TryGetFamily(key, cube.Size, out var cached)) return cached;
        }
        MarkIrredundant(cube, F, D, stack);
        var result = CompactInactive(F);
        if (cacheActive) MemoCache.PutFamily(key, result);
        return result;
    }
    public static void MarkIrredundant(CubeData cube, BitVectorFamily F, BitVectorFamily D, SplitStack stack)
    {
        // --- inlined SplitCoverByRedundancy ---
        BitVectorFamily E, Rp;
        {
            for (int si = 0; si < F.Count; si++)
                SetSortKey(F.GetSet(si), si);
            int cap = Math.Max(F.Count / 2, 4);
            E = Create(cap, F.SfSize);
            Rp = Create(cap, F.SfSize);
            var R = Create(cap, F.SfSize);
            CubeList FD = Cofactor.BuildCubeList(cube, F, D);
            for (int si = 0; si < F.Count; si++)
            {
                BitVector p = F.GetSet(si);
                if (IsCubeCovered(cube, FD, p, stack)) R = Add(R, p);
                else E = Add(E, p);
            }
            FD.ReturnCubes();
            CubeList ED = Cofactor.BuildCubeList(cube, E, D);
            for (int si = 0; si < R.Count; si++)
            {
                BitVector p = R.GetSet(si);
                if (!IsCubeCovered(cube, ED, p, stack)) Rp = Add(Rp, p);
            }
            ED.ReturnCubes();
        }
        // --- inlined DeriveCoverTable ---
        SparseMatrix coverTable;
        {
            ClearAllFlags(D, CubeFlags.Redund);
            ClearAllFlags(E, CubeFlags.Redund);
            SetAllFlags(Rp, CubeFlags.Redund);
            var list = Cofactor.BuildCubeList(cube, D, E, Rp);
            coverTable = new SparseMatrix();
            for (int j = 0; j < Rp.Count; j++)
            {
                BitVector p = Rp.GetSet(j);
                var cof = Cofactor.ComputeCofactor(cube, list, Rp.GetSpan(j));
                FunctionalTautology(cube, cof, coverTable, (int)GetSortKey(p), stack, 0);
                cof.ReturnCubes();
                ClearFlag(p, CubeFlags.Redund);
            }
            list.ReturnCubes();
        }
        var cover = MinimumCoverSolver.Solve(coverTable);
        ClearAllFlags(F, CubeFlags.Active | CubeFlags.RelEssen);
        for (int i = 0; i < E.Count; i++)
        {
            BitVector p1 = F.GetSet((int)GetSortKey(E.GetSet(i)));
            AddFlag(p1, CubeFlags.Active);
            AddFlag(p1, CubeFlags.RelEssen);
        }
        foreach (int col in cover.Cols)
            AddFlag(F.GetSet(col), CubeFlags.Active);
    }
    public static bool IsCubeCovered(CubeData cube, CubeList T, BitVector c, SplitStack stack)
    {
        // NOTE: not safely cacheable — Cofactor.ComputeCofactor filters via sp.Overlaps(c) which
        // is a memory-alias check, so the result depends on whether c is a BitVector reference
        // owned by T or just a same-data copy. IsTautology (invoked below) is pure and caches itself.
        var cof = Cofactor.ComputeCofactor(cube, T, c.AsSpan());
        bool result = IsTautology(cube, cof, stack, 0);
        cof.ReturnCubes();
        return result;
    }
    public static bool IsTautology(CubeData cube, CubeList T, SplitStack stack, int depth)
    {
        MemoCache.Key tautKey = default;
        bool tautCacheActive = MemoCache.Enabled && Environment.GetEnvironmentVariable("ESPRESSO_CACHE_NO_TAUT") != "1";
        if (tautCacheActive)
        {
            tautKey = MemoCache.BuildCubeListKey(MemoCache.TagIsTautology, cube, T);
            if (MemoCache.TryGetBool(tautKey, out bool cached)) return cached;
        }
        SplitSummary summary;
        int tscResult;
        // --- inlined TautologySpecialCases ---
        {
            summary = default;
            Span<uint> sceil = cube.Temp[0].AsSpan();
            ReadOnlySpan<uint> scof = T.CofSpan, sFull = cube.FullSet.AsSpan();
            bool firstPass = true;
            tscResult = TautMaybe;
            while (true)
            {
                Copy(sceil, scof);
                for (int ti = 0; ti < T.Count; ti++)
                {
                    ReadOnlySpan<uint> sp = T.GetSpan(ti);
                    if (firstPass && IsFullCoverage(cube, sp, scof)) { tscResult = TautTrue; break; }
                    Or(sceil, sceil, sp);
                }
                if (tscResult != TautMaybe) break;
                firstPass = false;
                if (!AreEqual(sceil, sFull)) { tscResult = TautFalse; break; }
                summary = Cofactor.AnalyzeSplitVariable(cube, T);
                if (summary.VarsUnate == summary.VarsActive) { tscResult = TautFalse; break; }
                if (summary.VarsActive == 1) { tscResult = TautTrue; break; }
                if (summary.VarsUnate != 0)
                {
                    var analysis = Cofactor.AnalyzeAllVariables(cube, T);
                    T = FilterUnate(cube, T, analysis, cube.Temp[0], cube.Temp[1]);
                    continue;
                }
                if (summary.BestVarZeros < T.Count / 2)
                {
                    if (CoverManipulation.PartitionCubeList(cube, T, out CubeList A, out CubeList B) == 0)
                        break; // TautMaybe
                    bool rp = IsTautology(cube, A, stack, depth) ? true : IsTautology(cube, B, stack, depth);
                    A.ReturnCubes(); B.ReturnCubes();
                    if (tautCacheActive) MemoCache.PutBool(tautKey, rp);
                    return rp;
                }
                break; // TautMaybe
            }
        }
        // --- end inlined TautologySpecialCases ---
        if (tscResult != TautMaybe)
        {
            bool r = tscResult == TautTrue;
            if (tautCacheActive) MemoCache.PutBool(tautKey, r);
            return r;
        }
        stack.GetPair(depth, out BitVector cl, out BitVector cr);
        Span<uint> scl = cl.AsSpan(), scr = cr.AsSpan();
        int best = summary.Best;
        Cofactor.BuildSplitCubes(cube, T, best, scl, scr);
        var cofL = Cofactor.SingleVariableCofactor(cube, T, scl, best);
        var cofR = Cofactor.SingleVariableCofactor(cube, T, scr, best);
        bool result2 = IsTautology(cube, cofL, stack, depth + 1)
            && IsTautology(cube, cofR, stack, depth + 1);
        cofL.ReturnCubes();
        cofR.ReturnCubes();
        if (tautCacheActive) MemoCache.PutBool(tautKey, result2);
        return result2;
    }
    private static void FunctionalTautology(CubeData cube, CubeList T, SparseMatrix table, int rpCurrent, SplitStack stack, int depth)
    {
        SplitSummary summary;
        int ftscResult;
        // --- inlined FunctionalTautologySpecialCases ---
        {
            summary = default;
            ReadOnlySpan<uint> scof = T.CofSpan;
            ftscResult = TautMaybe;
            while (true)
            {
                for (int fi = 0; fi < T.Count; fi++)
                    if (!HasFlag(T[fi], CubeFlags.Redund) && IsFullCoverage(cube, T.GetSpan(fi), scof)) { ftscResult = TautTrue; break; }
                if (ftscResult != TautMaybe) break;
                summary = Cofactor.AnalyzeSplitVariable(cube, T);
                if (summary.VarsUnate == summary.VarsActive)
                {
                    int rownum = table.LastRowNum + 1;
                    SparseMatrix.Insert(table, rownum, rpCurrent);
                    for (int fi = 0; fi < T.Count; fi++)
                        if (HasFlag(T[fi], CubeFlags.Redund) && IsFullCoverage(cube, T.GetSpan(fi), scof))
                            SparseMatrix.Insert(table, rownum, (int)GetSortKey(T[fi]));
                    ftscResult = TautTrue;
                    break;
                }
                if (summary.VarsUnate != 0)
                {
                    var analysis = Cofactor.AnalyzeAllVariables(cube, T);
                    T = FilterUnate(cube, T, analysis, cube.Temp[1], cube.Temp[0]);
                    scof = T.CofSpan;
                    continue;
                }
                break; // TautMaybe
            }
        }
        // --- end inlined FunctionalTautologySpecialCases ---
        if (ftscResult == TautMaybe)
        {
            stack.GetPair(depth, out BitVector cl, out BitVector cr);
            Span<uint> scl = cl.AsSpan(), scr = cr.AsSpan();
            int best = summary.Best;
            Cofactor.BuildSplitCubes(cube, T, best, scl, scr);
            var cofL = Cofactor.SingleVariableCofactor(cube, T, scl, best);
            var cofR = Cofactor.SingleVariableCofactor(cube, T, scr, best);
            FunctionalTautology(cube, cofL, table, rpCurrent, stack, depth + 1);
            FunctionalTautology(cube, cofR, table, rpCurrent, stack, depth + 1);
            cofL.ReturnCubes();
            cofR.ReturnCubes();
        }
    }
    private static CubeList FilterUnate(CubeData cube, CubeList T, VariableAnalysis analysis, BitVector ceil, BitVector temp)
    {
        Span<uint> sceil = ceil.AsSpan(), stemp = temp.AsSpan();
        ReadOnlySpan<uint> scof = T.CofSpan;
        Copy(sceil, cube.EmptySet.AsSpan());
        for (int v = 0; v < cube.NumVars; v++)
            if (analysis.IsUnate[v]) Or(sceil, sceil, cube.VarMask[v].AsSpan());
        Cofactor.ReturnAnalysis(analysis);
        var filtered = ArrayPool<BitVector>.Shared.Rent(Math.Max(T.Count, 1));
        int fc = 0;
        for (int i = 0; i < T.Count; i++)
        {
            Or(stemp, T.GetSpan(i), scof);
            if (IsSubsetOf(sceil, stemp)) filtered[fc++] = T[i];
        }
        return new CubeList(T.Cof, filtered, fc, rented: true);
    }
}
public static class GaspOptimizer
{
    internal static bool IsFeasiblyCovered(CubeData cube, BitVectorFamily R, BitVector p, BitVector RAISE)
    {
        Span<uint> sr = cube.Temp[0].AsSpan();
        Or(sr, RAISE.AsSpan(), p.AsSpan());
        for (int i = 0; i < R.Count; i++)
        {
            if (!HasFlag(R.GetSet(i), CubeFlags.Active)) continue;
            if (DistanceCapped(cube, R.GetSpan(i), sr) == 0) return false;
        }
        return true;
    }
}
public static class Complement
{
    private const int UseComplLift = 0, UseComplLiftOnset = 1;
    public static BitVectorFamily ComputeComplement(CubeData cube, CubeList T)
    {
        MemoCache.Key key = default;
        bool cacheActive = MemoCache.Enabled;
        if (cacheActive)
        {
            key = MemoCache.BuildCubeListFamilyKey(MemoCache.TagComplement, cube, T);
            if (MemoCache.TryGetFamily(key, cube.Size, out var cached)) return cached;
        }
        var result = ComputeComplement(cube, T, new SplitStack(cube.Size, cube.Size), 0);
        if (cacheActive) MemoCache.PutFamily(key, result);
        return result;
    }
    private static BitVectorFamily ComputeComplement(CubeData cube, CubeList T, SplitStack stack, int depth)
    {
        BitVectorFamily Tbar;
        bool cscHandled = false;
        // --- inlined ComplementSpecialCases ---
        {
            BitVector cof = T.Cof;
            ReadOnlySpan<uint> scof = T.CofSpan, sFull = cube.FullSet.AsSpan();
            if (T.Count == 0)
            {
                Tbar = BitVectorFamily.Add(BitVectorFamily.Create(1, cube.Size), cube.FullSet);
                cscHandled = true;
            }
            else if (T.Count == 1)
            {
                BitVector tmp = Clone(cof);
                Or(tmp.AsSpan(), scof, T.GetSpan(0));
                Tbar = ComplementSingleCube(cube, tmp);
                cscHandled = true;
            }
            else
            {
                Tbar = default!;
                for (int t1 = 0; t1 < T.Count; t1++)
                    if (IsFullCoverage(cube, T.GetSpan(t1), scof))
                    {
                        Tbar = BitVectorFamily.Create(0, cube.Size);
                        cscHandled = true;
                        break;
                    }
                if (!cscHandled)
                {
                    BitVector ceil = Clone(cof);
                    Span<uint> sceil = ceil.AsSpan();
                    for (int t1 = 0; t1 < T.Count; t1++)
                        Or(sceil, sceil, T.GetSpan(t1));
                    if (!AreEqual(sceil, sFull))
                    {
                        BitVectorFamily ceilCompl = ComplementSingleCube(cube, ceil);
                        AndNot(sceil, sFull, sceil);
                        Or(cof.AsSpan(), scof, sceil);
                        Tbar = BitVectorFamily.Append(ComputeComplement(cube, T, stack, depth), ceilCompl);
                        cscHandled = true;
                    }
                    else
                    {
                        var analysis = Cofactor.AnalyzeAllVariables(cube, T);
                        if (analysis.VarsActive == 1)
                        {
                            Cofactor.ReturnAnalysis(analysis);
                            Tbar = BitVectorFamily.Create(0, cube.Size);
                            cscHandled = true;
                        }
                        else if (analysis.VarsUnate == analysis.VarsActive)
                        {
                            // --- inlined MapToUnate + Compute + MapFromUnate ---
                            BitVectorFamily ucA;
                            {
                                ucA = BitVectorFamily.Create(T.Count, analysis.VarsUnate);
                                ucA.Count = T.Count;
                                for (int i = 0; i < ucA.Count; i++) ucA.GetSpan(i).Clear();
                                int ncol = 0;
                                for (int i = 0; i < cube.Size; i++)
                                {
                                    if (analysis.PartZeros[i] <= 0) continue;
                                    int wordTest = WhichWord(i), bitTest = WhichBit(i);
                                    int wordSet = WhichWord(ncol), bitSet = WhichBit(ncol);
                                    for (int j = 0; j < T.Count; j++)
                                    {
                                        if ((T.GetSpan(j)[wordTest] & (1u << bitTest)) == 0)
                                            ucA.GetSpan(j)[wordSet] |= (uint)(1 << bitSet);
                                    }
                                    ncol++;
                                }
                            }
                            // Compute
                            {
                                for (int si = 0; si < ucA.Count; si++) SetSortKey(ucA.GetSet(si), PopCount(ucA.GetSpan(si)));
                                ucA = UnateComplement.ComplementRecursive(ucA);
                                if (ucA.Count > 0)
                                {
                                    for (int i = 0; i < ucA.Count; i++) SetSortKey(ucA.GetSet(i), PopCount(ucA.GetSpan(i)));
                                    int[] ucOrder = ArrayPool<int>.Shared.Rent(ucA.Count);
                                    for (int i = 0; i < ucA.Count; i++) ucOrder[i] = i;
                                    ucOrder.AsSpan(0, ucA.Count).Sort((a, b) =>
                                    {
                                        int sa = GetSortKey(ucA.GetSet(a)), sb = GetSortKey(ucA.GetSet(b));
                                        return sa != sb ? sa - sb : CompareAscending(ucA.GetSet(a), ucA.GetSet(b));
                                    });
                                    bool[] ucKeep = ArrayPool<bool>.Shared.Rent(ucA.Count);
                                    Array.Fill(ucKeep, true, 0, ucA.Count);
                                    for (int i = 0; i < ucA.Count; i++)
                                    {
                                        if (!ucKeep[ucOrder[i]]) continue;
                                        int iPop = PopCount(ucA.GetSpan(ucOrder[i]));
                                        for (int j = i + 1; j < ucA.Count; j++)
                                        {
                                            if (!ucKeep[ucOrder[j]]) continue;
                                            if (PopCount(ucA.GetSpan(ucOrder[j])) < iPop) continue;
                                            if (IsSubsetOf(ucA.GetSpan(ucOrder[i]), ucA.GetSpan(ucOrder[j]))) ucKeep[ucOrder[j]] = false;
                                        }
                                    }
                                    int ucCnt = 0;
                                    for (int i = 0; i < ucA.Count; i++)
                                        if (ucKeep[ucOrder[i]]) ucCnt++;
                                    var ucR = BitVectorFamily.Create(ucCnt, ucA.SfSize);
                                    for (int i = 0; i < ucA.Count; i++)
                                    {
                                        if (!ucKeep[ucOrder[i]]) continue;
                                        Array.Copy(ucA.Data, ucOrder[i] * ucA.Stride, ucR.Data, ucR.Count * ucR.Stride, ucA.Stride);
                                        ucR.Count++;
                                    }
                                    ArrayPool<int>.Shared.Return(ucOrder, clearArray: false);
                                    ArrayPool<bool>.Shared.Return(ucKeep, clearArray: false);
                                    ucA = ucR;
                                }
                            }
                            // MapFromUnate
                            {
                                var ucB = BitVectorFamily.Create(ucA.Count, cube.Size);
                                ucB.Count = ucA.Count;
                                int[] unate = ArrayPool<int>.Shared.Rent(cube.NumVars);
                                int nunate = 0;
                                for (int v = 0; v < cube.NumVars; v++)
                                    if (analysis.IsUnate[v]) unate[nunate++] = v;
                                for (int si = 0; si < ucA.Count; si++)
                                {
                                    ReadOnlySpan<uint> ucsp = ucA.GetSpan(si);
                                    Span<uint> ucspB = ucB.GetSpan(si);
                                    Fill(ucspB, cube.Size);
                                    for (int ncol = 0; ncol < nunate; ncol++)
                                        if (Contains(ucsp, ncol))
                                            for (int i = cube.FirstPart![unate[ncol]]; i <= cube.LastPart![unate[ncol]]; i++)
                                                if (analysis.PartZeros[i] == 0) Remove(ucspB, i);
                                }
                                ArrayPool<int>.Shared.Return(unate, clearArray: false);
                                Tbar = ucB;
                            }
                            // --- end inlined MapToUnate + Compute + MapFromUnate ---
                            Cofactor.ReturnAnalysis(analysis);
                            cscHandled = true;
                        }
                        else
                            Cofactor.ReturnAnalysis(analysis);
                    }
                }
            }
        }
        // --- end inlined ComplementSpecialCases ---
        if (cscHandled) return Tbar;

        stack.GetPair(depth, out BitVector cl, out BitVector cr);
        Span<uint> scl = cl.AsSpan(), scr = cr.AsSpan();
        var summary = Cofactor.AnalyzeSplitVariable(cube, T);
        int best = summary.Best;
        Cofactor.BuildSplitCubes(cube, T, best, scl, scr);
        var cofL = Cofactor.SingleVariableCofactor(cube, T, scl, best);
        var cofR = Cofactor.SingleVariableCofactor(cube, T, scr, best);
        BitVectorFamily Tl = ComputeComplement(cube, cofL, stack, depth + 1);
        BitVectorFamily Tr = ComputeComplement(cube, cofR, stack, depth + 1);
        cofL.ReturnCubes();
        cofR.ReturnCubes();
        int lifting = Tr.Count * Tl.Count > (Tr.Count + Tl.Count) * T.Count ? UseComplLiftOnset : UseComplLift;

        // --- inlined MergeComplements ---
        {
            ReadOnlySpan<uint> smcl = cl.AsSpan(), smcr = cr.AsSpan();
            for (int i = 0; i < Tl.Count; i++)
            {
                And(Tl.GetSpan(i), Tl.GetSpan(i), smcl);
                AddFlag(Tl.GetSet(i), CubeFlags.Active);
            }
            for (int i = 0; i < Tr.Count; i++)
            {
                And(Tr.GetSpan(i), Tr.GetSpan(i), smcr);
                AddFlag(Tr.GetSet(i), CubeFlags.Active);
            }
            Copy(cube.Temp[0].AsSpan(), cube.VarMask[best].AsSpan());
            BitVector[] L1 = SfListSorted(cube, Tl), R1 = SfListSorted(cube, Tr);
            {
                int li = 0, ri = 0;
                while (li < L1.Length && ri < R1.Length)
                    switch (CubeDistance.Distance1Order(cube, L1[li], R1[ri]))
                    {
                        case 1:  ri++; break;
                        case -1: li++; break;
                        default:
                            ClearFlag(R1[ri], CubeFlags.Active);
                            Span<uint> sL = L1[li].AsSpan();
                            Or(sL, sL, R1[ri].AsSpan());                    ri++;
                            break;
                    }
            }
            switch(lifting)
            {
                case UseComplLiftOnset:
                {
                    // --- inlined MergeCubeList ---
                    BitVectorFamily Tcover;
                    {
                        Tcover = BitVectorFamily.Create(T.Count, cube.Size);
                        ReadOnlySpan<uint> mcCofSpan = T.CofSpan;
                        for (int mci = 0; mci < T.Count; mci++) Or(Tcover.GetSpan(mci), T.GetSpan(mci), mcCofSpan);
                        Tcover.Count = T.Count;
                    }
                    // --- end inlined MergeCubeList ---
                    LiftComplementOnset(cube, L1, Tcover, cr, best);
                    LiftComplementOnset(cube, R1, Tcover, cl, best);
                    break;
                }
                case UseComplLift:
                    LiftComplement(cube, L1, R1, cr, best);
                    LiftComplement(cube, R1, L1, cl, best);
                    break;
            }
            Tbar = BitVectorFamily.Create(Tl.Count + Tr.Count, cube.Size);
            for (int i = 0; i < Tl.Count; i++)
                BitVectorFamily.Add(Tbar, Tl.GetSet(i));
            for (int i = 0; i < Tr.Count; i++)
                if (HasFlag(Tr.GetSet(i), CubeFlags.Active)) BitVectorFamily.Add(Tbar, Tr.GetSet(i));
        }
        // --- end inlined MergeComplements ---
        return Tbar;
    }
    private static BitVectorFamily ComplementSingleCube(CubeData cube, BitVector p)
    {
        Span<uint> sdiff = cube.Temp[7].AsSpan();
        ReadOnlySpan<uint> sfull = cube.FullSet.AsSpan(), sp = p.AsSpan();
        AndNot(sdiff, sfull, sp);
        BitVectorFamily R = BitVectorFamily.Create(cube.NumVars, cube.Size);
        for (int var = 0; var < cube.NumVars; var++)
        {
            ReadOnlySpan<uint> smask = cube.VarMask[var].AsSpan();
            if (!AreDisjoint(sdiff, smask))
                MergeWithMask(R.GetSet(R.Count++).AsSpan(), sdiff, sfull, smask);
        }
        return R;
    }
    private static void LiftComplement(CubeData cube, BitVector[] A1, BitVector[] B1, BitVector bcube, int var)
    {
        BitVector lift = cube.Temp[4], liftor = cube.Temp[5], mask = cube.VarMask[var];
        Span<uint> slift = lift.AsSpan(), sliftor = liftor.AsSpan();
        ReadOnlySpan<uint> sbcube = bcube.AsSpan(), smask = mask.AsSpan();
        And(sliftor, sbcube, smask);
        for (int ai = 0; ai < A1.Length; ai++)
        {
            BitVector a = A1[ai];
            if (!HasFlag(a, CubeFlags.Active)) continue;
            Span<uint> sa = a.AsSpan();
            MergeWithMask(slift, sbcube, sa, smask);
            int liftPop = PopCount(slift);
            for (int bi = 0; bi < B1.Length; bi++)
            {
                if (PopCount(B1[bi].AsSpan()) < liftPop) continue;
                if (!IsSubsetOf(slift, B1[bi].AsSpan())) continue;
                Or(sa, sa, sliftor);
                break;
            }
        }
    }
    private static void LiftComplementOnset(CubeData cube, BitVector[] A1, BitVectorFamily T, BitVector bcube, int var)
    {
        BitVector lift = cube.Temp[4], mask = cube.VarMask[var];
        Span<uint> slift = lift.AsSpan();
        ReadOnlySpan<uint> sbcube = bcube.AsSpan(), smask = mask.AsSpan();
        for (int ai = 0; ai < A1.Length; ai++)
        {
            BitVector a = A1[ai];
            if (!HasFlag(a, CubeFlags.Active)) continue;
            Span<uint> sa = a.AsSpan();
            And(slift, sbcube, smask);
            Or(slift, sa, slift);
            bool canLift = true;
            for (int ti = 0; ti < T.Count; ti++)
                if (AreDistance0(cube, T.GetSpan(ti), slift)) { canLift = false; break; }
            if (canLift)
            {
                Copy(sa, slift);
                AddFlag(a, CubeFlags.Active);
            }
        }
    }
    private static BitVector[] SfListSorted(CubeData cube, BitVectorFamily F)
    {
        var arr = new BitVector[F.Count];
        for (int i = 0; i < F.Count; i++)
            arr[i] = F.GetSet(i);
        arr.AsSpan().Sort((a, b) => CubeDistance.Distance1Order(cube, a, b));
        return arr;
    }
}
