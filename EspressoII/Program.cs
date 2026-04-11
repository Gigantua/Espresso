namespace Espresso;
using static BitVectorOps;
using static BitVectorFamily;
public static class Program
{
    public static int Main(string[] args)
    {
        MemoCache.Init();
        bool verifyMode = false;
        int outType = PlaData.FType;
        int argi = 0;
        while (argi < args.Length)
        {
            string arg = args[argi];
            if (arg.Length == 0 || arg[0] != '-' || arg == "-") break;
            argi++;
            if (arg == "--") break;
            switch (arg)
            {
                case "-verify": verifyMode = true; break;
                case "-o":
                    if (argi >= args.Length) Usage();
                    string outputType = args[argi++];
                    if (outputType != "eqntott") Fail<int>($"espresso: bad output type \"{outputType}\"");
                    outType = PlaData.EqntottType;
                    break;
                default: Usage(); break;
            }
        }
        int remaining = args.Length - argi;
        if ((verifyMode && remaining != 2) || (!verifyMode && remaining > 1)) Usage();

        using var stdout = new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding, bufferSize: 1 << 16, leaveOpen: true) { AutoFlush = false };

        if (verifyMode)
        {
            PlaData pla = GetPla(args[argi++], false);
            PlaData pla1 = GetPla(args[argi++], false);
            bool verifyError = VerifyCovers(pla.Cube, pla1.F!, pla.F!, pla.D!);
            Console.WriteLine(verifyError ? "PLA comparison failed; the PLA's are not equivalent" : "PLA's compared equal");
            return verifyError ? 1 : 0;
        }

        // Single file argument: process it once. No argument: read multiple PLAs from stdin.
        bool stdinMode = remaining == 0;
        TextReader? stdinReader = stdinMode ? Console.In : null;
        string? singleFile = stdinMode ? null : args[argi++];

        while (true)
        {
            PlaData pla;
            if (stdinMode)
            {
                if (PlaReader.Read(stdinReader!, true, out PlaData? parsed) == -1 || parsed?.F == null) break;
                pla = parsed;
            }
            else
            {
                pla = GetPla(singleFile, true);
            }

            BitVectorFamily fold = BitVectorFamily.Clone(pla.F!);
            long hitsBefore = MemoCache.Hits;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pla.F = EspressoMinimizer.Minimize(pla.Cube, pla.F!, pla.D!, pla.R!);
            sw.Stop();
            bool minCacheHit = MemoCache.Enabled && MemoCache.Hits > hitsBefore && sw.Elapsed.TotalMilliseconds < 100;
            Console.Error.WriteLine($"[stats] Minimize={sw.Elapsed.TotalMilliseconds:F0}ms{(minCacheHit ? " (cache hit)" : "")}");
            if (MemoCache.Enabled)
                Console.Error.WriteLine($"[cache] entries={MemoCache.EntryCount} hits={MemoCache.Hits} misses={MemoCache.Misses} puts={MemoCache.Puts}");
            MemoCache.Flush();
            if (VerifyCovers(pla.Cube, pla.F, fold, pla.D!)) { pla.F = fold; throw new InvalidOperationException("cover verification failed"); }
            PlaWriter.Write(stdout, pla, outType);
            stdout.Flush();

            if (!stdinMode) break;
        }
        return 0;
    }
    private static PlaData GetPla(string? filename, bool needsOffset)
    {
        TextReader fp;
        if (filename is null or "-") { fp = Console.In; filename = "(stdin)"; }
        else { try { fp = new StreamReader(filename); } catch { return Fail<PlaData>($"espresso: Unable to open {filename}"); } }
        return PlaReader.Read(fp, needsOffset, out PlaData? pla) == -1 || pla == null
            ? Fail<PlaData>($"espresso: Unable to find PLA on file {filename}") : pla;
    }
    private static void Usage()
    {
        Console.WriteLine("EspressoII\n\nUsage:\n  espresso [input.pla]\n  espresso -o eqntott [input.pla]\n  espresso -verify a.pla b.pla\n\nUse '-' or omit the file name to read from standard input.");
        Environment.Exit(1);
    }
    private static T Fail<T>(string message) { Console.Error.WriteLine(message); Environment.Exit(1); throw new InvalidOperationException(message); }
    public static bool VerifyCovers(CubeData cube, BitVectorFamily F, BitVectorFamily Fold, BitVectorFamily Dold)
    {
        bool verifyError = false;
        var stack = new SplitStack(cube.Size, cube.Size);
        CubeList FD = Cofactor.BuildCubeList(cube, Fold, Dold);
        for (int i = 0; i < F.Count; i++)
            if (!Irredundant.IsCubeCovered(cube, FD, F.GetSet(i), stack)) { Console.WriteLine("some minterm in F is not covered by Fold u Dold"); verifyError = true; break; }
        FD.ReturnCubes();
        FD = Cofactor.BuildCubeList(cube, F, Dold);
        for (int i = 0; i < Fold.Count; i++)
            if (!Irredundant.IsCubeCovered(cube, FD, Fold.GetSet(i), stack)) { Console.WriteLine("some minterm in Fold is not covered by F u Dold"); verifyError = true; break; }
        FD.ReturnCubes();
        return verifyError;
    }
}
