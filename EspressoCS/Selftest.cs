namespace EspressoCS;

using System.Security.Cryptography;
using System.Text;

using static SetOps;
using static SetFamily;
using static CubeContext;

/// <summary>
/// Built-in regression selftest.
///
/// For each PLA file in a directory (iterated in sorted order) espresso is
/// run once, then its output is rendered in every supported output format and
/// each rendering is hashed with SHA-256.
///
/// Expected hashes live in &lt;dir&gt;/hash.txt, one line per file:
///   &lt;relative-path&gt;|&lt;format&gt;  &lt;sha256hex&gt;
///
/// Use SelftestGenerate to produce hash.txt, then commit it alongside the
/// test inputs.
/// </summary>
public static class Selftest
{
    private const string HashFileName = "hash.txt";

    private sealed class OutputFormat
    {
        public string Tag { get; }
        public int OutputType { get; }
        public Func<Pla, bool> Supported { get; }

        public OutputFormat(string tag, int outputType, Func<Pla, bool> supported)
        {
            Tag = tag;
            OutputType = outputType;
            Supported = supported;
        }
    }

    private static readonly OutputFormat[] OutputFormats =
    {
        new("f",        Pla.FType,                     _ => true),
        new("r",        Pla.RType,                     _ => true),
        new("d",        Pla.DType,                     _ => true),
        new("fd",       Pla.FdType,                    _ => true),
        new("fr",       Pla.FrType,                    _ => true),
        new("dr",       Pla.DrType,                    _ => true),
        new("fdr",      Pla.FdrType,                   _ => true),
        new("fc",       Pla.FType  | Pla.ConstraintsType, _ => true),
        new("rc",       Pla.RType  | Pla.ConstraintsType, _ => true),
        new("dc",       Pla.DType  | Pla.ConstraintsType, _ => true),
        new("fdc",      Pla.FdType | Pla.ConstraintsType, _ => true),
        new("frc",      Pla.FrType | Pla.ConstraintsType, _ => true),
        new("drc",      Pla.DrType | Pla.ConstraintsType, _ => true),
        new("fdrc",     Pla.FdrType | Pla.ConstraintsType, _ => true),
        new("cons",     Pla.ConstraintsType,          _ => true),
        new("scons",    Pla.SymbolicConstraintsType,  _ => true),
        new("pleasure", Pla.PleasureType,             _ => true),
        new("eqntott",  Pla.EqntottType,              _ => CubeContext.Output != -1 && CubeContext.NumMvVars == 1),
        new("kiss",     Pla.KissType,                 SupportsKissOutput),
    };

    private static string NormalizeHashedOutput(string text)
    {
        return text.Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------
    // Global reset (mirrors reset_espresso_globals in selftest.c)
    // ------------------------------------------------------------------

    private static void ResetEspressoGlobals()
    {
        Globals.Debug                 = 0;
        Globals.VerboseDebug          = false;
        Globals.EchoComments          = false;
        Globals.EchoUnknownCommands   = true;
        Globals.ForceIrredundant      = true;
        Globals.SkipMakeSparse        = false;
        Globals.Kiss                  = false;
        Globals.Pos                   = false;
        Globals.PrintSolution         = true;
        Globals.RecomputeOnset        = false;
        Globals.RemoveEssential       = true;
        Globals.SingleExpand          = false;
        Globals.Summary               = false;
        Globals.Trace                 = false;
        Globals.UnwrapOnset           = true;
        Globals.UseRandomOrder        = false;
        Globals.UseSuperGasp          = false;
    }

    // ------------------------------------------------------------------
    private static bool CubeSupportsKissOutput(SetFamily family)
    {
        for (int si = 0; si < family.Count; si++)
        {
            var p = family.GetSet(si);
            for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
            {
                if (SetOps.SetpImplies(CubeContext.VarMask![var], p))
                    continue;

                int part = -1;
                for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                {
                    if (SetOps.IsInSet(p, i))
                    {
                        if (part != -1)
                            return false;
                        part = i;
                    }
                }
            }
        }
        return true;
    }

    private static bool SupportsKissOutput(Pla pla) =>
        CubeSupportsKissOutput(pla.F!) && CubeSupportsKissOutput(pla.D!);

    private static string MakeFormatKey(string relKey, string formatTag) =>
        $"{relKey}|{formatTag}";

    private static string HashRenderedPla(Pla pla, int outputType)
    {
        var sb = new StringBuilder();
        using (var sw = new StringWriter(sb))
            CvrOut.FprintPla(sw, pla, outputType);

        string normalized = NormalizeHashedOutput(sb.ToString());
        byte[] outputBytes = Encoding.UTF8.GetBytes(normalized);
        byte[] digest = SHA256.HashData(outputBytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    // Run espresso on one PLA file and prepare the minimized PLA.
    // Returns:
    //   0  – success
    //  -1  – I/O or parse error
    //   1  – verify step detected an inconsistency
    // ------------------------------------------------------------------

    private static int PrepareOneFile(string path, out Pla? pla, out bool verifyFailed)
    {
        pla = null;
        verifyFailed = false;
        ResetEspressoGlobals();

        TextReader? fp;
        try { fp = new StreamReader(path); }
        catch
        {
            Console.Error.WriteLine($"selftest: cannot open {path}");
            return -1;
        }

        int rc = CvrIn.ReadPla(fp, true, true, Pla.FdType, out pla);
        fp.Close();

        if (rc == -1 || pla == null)
        {
            Console.Error.WriteLine($"selftest: cannot read PLA from {path}");
            return -1;
        }

        pla.Filename = path;
        Globals.Filename = pla.Filename;

        SetFamily Fold = SfSave(pla.F!);
        pla.F = EspressoAlgo.Espresso(pla.F!, pla.D!, pla.R!);

        long tStart = Stubs.PTime();
        bool error = Verify.VerifyCovers(pla.F, Fold, pla.D!);
        long tEnd = Stubs.PTime();
        Globals.TotalTime[Globals.VerifyTime]  += tEnd - tStart;
        Globals.TotalCalls[Globals.VerifyTime] += 1;

        if (error)
        {
            pla.F = Fold;
        }
        verifyFailed = error;
        return error ? 1 : 0;
    }

    // ------------------------------------------------------------------
    // File collection: recursively gather all files under dir, sorted.
    // ------------------------------------------------------------------

    private static List<string> CollectFiles(string dir)
    {
        var files = new List<string>();
        CollectFilesRecursive(dir, files);
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static void CollectFilesRecursive(string dir, List<string> files)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
        {
            if (Directory.Exists(entry))
                CollectFilesRecursive(entry, files);
            else
                files.Add(entry);
        }
    }

    // ------------------------------------------------------------------
    // Build the hash.txt key: path relative to baseDir with forward slashes.
    // ------------------------------------------------------------------

    private static string MakeRelKey(string baseDir, string path)
    {
        // Normalise separators so we can do a reliable prefix strip.
        string normBase = baseDir.Replace('\\', '/').TrimEnd('/');
        string normPath = path.Replace('\\', '/');

        if (normPath.StartsWith(normBase + "/", StringComparison.Ordinal))
            normPath = normPath[(normBase.Length + 1)..];

        return normPath;
    }

    // ------------------------------------------------------------------
    // Hash-file helpers
    // ------------------------------------------------------------------

    private static Dictionary<string, string> LoadHashFile(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return dict;

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            int sep = line.IndexOf(' ');
            if (sep < 0) continue;

            string name = line[..sep];
            string hash = line[(sep + 1)..].TrimStart();
            if (hash.Length != 64) continue;

            dict[name] = hash;
        }
        return dict;
    }

    // ------------------------------------------------------------------
    // Public entry points
    // ------------------------------------------------------------------

    /// <summary>
    /// Run espresso on every PLA file in <paramref name="dir"/> and compare
    /// SHA-256 hashes against the expected values in hash.txt.
    /// </summary>
    /// <returns><c>true</c> on full success, <c>false</c> if any test failed.</returns>
    public static bool SelftestRun(string dir)
    {
        if (string.IsNullOrEmpty(dir)) dir = "tests";

        string hashPath = Path.Combine(dir, HashFileName);
        var ht = LoadHashFile(hashPath);
        if (ht.Count == 0)
        {
            Console.Error.WriteLine(
                $"selftest: cannot load '{hashPath}'\n" +
                $"  Run: espresso -selftest generate {dir}");
            return false;
        }

        List<string> files = CollectFiles(dir);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"selftest: no files found in '{dir}'");
            return false;
        }

        Console.WriteLine($"Selftest: directory '{dir}'\n");

        int passed = 0, failed = 0;
        long tStart = Stubs.PTime();

        foreach (string path in files)
        {
            string relKey = MakeRelKey(dir, path);
            if (string.Equals(Path.GetFileName(path), HashFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            int rc = PrepareOneFile(path, out Pla? pla, out bool verifyFailed);
            if (rc < 0 || pla == null)
            {
                Console.WriteLine($"  [ERROR ] {relKey}");
                failed++;
            }
            else
            {
                foreach (var format in OutputFormats)
                {
                    if (!format.Supported(pla))
                        continue;

                    string formatKey = MakeFormatKey(relKey, format.Tag);
                    string fileHex = HashRenderedPla(pla, format.OutputType);
                    ht.TryGetValue(formatKey, out string? expected);

                    if (expected == null)
                    {
                        Console.WriteLine($"  [UNKNWN] {formatKey,-44}  {fileHex}  (not in {HashFileName})");
                        failed++;
                    }
                    else if (verifyFailed)
                    {
                        Console.WriteLine($"  [VERIFY] {formatKey,-44}  {fileHex}  (verify failed)");
                        failed++;
                    }
                    else if (fileHex != expected)
                    {
                        Console.WriteLine($"  [HASH  ] {formatKey,-44}  {fileHex}");
                        Console.WriteLine($"           {"",-44}  {expected}  (expected)");
                        failed++;
                    }
                    else
                    {
                        Console.WriteLine($"  [OK    ] {formatKey,-44}  {fileHex}");
                        passed++;
                    }
                }

                Pla.FreePla(pla);
                SetdownCube();
            }
        }

        Console.WriteLine($"\nResults : {passed} passed, {failed} failed");
        Console.WriteLine($"Time    : {Stubs.PrintTime(Stubs.PTime() - tStart)}\n");

        return failed == 0;
    }

    /// <summary>
    /// Run espresso on every PLA file in <paramref name="dir"/> and write
    /// the computed SHA-256 hashes to hash.txt.
    /// </summary>
    /// <returns><c>true</c> on success, <c>false</c> if any file failed.</returns>
    public static bool SelftestGenerate(string dir)
    {
        if (string.IsNullOrEmpty(dir)) dir = "tests";

        List<string> files = CollectFiles(dir);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"selftest: no files found in '{dir}'");
            return false;
        }

        string hashPath = Path.Combine(dir, HashFileName);
        StreamWriter? outWriter;
        try { outWriter = new StreamWriter(hashPath, append: false); }
        catch
        {
            Console.Error.WriteLine($"selftest: cannot write '{hashPath}'");
            return false;
        }

        Console.WriteLine($"Generating hashes for files in '{dir}'...\n");

        int ok = 0, errors = 0;
        long tStart = Stubs.PTime();

        using (outWriter)
        {
        foreach (string path in files)
        {
            string relKey = MakeRelKey(dir, path);
            if (string.Equals(Path.GetFileName(path), HashFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            int rc = PrepareOneFile(path, out Pla? pla, out bool verifyFailed);
            if (rc < 0 || pla == null)
                {
                    Console.WriteLine($"  [ERROR ] {relKey}");
                    errors++;
                }
                else
                {
                    foreach (var format in OutputFormats)
                    {
                        if (!format.Supported(pla))
                            continue;

                        string formatKey = MakeFormatKey(relKey, format.Tag);
                        string fileHex = HashRenderedPla(pla, format.OutputType);
                        outWriter.WriteLine($"{formatKey}  {fileHex}");
                        Console.WriteLine($"  [HASHED] {formatKey,-44}  {fileHex}");
                        ok++;
                    }

                    Pla.FreePla(pla);
                    SetdownCube();
                }
            }
        }

        Console.WriteLine($"\nGenerated: {ok} hash(es), {errors} error(s)");
        Console.WriteLine($"Hash file: {hashPath}");
        Console.WriteLine($"Time     : {Stubs.PrintTime(Stubs.PTime() - tStart)}\n");

        return errors == 0;
    }
}
