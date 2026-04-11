namespace EspressoCS;

using static CubeContext;
using static SetOps;
using static SetFamily;

/// <summary>Mirrors pair_t / ppair from espresso.h.</summary>
public class Pair
{
    public int Cnt;
    public int[]? Var1;
    public int[]? Var2;
}

/// <summary>Port of pair.c — variable pairing / two-bit decoder encoding.</summary>
public static class PairOps
{
    // Module-level statics (mirrors C file-scope statics in pair.c)
    private static int       _bestCost;
    private static int[][]?  _costArray;
    private static Pair?     _bestPair;
    private static PSet      _bestPhase;
    private static Pla?      _globalPla;
    private static SetFamily? _bestF, _bestD, _bestR;
    private static int       _pairMinimStrategy;

    // -----------------------------------------------------------------------
    // set_pair / set_pair1
    // -----------------------------------------------------------------------

    /// <summary>set_pair — set up two-bit decoder pairing (calls set_pair1 with adjust_labels=true).</summary>
    public static void SetPair(Pla PLA) => SetPair1(PLA, true);

    /// <summary>set_pair1 — core pairing logic.</summary>
    public static void SetPair1(Pla PLA, bool adjustLabels)
    {
        Pair pair = PLA.PairData!;
        if (adjustLabels) CvrOut.MakeupLabels(PLA);

        bool[] paired = new bool[NumBinaryVars];
        for (int i = 0; i < pair.Cnt; i++)
        {
            if (pair.Var1![i] > 0 && pair.Var1[i] <= NumBinaryVars &&
                pair.Var2![i] > 0 && pair.Var2[i] <= NumBinaryVars)
            {
                paired[pair.Var1[i] - 1] = true;
                paired[pair.Var2[i] - 1] = true;
            }
            else
                throw new InvalidOperationException("can only pair binary-valued variables");
        }

        PLA.F = DelVar(PairVar(PLA.F!, pair), paired);
        PLA.R = DelVar(PairVar(PLA.R!, pair), paired);
        PLA.D = DelVar(PairVar(PLA.D!, pair), paired);

        int oldSize          = Size;
        int oldNumVars        = NumVars;
        int oldNumBinaryVars  = NumBinaryVars;
        int oldMvStart        = FirstPart![NumBinaryVars];

        int newNumBinaryVars = 0;
        for (int v = 0; v < oldNumBinaryVars; v++)
            if (!paired[v]) newNumBinaryVars++;
        int newNumVars = newNumBinaryVars + pair.Cnt + (oldNumVars - oldNumBinaryVars);
        int[] newPartSize = new int[newNumVars];
        for (int v = 0; v < pair.Cnt; v++)
            newPartSize[newNumBinaryVars + v] = 4;
        for (int v = 0; v < oldNumVars - oldNumBinaryVars; v++)
            newPartSize[newNumBinaryVars + pair.Cnt + v] = PartSize![oldNumBinaryVars + v];
        SetdownCube();
        NumVars        = newNumVars;
        NumBinaryVars  = newNumBinaryVars;
        PartSize       = newPartSize;
        CubeSetup();

        if (adjustLabels)
        {
            string?[]  oldlabel = PLA.Label!;
            PLA.Label = new string?[Size];
            for (int v = 0; v < pair.Cnt; v++)
            {
                int newvar   = NumBinaryVars * 2 + v * 4;
                string? var1    = oldlabel[(pair.Var1![v] - 1) * 2 + 1];
                string? var2    = oldlabel[(pair.Var2![v] - 1) * 2 + 1];
                string? var1bar = oldlabel[(pair.Var1[v] - 1) * 2];
                string? var2bar = oldlabel[(pair.Var2[v] - 1) * 2];
                PLA.Label[newvar]     = $"{var1bar}+{var2bar}";
                PLA.Label[newvar + 1] = $"{var1bar}+{var2}";
                PLA.Label[newvar + 2] = $"{var1}+{var2bar}";
                PLA.Label[newvar + 3] = $"{var1}+{var2}";
            }
            // Copy unpaired binary labels
            int idx = 0;
            for (int v = 0; v < oldNumBinaryVars; v++)
            {
                if (!paired[v])
                {
                    PLA.Label[2 * idx]     = oldlabel[2 * v];
                    PLA.Label[2 * idx + 1] = oldlabel[2 * v + 1];
                    idx++;
                }
            }
            // Copy remaining mv labels
            int newMvStart = NumBinaryVars * 2 + pair.Cnt * 4;
            for (int i = oldMvStart; i < oldSize; i++)
                PLA.Label[newMvStart + i - oldMvStart] = oldlabel[i];
        }

        // Paired variables should not be sparse
        for (int v = 0; v < pair.Cnt; v++)
            SparseVar![NumBinaryVars + v] = 0;
    }

    // -----------------------------------------------------------------------
    // pairvar
    // -----------------------------------------------------------------------
    public static SetFamily PairVar(SetFamily A, Pair pair)
    {
        int insertCol = FirstPart![NumVars - 1];
        A = SfDelcol(A, insertCol, -4 * pair.Cnt);

        for (int _i = 0; _i < A.Count; _i++)
        {
            var p = A.GetSet(_i);
            for (int pairnum = 0; pairnum < pair.Cnt; pairnum++)
            {
                int p1  = FirstPart![pair.Var1![pairnum] - 1];
                int p2  = FirstPart![pair.Var2![pairnum] - 1];
                int b1  = IsInSet(p, p2 + 1) ? 1 : 0;
                int b0  = IsInSet(p, p2)     ? 1 : 0;
                int val = insertCol + pairnum * 4;
                if (IsInSet(p, p1))        // a0
                {
                    if (b0 != 0) SetInsert(p, val + 3);
                    if (b1 != 0) SetInsert(p, val + 2);
                }
                if (IsInSet(p, p1 + 1))    // a1
                {
                    if (b0 != 0) SetInsert(p, val + 1);
                    if (b1 != 0) SetInsert(p, val);
                }
            }
        }
        return A;
    }

    // -----------------------------------------------------------------------
    // delvar
    // -----------------------------------------------------------------------
    public static SetFamily DelVar(SetFamily A, bool[] paired)
    {
        bool run = false;
        int firstRun = 0, runLength = 0, offset = 0;

        for (int v = 0; v < NumBinaryVars; v++)
        {
            if (paired[v])
            {
                if (run)
                    runLength += PartSize![v];
                else
                {
                    run       = true;
                    firstRun  = FirstPart![v];
                    runLength = PartSize![v];
                }
            }
            else
            {
                if (run)
                {
                    A       = SfDelcol(A, firstRun - offset, runLength);
                    run     = false;
                    offset += runLength;
                }
            }
        }
        if (run)
            A = SfDelcol(A, firstRun - offset, runLength);
        return A;
    }

    // -----------------------------------------------------------------------
    // find_optimal_pairing
    // -----------------------------------------------------------------------
    public static void FindOptimalPairing(Pla PLA, int strategy)
    {
        int[][] costArr = FindPairingCost(PLA, strategy);

        if (Globals.Summary)
        {
            Console.Write("    ");
            for (int i = 0; i < NumBinaryVars; i++) Console.Write($"{i + 1,3} ");
            Console.WriteLine();
            for (int i = 0; i < NumBinaryVars; i++)
            {
                Console.Write($"{i + 1,3} ");
                for (int j = 0; j < NumBinaryVars; j++) Console.Write($"{costArr[i][j],3} ");
                Console.WriteLine();
            }
        }

        if (NumBinaryVars <= 14)
            PLA.PairData = PairBestCost(costArr);
        else
            GreedyBestCost(costArr, out PLA.PairData);

        Console.Write("# ");
        PrintPair(PLA.PairData!);
        SetPair(PLA);
        PLA.F = Stubs.Espresso(PLA.F!, PLA.D!, PLA.R!);
    }

    // -----------------------------------------------------------------------
    // find_pairing_cost
    // -----------------------------------------------------------------------
    public static int[][] FindPairingCost(Pla PLA, int strategy)
    {
        int[][] costArr = new int[NumBinaryVars][];
        for (int i = 0; i < NumBinaryVars; i++)
            costArr[i] = new int[NumBinaryVars];

        PLA.PairData = PairNew(1);
        PLA.PairData.Cnt = 1;

        for (int var1 = 0; var1 < NumBinaryVars - 1; var1++)
        {
            for (int var2 = var1 + 1; var2 < NumBinaryVars; var2++)
            {
                SetFamily? Fsave = null, Dsave = null, Rsave = null;
                int xNumBinaryVars = 0, xNumVars = 0;
                int[]? xPartSize = null;
                int cost = 0;

                if (strategy > 0)
                {
                    Fsave = SfSave(PLA.F!);
                    Dsave = SfSave(PLA.D!);
                    Rsave = SfSave(PLA.R!);
                    xNumBinaryVars = NumBinaryVars;
                    xNumVars       = NumVars;
                    xPartSize      = new int[NumVars];
                    for (int i = 0; i < NumVars; i++) xPartSize[i] = PartSize![i];
                    PLA.PairData.Var1![0] = var1 + 1;
                    PLA.PairData.Var2![0] = var2 + 1;
                    SetPair1(PLA, false);
                }

                switch (strategy)
                {
                    case 3:
                        PLA.F = Stubs.MinimizeExact(PLA.F!, PLA.D!, PLA.R!, 1);
                        cost  = Fsave!.Count - PLA.F.Count;
                        break;
                    case 2:
                        PLA.F = Stubs.Espresso(PLA.F!, PLA.D!, PLA.R!);
                        cost  = Fsave!.Count - PLA.F.Count;
                        break;
                    case 1:
                        PLA.F = Reduce.ReduceCover(PLA.F!, PLA.D!);
                        PLA.F = Expand.ExpandCover(PLA.F, PLA.R!, 0);
                        PLA.F = Irred.Irredundant(PLA.F, PLA.D!);
                        cost  = Fsave!.Count - PLA.F.Count;
                        break;
                    case 0:
                        PSet mask = SetNew(Size);
                        SetOr(mask, VarMask![var1], VarMask[var2]);
                        SetFamily T = Contain.DistMerge(SfSave(PLA.F!), mask);
                        cost = PLA.F!.Count - T.Count;
                        break;
                }

                costArr[var1][var2] = cost;

                if (strategy > 0)
                {
                    SetdownCube();
                    NumBinaryVars = xNumBinaryVars;
                    NumVars       = xNumVars;
                    PartSize      = xPartSize;
                    CubeSetup();
                    PLA.F = Fsave!;
                    PLA.D = Dsave!;
                    PLA.R = Rsave!;
                }
            }
        }

        PairFree(PLA.PairData);
        PLA.PairData = null;
        return costArr;
    }

    // -----------------------------------------------------------------------
    // print_pair
    // -----------------------------------------------------------------------
    public static void PrintPair(Pair pair)
    {
        Console.Write("pair is");
        for (int i = 0; i < pair.Cnt; i++)
            Console.Write($" ({pair.Var1![i]} {pair.Var2![i]})");
        Console.WriteLine();
    }

    // -----------------------------------------------------------------------
    // greedy_best_cost
    // -----------------------------------------------------------------------
    public static int GreedyBestCost(int[][] costArrLocal, out Pair pairOut)
    {
        Pair pair = PairNew(NumBinaryVars);
        PSet cand = SetFull(NumBinaryVars);
        int totalCost = 0;

        while (SetOrd(cand) >= 2)
        {
            int maxcost = -1, besti = -1, bestj = -1;
            for (int i = 0; i < NumBinaryVars; i++)
            {
                if (!IsInSet(cand, i)) continue;
                for (int j = i + 1; j < NumBinaryVars; j++)
                {
                    if (!IsInSet(cand, j)) continue;
                    if (costArrLocal[i][j] > maxcost)
                    {
                        maxcost = costArrLocal[i][j];
                        besti = i; bestj = j;
                    }
                }
            }
            pair.Var1![pair.Cnt] = besti + 1;
            pair.Var2![pair.Cnt] = bestj + 1;
            pair.Cnt++;
            SetRemove(cand, besti);
            SetRemove(cand, bestj);
            totalCost += maxcost;
        }
        pairOut = pair;
        return totalCost;
    }

    // -----------------------------------------------------------------------
    // pair_best_cost
    // -----------------------------------------------------------------------
    public static Pair PairBestCost(int[][] costArrLocal)
    {
        _bestCost  = -1;
        _bestPair  = null;
        _costArray = costArrLocal;

        Pair pair      = PairNew(NumBinaryVars);
        PSet candidate = SetFull(NumBinaryVars);
        GenerateAllPairs(pair, NumBinaryVars, candidate, FindBestCost);
        PairFree(pair);
        return _bestPair!;
    }

    // -----------------------------------------------------------------------
    // find_best_cost  (callback)
    // -----------------------------------------------------------------------
    private static void FindBestCost(Pair pair)
    {
        int cost = 0;
        for (int i = 0; i < pair.Cnt; i++)
            cost += _costArray![pair.Var1![i] - 1][pair.Var2![i] - 1];
        if (cost > _bestCost)
        {
            _bestCost = cost;
            _bestPair = PairSave(pair, pair.Cnt);
        }
        if ((Globals.Debug & EspressoConstants.Mincov) != 0 && Globals.Trace)
        {
            Console.Write($"cost is {cost} ");
            PrintPair(pair);
        }
    }

    // -----------------------------------------------------------------------
    // pair_all
    // -----------------------------------------------------------------------
    public static void PairAll(Pla PLA, int pairStrategy)
    {
        _globalPla           = PLA;
        _pairMinimStrategy   = pairStrategy;
        _bestCost            = PLA.F!.Count + 1;
        _bestPair            = null;
        _bestPhase           = PSet.Null;
        _bestF = _bestD = _bestR = null;

        Pair pair      = PairNew(NumBinaryVars);
        PSet candidate = SetFill(SetNew(NumBinaryVars), NumBinaryVars);
        GenerateAllPairs(pair, NumBinaryVars, candidate, MinimizePair);

        PLA.PairData = _bestPair;
        PLA.Phase    = _bestPhase;
        SetPair(PLA);
        Console.Write("# ");
        PrintPair(PLA.PairData!);

        PLA.F = _bestF!;
        PLA.D = _bestD!;
        PLA.R = _bestR!;
    }

    // -----------------------------------------------------------------------
    // minimize_pair  (callback)
    // -----------------------------------------------------------------------
    private static void MinimizePair(Pair pair)
    {
        SetFamily Fsave = SfSave(_globalPla!.F!);
        SetFamily Dsave = SfSave(_globalPla.D!);
        SetFamily Rsave = SfSave(_globalPla.R!);

        int xNumBinaryVars = NumBinaryVars;
        int xNumVars       = NumVars;
        int[] xPartSize    = new int[NumVars];
        for (int i = 0; i < NumVars; i++) xPartSize[i] = PartSize![i];

        _globalPla.PairData = pair;
        SetPair1(_globalPla, false);

        if (Globals.Summary) PrintPair(pair);
        switch (_pairMinimStrategy)
        {
            case 2:
                Opo.PhaseAssignment(_globalPla, 0);
                if (Globals.Summary)
                    Console.WriteLine($"# phase is {_globalPla.Phase}");
                break;
            case 1:
                _globalPla.F = Stubs.MinimizeExact(_globalPla.F!, _globalPla.D!, _globalPla.R!, 1);
                break;
            case 0:
                _globalPla.F = Stubs.Espresso(_globalPla.F!, _globalPla.D!, _globalPla.R!);
                break;
        }

        if (_globalPla.F!.Count < _bestCost)
        {
            _bestCost  = _globalPla.F.Count;
            _bestPair  = PairSave(pair, pair.Cnt);
            _bestPhase = _globalPla.Phase.IsNull == false ? SetSave(_globalPla.Phase) : PSet.Null;
            _bestF     = SfSave(_globalPla.F);
            _bestD     = SfSave(_globalPla.D!);
            _bestR     = SfSave(_globalPla.R!);
        }

        // Restore cube structure
        SetdownCube();
        NumBinaryVars = xNumBinaryVars;
        NumVars       = xNumVars;
        PartSize      = xPartSize;
        CubeSetup();

        // Restore covers
        _globalPla.F        = Fsave;
        _globalPla.D        = Dsave;
        _globalPla.R        = Rsave;
        _globalPla.PairData = null;
        _globalPla.Phase    = PSet.Null;
    }

    // -----------------------------------------------------------------------
    // generate_all_pairs
    // -----------------------------------------------------------------------
    public static void GenerateAllPairs(Pair pair, int n, PSet candidate, Action<Pair> action)
    {
        if (SetOrd(candidate) < 2)
        {
            action(pair);
            return;
        }

        Pair recurPair      = PairSave(pair, n);
        PSet recurCandidate = SetSave(candidate);

        // Find first variable in candidate set
        int i = 0;
        for (; i < n; i++)
            if (IsInSet(candidate, i)) break;

        // Try all pairs of i with other variables
        for (int j = i + 1; j < n; j++)
        {
            if (!IsInSet(candidate, j)) continue;
            SetRemove(recurCandidate, i);
            SetRemove(recurCandidate, j);
            recurPair.Var1![recurPair.Cnt] = i + 1;
            recurPair.Var2![recurPair.Cnt] = j + 1;
            recurPair.Cnt++;
            GenerateAllPairs(recurPair, n, recurCandidate, action);
            recurPair.Cnt--;
            SetInsert(recurCandidate, i);
            SetInsert(recurCandidate, j);
        }

        // If odd, generate pairs not including i
        if ((SetOrd(candidate) % 2) == 1)
        {
            SetRemove(recurCandidate, i);
            GenerateAllPairs(recurPair, n, recurCandidate, action);
        }
    }

    // -----------------------------------------------------------------------
    // pair_new / pair_save / pair_free
    // -----------------------------------------------------------------------
    public static Pair PairNew(int n) =>
        new Pair { Cnt = 0, Var1 = new int[n], Var2 = new int[n] };

    public static Pair PairSave(Pair pair, int n)
    {
        Pair p1 = PairNew(n);
        p1.Cnt = pair.Cnt;
        for (int k = 0; k < pair.Cnt; k++)
        {
            p1.Var1![k] = pair.Var1![k];
            p1.Var2![k] = pair.Var2![k];
        }
        return p1;
    }

    public static void PairFree(Pair pair) { /* GC handles memory */ }
}
