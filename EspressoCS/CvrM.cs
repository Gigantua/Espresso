namespace EspressoCS;

/// <summary>Ports cvrm.c — miscellaneous cover manipulation, sort, partition.</summary>
public static class CvrM
{
    // Module-level statics (mirrors C static variables)
    private static SetFamily? _fmin;
    private static PSet       _phase;

    // -----------------------------------------------------------------------
    // cb_unravel (static in C)
    // -----------------------------------------------------------------------
    private static void CbUnravel(PSet c, int start, int end, PSet startbase, SetFamily B1)
    {
        PSet   baseSet  = CubeContext.Temp![0];
        int    expansion, place, skip, size, offset;

        expansion = 1;
        SetOps.SetCopy(baseSet, startbase);
        for (int var = start; var <= end; var++)
        {
            if ((size = SetOps.SetDist(c, CubeContext.VarMask![var])) < 2)
                SetOps.SetOr(baseSet, baseSet, CubeContext.VarMask![var]);
            else
                expansion *= size;
        }
        SetOps.SetAnd(baseSet, c, baseSet);

        offset     = B1.Count;
        B1.Count  += expansion;
        for (int pi = offset; pi < B1.Count; pi++)
            SetOps.InlineCopy(B1.GetSet(pi), baseSet);

        place = expansion;
        for (int var = start; var <= end; var++)
        {
            if ((size = SetOps.SetDist(c, CubeContext.VarMask![var])) > 1)
            {
                skip  = place;
                place = place / size;
                int n = 0;
                for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                {
                    if (SetOps.IsInSet(c, i))
                    {
                        for (int j = n; j < expansion; j += skip)
                            for (int k = 0; k < place; k++)
                                SetOps.SetInsert(B1.GetSet(j + k + offset), i);
                        n += place;
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // unravel_range
    // -----------------------------------------------------------------------
    public static SetFamily UnravelRange(SetFamily B, int start, int end)
    {
        PSet startbase = CubeContext.Temp![1];

        SetOps.SetCopy(startbase, CubeContext.EmptySet);
        for (int var = 0; var < start; var++)
            SetOps.SetOr(startbase, startbase, CubeContext.VarMask![var]);
        for (int var = end + 1; var < CubeContext.NumVars; var++)
            SetOps.SetOr(startbase, startbase, CubeContext.VarMask![var]);

        int totalSize = 0;
        for (int si = 0; si < B.Count; si++)
        {
            var p = B.GetSet(si);
            int expansion = 1;
            for (int var = start; var <= end; var++)
            {
                int size = SetOps.SetDist(p, CubeContext.VarMask![var]);
                if (size >= 2)
                {
                    expansion *= size;
                    if (expansion > 1000000)
                        throw new InvalidOperationException("unreasonable expansion in unravel");
                }
            }
            totalSize += expansion;
        }

        var B1 = SetFamily.SfNew(totalSize, CubeContext.Size);
        for (int si = 0; si < B.Count; si++)
            CbUnravel(B.GetSet(si), start, end, startbase, B1);
        return B1;
    }

    // unravel
    public static SetFamily Unravel(SetFamily B, int start) =>
        UnravelRange(B, start, CubeContext.NumVars - 1);

    // -----------------------------------------------------------------------
    // lex_sort
    // -----------------------------------------------------------------------
    public static SetFamily LexSort(SetFamily T)
    {
        int count = T.Count, sfSize = T.SfSize;
        var T1 = SetFamily.SfUnlist(
            SetFamily.SfSort(T, (a, b) => SetOps.LexOrder(a, b)),
            count, sfSize);
        return T1;
    }

    // size_sort
    public static SetFamily SizeSort(SetFamily T)
    {
        int count = T.Count, sfSize = T.SfSize;
        var T1 = SetFamily.SfUnlist(
            SetFamily.SfSort(T, (a, b) => SetOps.Descend(a, b)),
            count, sfSize);
        return T1;
    }

    // mini_sort
    public static SetFamily MiniSort(SetFamily F, Comparison<PSet> compare)
    {
        int n = CubeContext.Size;
        int[] count = SetFamily.SfCount(F);

        for (int si = 0; si < F.Count; si++)
        {
            var p = F.GetSet(si);
            int cnt = 0;
            for (int i = 0; i < n; i++)
                if (SetOps.IsInSet(p, i)) cnt += count[i];
            SetOps.PutSize(p, cnt);
        }

        int fcount = F.Count, sfSize = F.SfSize;
        var F1     = SetFamily.SfList(F);
        Array.Sort(F1, 0, fcount, Comparer<PSet>.Create(compare));
        return SetFamily.SfUnlist(F1, fcount, sfSize);
    }

    // sort_reduce
    public static SetFamily SortReduce(SetFamily T)
    {
        if (T.Count == 0) return T;

        int  n        = CubeContext.NumVars;
        int  bestsize = -1;
        PSet largest  = PSet.Null;

        for (int si = 0; si < T.Count; si++)
        {
            var p    = T.GetSet(si);
            int size = SetOps.SetOrd(p);
            if (size > bestsize) { largest = p; bestsize = size; }
        }

        for (int si = 0; si < T.Count; si++)
        {
            var p = T.GetSet(si);
            SetOps.PutSize(p, ((n - Stubs.Cdist(largest, p)) << 7) + Math.Min(SetOps.SetOrd(p), 127));
        }

        int tcount = T.Count, sfSize = T.SfSize;
        var T1     = SetFamily.SfList(T);
        Array.Sort(T1, 0, tcount, Comparer<PSet>.Create((a, b) => SetOps.Descend(a, b)));
        return SetFamily.SfUnlist(T1, tcount, sfSize);
    }

    // random_order
    public static SetFamily RandomOrder(SetFamily F)
    {
        var temp = SetOps.SetNew(F.SfSize);
        for (int i = F.Count - 1; i > 0; i--)
        {
            int k = (i * 23 + 997) % i;
            SetOps.SetCopy(temp, F.GetSet(k));
            SetOps.SetCopy(F.GetSet(k), F.GetSet(i));
            SetOps.SetCopy(F.GetSet(i), temp);
        }
        return F;
    }

    // -----------------------------------------------------------------------
    // cubelist_partition
    // -----------------------------------------------------------------------
    public static int CubelistPartition(PSet[] T, out PSet[]? A, out PSet[]? B, uint compDebug)
    {
        int numcube = CubeListSize(T);

        for (int ti = 2; ti < T.Length && !T[ti].IsNull; ti++)
            SetOps.ResetFlag(T[ti], SetOps.Covered);

        PSet seed = SetOps.SetSave(T[2]);
        PSet cof  = T[0];
        SetOps.SetFlag(T[2], SetOps.Covered);
        int count = 1;

        bool change;
        do
        {
            change = false;
            for (int ti = 2; ti < T.Length && !T[ti].IsNull; ti++)
            {
                var p = T[ti];
                if (!SetOps.TestP(p, SetOps.Covered) && Stubs.Ccommon(p, seed, cof))
                {
                    SetOps.InlineAnd(seed, seed, p);
                    SetOps.SetFlag(p, SetOps.Covered);
                    change = true;
                    count++;
                }
            }
        } while (change);

        SetOps.SetFree(seed);

        if (compDebug != 0)
            Console.WriteLine($"COMPONENT_REDUCTION: split into {count} {numcube - count}");

        if (count != numcube)
        {
            var aArr = new PSet[numcube + 3];
            var bArr = new PSet[numcube + 3];
            aArr[0] = SetOps.SetSave(T[0]);
            bArr[0] = SetOps.SetSave(T[0]);
            int ai = 2, bi = 2;

            for (int ti = 2; ti < T.Length && !T[ti].IsNull; ti++)
            {
                var p = T[ti];
                if (SetOps.TestP(p, SetOps.Covered))
                    aArr[ai++] = p;
                else
                    bArr[bi++] = p;
            }
            aArr[ai] = PSet.Null;
            bArr[bi] = PSet.Null;
            A = aArr;
            B = bArr;
        }
        else
        {
            A = null;
            B = null;
        }

        return numcube - count;
    }

    // -----------------------------------------------------------------------
    // cof_output -- quick cofactor against a single output function
    // -----------------------------------------------------------------------
    public static SetFamily CofOutput(SetFamily T, int i)
    {
        PSet mask = CubeContext.VarMask![CubeContext.Output];
        var  T1   = SetFamily.SfNew(T.Count, CubeContext.Size);
        for (int si = 0; si < T.Count; si++)
        {
            var p = T.GetSet(si);
            if (SetOps.IsInSet(p, i))
            {
                var pdest = T1.GetSet(T1.Count++);
                SetOps.InlineOr(pdest, p, mask);
                SetOps.ResetFlag(pdest, SetOps.Prime);
            }
        }
        return T1;
    }

    // uncof_output -- quick intersection against a single output function
    public static SetFamily? UncofOutput(SetFamily? T, int i)
    {
        if (T == null) return T;
        PSet mask = CubeContext.VarMask![CubeContext.Output];
        for (int si = 0; si < T.Count; si++)
        {
            var p = T.GetSet(si);
            SetOps.InlineDiff(p, p, mask);
            SetOps.SetInsert(p, i);
        }
        return T;
    }

    // -----------------------------------------------------------------------
    // foreach_output_function
    // -----------------------------------------------------------------------
    public static void ForeachOutputFunction(
        Pla PLA,
        Func<Pla, int, int> func,
        Func<Pla, int, int> func1)
    {
        for (int i = 0; i < CubeContext.PartSize![CubeContext.Output]; i++)
        {
            var PLA1 = Pla.NewPla();
            int base1 = CubeContext.FirstPart![CubeContext.Output];
            PLA1.F = CofOutput(PLA.F!,  i + base1);
            PLA1.R = CofOutput(PLA.R!,  i + base1);
            PLA1.D = CofOutput(PLA.D!,  i + base1);

            if (func(PLA1, i) == 0) { Pla.FreePla(PLA1); return; }

            PLA1.F = UncofOutput(PLA1.F, i + base1);
            PLA1.R = UncofOutput(PLA1.R, i + base1);
            PLA1.D = UncofOutput(PLA1.D, i + base1);

            if (func1(PLA1, i) == 0) { Pla.FreePla(PLA1); return; }

            Pla.FreePla(PLA1);
        }
    }

    // -----------------------------------------------------------------------
    // so_espresso / so_both_espresso
    // -----------------------------------------------------------------------
    public static void SoEspresso(Pla PLA, int strategy)
    {
        _fmin = SetFamily.SfNew(PLA.F!.Count, CubeContext.Size);
        if (strategy == 0)
            ForeachOutputFunction(PLA, SoDoEspresso, SoSave);
        else
            ForeachOutputFunction(PLA, SoDoExact, SoSave);
        PLA.F = _fmin;
    }

    public static void SoBothEspresso(Pla PLA, int strategy)
    {
        _phase = SetOps.SetSave(CubeContext.FullSet);
        _fmin  = SetFamily.SfNew(PLA.F!.Count, CubeContext.Size);
        if (strategy == 0)
            ForeachOutputFunction(PLA, SoBothDoEspresso, SoBothSave);
        else
            ForeachOutputFunction(PLA, SoBothDoExact, SoBothSave);
        PLA.F   = _fmin;
        PLA.Phase = _phase;
    }

    public static int SoDoEspresso(Pla PLA, int i)
    {
        Globals.SkipMakeSparse = true;
        long t  = Stubs.PTime();
        PLA.F   = Stubs.Espresso(PLA.F!, PLA.D!, PLA.R!);
        if (Globals.Summary) CvrMisc.SizeStamp(PLA.F!, $"ESPRESSO-POS({i})");
        return 1;
    }

    public static int SoDoExact(Pla PLA, int i)
    {
        Globals.SkipMakeSparse = true;
        long t  = Stubs.PTime();
        PLA.F   = Stubs.MinimizeExact(PLA.F!, PLA.D!, PLA.R!, 1);
        if (Globals.Summary) CvrMisc.SizeStamp(PLA.F!, $"EXACT-POS({i})");
        return 1;
    }

    public static int SoSave(Pla PLA, int i)
    {
        _fmin = SetFamily.SfAppend(_fmin!, PLA.F!);
        PLA.F = null;
        return 1;
    }

    public static int SoBothDoEspresso(Pla PLA, int i)
    {
        Globals.SkipMakeSparse = true;
        long t1 = Stubs.PTime();
        PLA.F = Stubs.Espresso(PLA.F!, PLA.D!, PLA.R!);
        if (Globals.Summary) CvrMisc.SizeStamp(PLA.F!, $"ESPRESSO-POS({i})");

        Globals.SkipMakeSparse = true;
        long t2 = Stubs.PTime();
        PLA.R = Stubs.Espresso(PLA.R!, PLA.D!, PLA.F!);
        if (Globals.Summary) CvrMisc.SizeStamp(PLA.R!, $"ESPRESSO-NEG({i})");
        return 1;
    }

    public static int SoBothDoExact(Pla PLA, int i)
    {
        Globals.SkipMakeSparse = true;
        long t1 = Stubs.PTime();
        PLA.F = Stubs.MinimizeExact(PLA.F!, PLA.D!, PLA.R!, 1);
        if (Globals.Summary) CvrMisc.SizeStamp(PLA.F!, $"EXACT-POS({i})");

        Globals.SkipMakeSparse = true;
        long t2 = Stubs.PTime();
        PLA.R = Stubs.MinimizeExact(PLA.R!, PLA.D!, PLA.F!, 1);
        if (Globals.Summary) CvrMisc.SizeStamp(PLA.R!, $"EXACT-NEG({i})");
        return 1;
    }

    public static int SoBothSave(Pla PLA, int i)
    {
        if (PLA.F!.Count > PLA.R!.Count)
        {
            PLA.F = PLA.R;
            PLA.R = null;
            int idx = CubeContext.FirstPart![CubeContext.Output] + i;
            SetOps.SetRemove(_phase, idx);
        }
        else
        {
            PLA.R = null;
        }
        _fmin = SetFamily.SfAppend(_fmin!, PLA.F!);
        PLA.F = null;
        return 1;
    }

    // -----------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------
    private static int CubeListSize(PSet[] T)
    {
        int n = 0;
        for (int i = 2; i < T.Length && !T[i].IsNull; i++) n++;
        return n;
    }
}
