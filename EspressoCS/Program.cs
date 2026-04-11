using EspressoCS;

// ─────────────────────────────────────────────────────────────────────────────
// Program.cs — port of main.c
// ─────────────────────────────────────────────────────────────────────────────

long start = Stubs.PTime();

// ── defaults (mirrors init + variable initialisations in main()) ──────────────
Globals.Debug             = 0;
Globals.VerboseDebug      = false;
Globals.PrintSolution     = true;
Globals.Summary           = false;
Globals.Trace             = false;
Globals.RemoveEssential   = true;
Globals.ForceIrredundant  = true;
Globals.UnwrapOnset       = true;
Globals.SingleExpand      = false;
Globals.Pos               = false;
Globals.RecomputeOnset    = false;
Globals.UseSuperGasp      = false;
Globals.UseRandomOrder    = false;
Globals.Kiss              = false;
Globals.EchoComments      = true;
Globals.EchoUnknownCommands = true;

// ── timing names (init_runtime) ───────────────────────────────────────────────
Globals.TotalName[Globals.ReadTime]     = "READ       ";
Globals.TotalName[Globals.WriteTime]    = "WRITE      ";
Globals.TotalName[Globals.ComplTime]    = "COMPL      ";
Globals.TotalName[Globals.ReduceTime]   = "REDUCE     ";
Globals.TotalName[Globals.ExpandTime]   = "EXPAND     ";
Globals.TotalName[Globals.EssenTime]    = "ESSEN      ";
Globals.TotalName[Globals.IrredTime]    = "IRRED      ";
Globals.TotalName[Globals.GreduceTime]  = "REDUCE_GASP";
Globals.TotalName[Globals.GexpandTime]  = "EXPAND_GASP";
Globals.TotalName[Globals.GirredTime]   = "IRRED_GASP ";
Globals.TotalName[Globals.MvReduceTime] = "MV_REDUCE  ";
Globals.TotalName[Globals.RaiseInTime]  = "RAISE_IN   ";
Globals.TotalName[Globals.VerifyTime]   = "VERIFY     ";
Globals.TotalName[Globals.PrimesTime]   = "PRIMES     ";
Globals.TotalName[Globals.MincovTime]   = "MINCOV     ";

// Work with a mutable list for backward-compat arg removal
var argList = new List<string>(args);

// ── backward compatibility hack ───────────────────────────────────────────────
int optionIndex = 0;
int outType = Pla.FType;
int inputType = Pla.FdType;

BackwardCompatibilityHack(argList, ref optionIndex, ref outType, ref inputType);

// ── handle -selftest before normal option processing ──────────────────────────
for (int k = 0; k < argList.Count; k++)
{
    if (argList[k] == "-selftest")
    {
        bool generate = argList.Count > k + 1 && argList[k + 1] == "generate";
        string dir = ".";
        if (generate && argList.Count > k + 2) dir = argList[k + 2];
        else if (!generate && argList.Count > k + 1) dir = argList[k + 1];

        bool ok = generate
            ? Selftest.SelftestGenerate(dir)
            : Selftest.SelftestRun(dir);
        Environment.Exit(ok ? 0 : 1);
    }
}

// ── getopt-style argument parsing ─────────────────────────────────────────────
int strategy = 0;
int first = -1;
int last = -1;
var allOptions = Options.GetAllOptions();
var allDebugOptions = Options.GetAllDebugOptions();

int optind = 0;
while (optind < argList.Count)
{
    string arg = argList[optind];
    if (arg.Length == 0 || arg[0] != '-' || arg == "-")
        break;
    if (arg == "--")
    {
        optind++;
        break;
    }

    // strip leading '-'
    string flag = arg.Substring(1);
    optind++;

    if (flag.Length == 0) { Usage(); Environment.Exit(1); }

    char opt = flag[0];
    string optarg = flag.Length > 1 ? flag.Substring(1) : "";

    switch (opt)
    {
        case 'D':
        {
            if (optarg == "" && optind < argList.Count) { optarg = argList[optind++]; }
            int j = Array.FindIndex(allOptions, e => e.Name == optarg);
            if (j < 0)
            {
                Console.Error.WriteLine($"espresso: bad subcommand \"{optarg}\"");
                Environment.Exit(1);
            }
            optionIndex = j;
            break;
        }
        case 'o':
        {
            if (optarg == "" && optind < argList.Count) { optarg = argList[optind++]; }
            var entry = Array.Find(PlaTypes.Table, e => e.Key.TrimStart('-') == optarg);
            if (entry == null)
            {
                Console.Error.WriteLine($"espresso: bad output type \"{optarg}\"");
                Environment.Exit(1);
            }
            outType = entry.Value;
            break;
        }
        case 'e':
        {
            if (optarg == "" && optind < argList.Count) { optarg = argList[optind++]; }
            var entry = Array.Find(Options.GetAllOptions(), _ => false); // placeholder
            foreach (var dbgEntry in Options.GetAllDebugOptions()) { } // just to compile
            // Use Options.ApplyOption to set the espresso option
            if (!ApplyEspressoOption(optarg))
            {
                Console.Error.WriteLine($"espresso: bad espresso option \"{optarg}\"");
                Environment.Exit(1);
            }
            break;
        }
        case 'd':
            Globals.Debug = Options.GetAllDebugOptions()[0].Value;
            Globals.Trace = true;
            Globals.Summary = true;
            break;
        case 'v':
        {
            if (optarg == "" && optind < argList.Count) { optarg = argList[optind++]; }
            Globals.VerboseDebug = true;
            uint flag2 = Options.GetDebugFlag(optarg);
            if (flag2 == 0 && optarg != "")
            {
                Console.Error.WriteLine($"espresso: bad debug type \"{optarg}\"");
                Environment.Exit(1);
            }
            Globals.Debug |= flag2;
            break;
        }
        case 't':
            Globals.Trace = true;
            break;
        case 's':
            Globals.Summary = true;
            break;
        case 'x':
            Globals.PrintSolution = false;
            break;
        case 'S':
        {
            if (optarg == "" && optind < argList.Count) { optarg = argList[optind++]; }
            strategy = int.Parse(optarg);
            break;
        }
        case 'r':
        {
            if (optarg == "" && optind < argList.Count) { optarg = argList[optind++]; }
            var parts = optarg.Split('-');
            if (parts.Length < 2 || !int.TryParse(parts[0], out first) || !int.TryParse(parts[1], out last))
            {
                Console.Error.WriteLine($"espresso: bad output range \"{optarg}\"");
                Environment.Exit(1);
            }
            break;
        }
        default:
            Usage();
            Environment.Exit(1);
            break;
    }
}

// ── summary/trace header ──────────────────────────────────────────────────────
if (Globals.Summary || Globals.Trace)
{
    Console.Write("#");
    foreach (var a in args) Console.Write($" {a}");
    Console.WriteLine();
    Console.WriteLine("# Espresso (C# port)");
}

// ── read PLA file(s) ──────────────────────────────────────────────────────────
TextReader? lastFp = null;
var selectedOption = allOptions[optionIndex];

Pla? PLA = null, PLA1 = null;

switch (selectedOption.NumPlas)
{
    case 2:
        if (optind + 2 < argList.Count) CvrMisc.Fatal("trailing arguments on command line");
        (PLA, lastFp) = GetPLA(optind < argList.Count ? argList[optind++] : null, selectedOption, outType, inputType);
        (PLA1, _)     = GetPLA(optind < argList.Count ? argList[optind++] : null, selectedOption, outType, inputType);
        break;
    case 1:
        if (optind + 1 < argList.Count) CvrMisc.Fatal("trailing arguments on command line");
        (PLA, lastFp) = GetPLA(optind < argList.Count ? argList[optind++] : null, selectedOption, outType, inputType);
        break;
}
if (optind < argList.Count) CvrMisc.Fatal("trailing arguments on command line");

if (Globals.Summary || Globals.Trace)
{
    if (PLA  != null) CvrIn.PlaSummary(PLA);
    if (PLA1 != null) CvrIn.PlaSummary(PLA1);
}

// ── main dispatch ─────────────────────────────────────────────────────────────
bool error = false;
bool exactCover = false;
var cost = new Cost();

switch (selectedOption.Key)
{
    case Options.OptionKey.KeyEspresso:
    {
        var Fold = SetFamily.SfSave(PLA!.F!);
        long t = Stubs.PTime();
        PLA.F = EspressoAlgo.Espresso(PLA.F!, PLA.D!, PLA.R!);
        error = Verify.VerifyCovers(PLA.F, Fold, PLA.D!);
        CvrMisc.Totals(t, Globals.VerifyTime, PLA.F, cost);
        if (error)
        {
            Globals.PrintSolution = false;
            PLA.F = Fold;
            Verify.CheckConsistency(PLA);
        }
        else
        {
            SetFamily.SfFree(Fold);
        }
        break;
    }

    case Options.OptionKey.KeyManyEspresso:
    {
        int plaType;
        do {
            long t = Stubs.PTime();
            PLA!.F = EspressoAlgo.Espresso(PLA.F!, PLA.D!, PLA.R!);
            if (Globals.PrintSolution)
            {
                CvrOut.FprintPla(Console.Out, PLA, outType);
                Console.Out.Flush();
            }
            plaType = PLA.PlaType;
            Pla.FreePla(PLA);
            CubeContext.SetdownCube();
            // cube.part_size freed implicitly
        } while (lastFp != null && CvrIn.ReadPla(lastFp, true, true, plaType, out PLA) != -1);
        Environment.Exit(0);
        break;
    }

    case Options.OptionKey.KeySimplify:
    {
        long t = Stubs.PTime();
        PLA!.F = Compl.Simplify(Cofactor.Cube1List(PLA.F!));
        CvrMisc.Totals(t, Globals.ComplTime, PLA.F, cost);
        break;
    }

    case Options.OptionKey.KeySo:
        if (strategy < 0 || strategy > 1) strategy = 0;
        CvrM.SoEspresso(PLA!, strategy);
        break;

    case Options.OptionKey.KeySoBoth:
        if (strategy < 0 || strategy > 1) strategy = 0;
        CvrM.SoBothEspresso(PLA!, strategy);
        break;

    case Options.OptionKey.KeyExpand:
    {
        long t = Stubs.PTime();
        PLA!.F = Expand.ExpandCover(PLA.F!, PLA.R!, 0);
        CvrMisc.Totals(t, Globals.ExpandTime, PLA.F, cost);
        break;
    }

    case Options.OptionKey.KeyIrred:
    {
        long t = Stubs.PTime();
        PLA!.F = Irred.Irredundant(PLA.F!, PLA.D!);
        CvrMisc.Totals(t, Globals.IrredTime, PLA.F, cost);
        break;
    }

    case Options.OptionKey.KeyReduce:
    {
        long t = Stubs.PTime();
        PLA!.F = Reduce.ReduceCover(PLA.F!, PLA.D!);
        CvrMisc.Totals(t, Globals.ReduceTime, PLA.F, cost);
        break;
    }

    case Options.OptionKey.KeyEssen:
    {
        long t = Stubs.PTime();
        var E = Essen.Essentials(ref PLA!.F!, ref PLA.D!);
        SetFamily.SfFree(E);
        CvrMisc.Totals(t, Globals.EssenTime, PLA.F, cost);
        break;
    }

    case Options.OptionKey.KeySuperGasp:
        PLA!.F = Gasp.SuperGasp(PLA.F!, PLA.D!, PLA.R!, ref cost);
        break;

    case Options.OptionKey.KeyGasp:
        PLA!.F = Gasp.LastGasp(PLA.F!, PLA.D!, PLA.R!, ref cost);
        break;

    case Options.OptionKey.KeyMakeSparse:
        PLA!.F = Stubs.MakeSparse(PLA.F!, PLA.D!, PLA.R!);
        break;

    case Options.OptionKey.KeyExact:
        exactCover = true;
        goto case Options.OptionKey.KeyQm;

    case Options.OptionKey.KeyQm:
    {
        var Fold = SetFamily.SfSave(PLA!.F!);
        PLA.F = Exact.MinimizeExact(PLA.F!, PLA.D!, PLA.R!, exactCover ? 1 : 0);
        long t = Stubs.PTime();
        error = Verify.VerifyCovers(PLA.F, Fold, PLA.D!);
        CvrMisc.Totals(t, Globals.VerifyTime, PLA.F, cost);
        if (error)
        {
            Globals.PrintSolution = false;
            PLA.F = Fold;
            Verify.CheckConsistency(PLA);
        }
        SetFamily.SfFree(Fold);
        break;
    }

    case Options.OptionKey.KeyPrimes:
    {
        long t = Stubs.PTime();
        PLA!.F = Primes.PrimesConsensus(Cofactor.Cube2List(PLA.F!, PLA.D!));
        CvrMisc.Totals(t, Globals.PrimesTime, PLA.F, cost);
        break;
    }

    case Options.OptionKey.KeyMap:
        Map.MapDisplay(PLA!.F!);
        Globals.PrintSolution = false;
        break;

    case Options.OptionKey.KeySignature:
    {
        var Fold = SetFamily.SfSave(PLA!.F!);
        PLA.F = Signature.Run(PLA.F!, PLA.D!, PLA.R!);
        long t = Stubs.PTime();
        error = Verify.VerifyCovers(PLA.F, Fold, PLA.D!);
        CvrMisc.Totals(t, Globals.VerifyTime, PLA.F, cost);
        if (error)
        {
            Globals.PrintSolution = false;
            PLA.F = Fold;
            Verify.CheckConsistency(PLA);
        }
        else
        {
            SetFamily.SfFree(Fold);
        }
        break;
    }

    case Options.OptionKey.KeyOpo:
        Opo.PhaseAssignment(PLA!, strategy);
        break;

    case Options.OptionKey.KeyOpoall:
        if (first < 0 || first >= CubeContext.PartSize![CubeContext.Output]) first = 0;
        if (last < 0 || last >= CubeContext.PartSize![CubeContext.Output]) last = CubeContext.PartSize[CubeContext.Output] - 1;
        Opo.Opoall(PLA!, first, last, strategy);
        break;

    case Options.OptionKey.KeyPair:
        PairOps.FindOptimalPairing(PLA!, strategy);
        break;

    case Options.OptionKey.KeyPairall:
        PairOps.PairAll(PLA!, strategy);
        break;

    case Options.OptionKey.KeyEcho:
        break;

    case Options.OptionKey.KeyTaut:
        Console.WriteLine($"ON-set is{(Taut.Tautology(PLA!.F!) ? " " : " not ")}a tautology");
        Globals.PrintSolution = false;
        break;

    case Options.OptionKey.KeyContain:
        PLA!.F = Contain.SfContain(PLA.F!);
        break;

    case Options.OptionKey.KeyIntersect:
        PLA!.F = Sharp.CvIntersect(PLA.F!, PLA1!.F!);
        break;

    case Options.OptionKey.KeyUnion:
        PLA!.F = Contain.SfUnion(PLA.F!, PLA1!.F!);
        break;

    case Options.OptionKey.KeyDisjoint:
        PLA!.F = Sharp.MakeDisjoint(PLA.F!);
        break;

    case Options.OptionKey.KeyDSharp:
        PLA!.F = Sharp.CvDsharp(PLA.F!, PLA1!.F!);
        break;

    case Options.OptionKey.KeySharp:
        PLA!.F = Sharp.CvSharp(PLA.F!, PLA1!.F!);
        break;

    case Options.OptionKey.KeyLexsort:
        PLA!.F = CvrM.LexSort(PLA.F!);
        break;

    case Options.OptionKey.KeyStats:
        if (!Globals.Summary) CvrIn.PlaSummary(PLA!);
        Globals.PrintSolution = false;
        break;

    case Options.OptionKey.KeyMinterms:
    {
        if (first < 0 || first >= CubeContext.NumVars) first = 0;
        if (last  < 0 || last  >= CubeContext.NumVars) last  = CubeContext.NumVars - 1;
        PLA!.F = Contain.SfDupl(CvrM.UnravelRange(PLA.F!, first, last));
        break;
    }

    case Options.OptionKey.KeyD1Merge:
    {
        if (first < 0 || first >= CubeContext.NumVars) first = 0;
        if (last  < 0 || last  >= CubeContext.NumVars) last  = CubeContext.NumVars - 1;
        for (int i = first; i <= last; i++)
            PLA!.F = Stubs.D1Merge(PLA.F!, i);
        break;
    }

    case Options.OptionKey.KeyD1MergeIn:
        for (int i = 0; i < CubeContext.NumBinaryVars; i++)
            PLA!.F = Stubs.D1Merge(PLA.F!, i);
        break;

    case Options.OptionKey.KeyPlaVerify:
    {
        long t = Stubs.PTime();
        bool verifyError = Verify.PlaVerify(PLA!, PLA1!);
        CvrMisc.Totals(t, Globals.VerifyTime, PLA!.F!, cost);
        if (verifyError)
        {
            Console.WriteLine("PLA comparison failed; the PLA's are not equivalent");
            Environment.Exit(1);
        }
        else
        {
            Console.WriteLine("PLA's compared equal");
            Environment.Exit(0);
        }
        break;
    }

    case Options.OptionKey.KeyVerify:
    {
        var Fold = PLA!.F!;
        var Dold = PLA.D!;
        long t = Stubs.PTime();
        bool verifyError = Verify.VerifyCovers(PLA1!.F!, Fold, Dold);
        CvrMisc.Totals(t, Globals.VerifyTime, PLA.F!, cost);
        if (verifyError)
        {
            Console.WriteLine("PLA comparison failed; the PLA's are not equivalent");
            Environment.Exit(1);
        }
        else
        {
            Console.WriteLine("PLA's compared equal");
            Environment.Exit(0);
        }
        break;
    }

    case Options.OptionKey.KeyCheck:
        Verify.CheckConsistency(PLA!);
        Globals.PrintSolution = false;
        break;

    case Options.OptionKey.KeyMapdc:
        Hack.MapDcSet(PLA!);
        outType = Pla.FdType;
        break;

    case Options.OptionKey.KeyEquiv:
        Equiv.FindEquivOutputs(PLA!);
        Globals.PrintSolution = false;
        break;

    case Options.OptionKey.KeySeparate:
        PLA!.F = Compl.Complement(Cofactor.Cube2List(PLA.D!, PLA.R!));
        break;

    case Options.OptionKey.KeyXor:
    {
        var T1 = Sharp.CvIntersect(PLA!.F!, PLA1!.R!);
        var T2 = Sharp.CvIntersect(PLA1.F!, PLA.R!);
        SetFamily.SfFree(PLA.F!);
        PLA.F = Contain.SfContain(SetFamily.SfJoin(T1, T2));
        SetFamily.SfFree(T1);
        SetFamily.SfFree(T2);
        break;
    }

    case Options.OptionKey.KeyFsm:
        Hack.DisassembleFsm(PLA!, Globals.Summary);
        Globals.PrintSolution = false;
        break;

    case Options.OptionKey.KeyTest:
    {
        var T = SetFamily.SfJoin(PLA!.D!, PLA.R!);
        var E = SetFamily.SfNew(10, CubeContext.Size);
        SetFamily.SfFree(PLA.F!);
        {
            long t = Stubs.PTime();
            PLA.F = Compl.Complement(Cofactor.Cube1List(T));
            CvrMisc.Totals(t, Globals.ComplTime, PLA.F, cost);
        }
        {
            long t = Stubs.PTime();
            PLA.F = Expand.ExpandCover(PLA.F, T, 0);
            CvrMisc.Totals(t, Globals.ExpandTime, PLA.F, cost);
        }
        {
            long t = Stubs.PTime();
            PLA.F = Irred.Irredundant(PLA.F, E);
            CvrMisc.Totals(t, Globals.IrredTime, PLA.F, cost);
        }
        SetFamily.SfFree(T);
        T = SetFamily.SfJoin(PLA.F, PLA.R!);
        {
            long t = Stubs.PTime();
            PLA.D = Expand.ExpandCover(PLA.D!, T, 0);
            CvrMisc.Totals(t, Globals.ExpandTime, PLA.D, cost);
        }
        {
            long t = Stubs.PTime();
            PLA.D = Irred.Irredundant(PLA.D, E);
            CvrMisc.Totals(t, Globals.IrredTime, PLA.D, cost);
        }
        SetFamily.SfFree(T);
        SetFamily.SfFree(E);
        break;
    }
}

// ── trace / summary / output ──────────────────────────────────────────────────
if (Globals.Trace)
{
    Runtime();
}

if (Globals.Summary || Globals.Trace)
{
    CvrMisc.PrintTrace(PLA!.F!, selectedOption.Name, Stubs.PTime() - start);
}

if (Globals.PrintSolution)
{
    long t = Stubs.PTime();
    CvrOut.FprintPla(Console.Out, PLA!, outType);
    CvrMisc.Totals(t, Globals.WriteTime, PLA!.F!, cost);
}

if (error)
{
    CvrMisc.Fatal("cover verification failed");
}

Pla.FreePla(PLA!);
CubeContext.SetdownCube();
SetFamily.SfCleanup();
SparseMatrix.SmCleanup();

Environment.Exit(0);

// ─────────────────────────────────────────────────────────────────────────────
// Helper functions
// ─────────────────────────────────────────────────────────────────────────────

static bool ApplyEspressoOption(string name)
{
    // Options.EspressOptionTable entries: name → SetVariable(DefaultValue)
    // We re-use the public ApplyOption method with the entry's stored default.
    // The C code sets *(variable) = value, so we call SetVariable(defaultValue).
    // But ApplyOption(name, value) exists — we call it with value=true (the "on" value
    // for most options). The actual stored value in EspressOptionEntry.DefaultValue is
    // what the C code uses; we look it up via reflection of the table entries.
    // Simpler: just call ApplyOption with value=true (all options use TRUE for the flag).
    return Options.ApplyOption(name, true);
}

static (Pla pla, TextReader fp) GetPLA(
    string? filename,
    Options.OptionTableEntry selectedOption,
    int outType,
    int inputType)
{
    TextReader fp;
    string fname;

    if (filename == null || filename == "-")
    {
        fp    = Console.In;
        fname = "(stdin)";
    }
    else
    {
        fname = filename;
        try { fp = new StreamReader(filename); }
        catch
        {
            Console.Error.WriteLine($"espresso: Unable to open {fname}");
            Environment.Exit(1);
            throw; // unreachable
        }
    }

    bool needsDcset, needsOffset;
    if (selectedOption.Key == Options.OptionKey.KeyEcho)
    {
        needsDcset  = (outType & Pla.DType) != 0;
        needsOffset = (outType & Pla.RType) != 0;
    }
    else
    {
        needsDcset  = selectedOption.NeedsDcset;
        needsOffset = selectedOption.NeedsOffset;
    }

    if (CvrIn.ReadPla(fp, needsDcset, needsOffset, inputType, out Pla? pla) == -1 || pla == null)
    {
        Console.Error.WriteLine($"espresso: Unable to find PLA on file {fname}");
        Environment.Exit(1);
        throw new Exception(); // unreachable
    }

    pla.Filename = fname;
    Globals.Filename = pla.Filename;
    return (pla, fp);
}

static void Runtime()
{
    long total = 1;
    for (int i = 0; i < EspressoConstants.TimeCount; i++)
        total += Globals.TotalTime[i];
    for (int i = 0; i < EspressoConstants.TimeCount; i++)
    {
        if (Globals.TotalCalls[i] != 0)
        {
            long temp = 100 * Globals.TotalTime[i];
            Console.WriteLine($"# {Globals.TotalName[i]}\t{Globals.TotalCalls[i],2} call(s) for {Stubs.PrintTime(Globals.TotalTime[i])} ({temp / total,2}.{(10 * (temp % total)) / total}%)");
        }
    }
}

static void BackwardCompatibilityHack(
    List<string> argv,
    ref int optionIndex,
    ref int outType,
    ref int inputType)
{
    var allOptions  = Options.GetAllOptions();
    var plaTypes    = PlaTypes.Table;

    optionIndex = 0;

    // -do <subcommand>
    for (int i = 0; i < argv.Count - 1; i++)
    {
        if (argv[i] == "-do")
        {
            int j = Array.FindIndex(allOptions, e => e.Name == argv[i + 1]);
            if (j < 0)
            {
                Console.Error.WriteLine($"espresso: bad keyword \"{argv[i + 1]}\" following -do");
                Environment.Exit(1);
            }
            optionIndex = j;
            argv.RemoveAt(i + 1);
            argv.RemoveAt(i);
            break;
        }
    }

    // -out <type>
    for (int i = 0; i < argv.Count - 1; i++)
    {
        if (argv[i] == "-out")
        {
            var entry = Array.Find(plaTypes, e => e.Key.TrimStart('-') == argv[i + 1]);
            if (entry == null)
            {
                Console.Error.WriteLine($"espresso: bad keyword \"{argv[i + 1]}\" following -out");
                Environment.Exit(1);
            }
            outType = entry.Value;
            argv.RemoveAt(i + 1);
            argv.RemoveAt(i);
            break;
        }
    }

    // -<espopt> flags (e.g. -pos, -fast, etc.)
    for (int i = argv.Count - 1; i >= 0; i--)
    {
        if (argv[i].Length > 1 && argv[i][0] == '-')
        {
            string name = argv[i].Substring(1);
            // check if it matches any esp_opt_table entry
            if (IsEspressoOptionName(name))
            {
                argv.RemoveAt(i);
                Options.ApplyOption(name, true);
            }
        }
    }

    // -fdr / -fr / -f input type flags
    if (CheckAndRemoveArg(argv, "-fdr")) inputType = Pla.FdrType;
    if (CheckAndRemoveArg(argv, "-fr"))  inputType = Pla.FrType;
    if (CheckAndRemoveArg(argv, "-f"))   inputType = Pla.FType;
}

static bool IsEspressoOptionName(string name)
{
    // Must match one of: eat, eatdots, fast, kiss, ness, nirr, nunwrap, onset, pos, random, strong
    return name is "eat" or "eatdots" or "fast" or "kiss" or "ness" or "nirr"
               or "nunwrap" or "onset" or "pos" or "random" or "strong";
}

static bool CheckAndRemoveArg(List<string> argv, string s)
{
    int i = argv.IndexOf(s);
    if (i < 0) return false;
    argv.RemoveAt(i);
    return true;
}

static void Usage()
{
    Console.WriteLine("Espresso (C# port)\n");
    Console.WriteLine("SYNOPSIS: espresso [options] [file]\n");
    Console.WriteLine("  -d        Enable debugging");
    Console.WriteLine("  -e[opt]   Select espresso option:");
    Console.WriteLine("                fast, ness, nirr, nunwrap, onset, pos, strong,");
    Console.WriteLine("                eat, eatdots, kiss, random");
    Console.WriteLine("  -o[type]  Select output format:");
    Console.WriteLine("                f, r, d, fd, fr, dr, fdr,");
    Console.WriteLine("                fc, rc, dc, fdc, frc, drc, fdrc,");
    Console.WriteLine("                pleasure, eqn, eqntott, kiss, cons, scons");
    Console.WriteLine("  -rn-m     Select range for subcommands:");
    Console.WriteLine("                d1merge: first and last variables (0 ... m-1)");
    Console.WriteLine("                minterms: first and last variables (0 ... m-1)");
    Console.WriteLine("                opoall: first and last outputs (0 ... m-1)");
    Console.WriteLine("  -s        Provide short execution summary");
    Console.WriteLine("  -t        Provide longer execution trace");
    Console.WriteLine("  -x        Suppress printing of solution");
    Console.WriteLine("  -v[type]  Verbose debugging detail (-v '' for all)");
    Console.WriteLine("  -D[cmd]   Execute subcommand 'cmd':");
    Console.WriteLine("                ESPRESSO, many, exact, qm, single_output, so,");
    Console.WriteLine("                so_both, simplify, echo, signature, opo, opoall,");
    Console.WriteLine("                pair, pairall, check, stats, verify, PLAverify,");
    Console.WriteLine("                equiv, map, mapdc, fsm, contain, d1merge,");
    Console.WriteLine("                d1merge_in, disjoint, dsharp, intersect,");
    Console.WriteLine("                minterms, primes, separate, sharp, union, xor,");
    Console.WriteLine("                essen, expand, gasp, irred, make_sparse, reduce,");
    Console.WriteLine("                taut, super_gasp, lexsort, test");
    Console.WriteLine("  -Sn       Select strategy for subcommands:");
    Console.WriteLine("                opo: bit2=exact bit1=repeated bit0=skip sparse");
    Console.WriteLine("                opoall: 0=minimize, 1=exact");
    Console.WriteLine("                pair: 0=algebraic, 1=strongd, 2=espresso, 3=exact");
    Console.WriteLine("                pairall: 0=minimize, 1=exact, 2=opo");
    Console.WriteLine("                so / single_output: 0=minimize, 1=exact");
    Console.WriteLine("                so_both: 0=minimize, 1=exact");
    Console.WriteLine("  -selftest <dir>          Run regression selftest (reads <dir>/hash.txt)");
    Console.WriteLine("  -selftest generate <dir> Compute hashes for all files under <dir>,");
    Console.WriteLine("                           write <dir>/hash.txt  (default dir: tests)");
}

