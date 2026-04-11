namespace EspressoCS;

/// <summary>Ports cvrmisc.c — cost computation, trace/stamp printing, fatal.</summary>
public static class CvrMisc
{
    // cover_cost -- compute the cost of a cover
    public static void CoverCost(SetFamily F, Cost cost)
    {
        PSet[] T = Stubs.Cube1List(F);
        Stubs.MassiveCount(T);
        Stubs.FreeCubelist(T);

        cost.Cubes  = F.Count;
        cost.Total  = cost.In = cost.Out = cost.Mv = cost.Primes = 0;

        for (int var = 0; var < CubeContext.NumBinaryVars; var++)
            cost.In += CubeContext.VarZeros![var];

        for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
            if (CubeContext.SparseVar![var] != 0)
                cost.Mv += F.Count * CubeContext.PartSize![var] - CubeContext.VarZeros![var];
            else
                cost.Mv += CubeContext.VarZeros![var];

        if (CubeContext.NumBinaryVars != CubeContext.NumVars)
        {
            int outVar = CubeContext.NumVars - 1;
            cost.Out = F.Count * CubeContext.PartSize![outVar] - CubeContext.VarZeros![outVar];
        }

        for (int si = 0; si < F.Count; si++)
            if (SetOps.TestP(F.GetSet(si), SetOps.Prime)) cost.Primes++;

        cost.Total = cost.In + cost.Out + cost.Mv;
    }

    // fmt_cost -- return a string reporting the cost of a cover
    public static string FmtCost(Cost cost)
    {
        if (CubeContext.NumBinaryVars == CubeContext.NumVars - 1)
            return $"c={cost.Cubes}({cost.Cubes - cost.Primes}) in={cost.In} out={cost.Out} tot={cost.Total}";
        else
            return $"c={cost.Cubes}({cost.Cubes - cost.Primes}) in={cost.In} mv={cost.Mv} out={cost.Out}";
    }

    // print_cost
    public static string PrintCost(SetFamily F)
    {
        var cost = new Cost();
        CoverCost(F, cost);
        return FmtCost(cost);
    }

    // copy_cost
    public static void CopyCost(Cost s, Cost d)
    {
        d.Cubes  = s.Cubes;
        d.In     = s.In;
        d.Out    = s.Out;
        d.Mv     = s.Mv;
        d.Total  = s.Total;
        d.Primes = s.Primes;
    }

    // size_stamp -- print single line giving the size of a cover
    public static void SizeStamp(SetFamily T, string name)
    {
        Console.WriteLine($"# {name}\tCost is {PrintCost(T)}");
        Console.Out.Flush();
    }

    // print_trace -- print a line reporting size and time after a function
    public static void PrintTrace(SetFamily T, string name, long time)
    {
        Console.WriteLine($"# {name}\tTime was {Stubs.PrintTime(time)}, cost is {PrintCost(T)}");
        Console.Out.Flush();
    }

    // totals -- add time spent in the function into the totals
    public static void Totals(long time, int i, SetFamily T, Cost cost)
    {
        time = Stubs.PTime() - time;
        Globals.TotalTime[i]  += time;
        Globals.TotalCalls[i] ++;
        CoverCost(T, cost);
        if (Globals.Trace)
        {
            Console.WriteLine($"# {Globals.TotalName[i]}\tTime was {Stubs.PrintTime(time)}, cost is {FmtCost(cost)}");
            Console.Out.Flush();
        }
    }

    // fatal -- report fatal error message
    public static void Fatal(string s) =>
        throw new InvalidOperationException(s);
}
