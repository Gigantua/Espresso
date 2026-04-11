using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Espresso;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EspressoII.Tests;

[TestClass]
public class BenchmarkTests
{
    public TestContext TestContext { get; set; } = null!;

    private static string? _testDir;
    private static string? _cEspressoPath;
    private static readonly ConcurrentBag<BenchmarkResult> _results = new();

    private static string TestDir => _testDir ??= FindTestDir();
    private static string CEspressoPath => _cEspressoPath ??= FindCEspresso();

    [TestMethod]
    [DynamicData(nameof(GetPlaFiles))]
    public async Task VerifyEspresso(string relPath)
    {
        string dir = TestDir;
        string cExe = CEspressoPath;
        Assert.IsTrue(File.Exists(cExe), $"C Espresso not found at {cExe}");

        string fullPath = Path.Combine(dir, relPath);
        string plaText = File.ReadAllText(fullPath);

        var cTask  = Task.Run(() => RunCEspresso(cExe, plaText));
        var csTask = Task.Run(() => RunCSharpEspresso(fullPath));
        await Task.WhenAll(cTask, csTask);

        var cResult  = cTask.Result;
        var csResult = csTask.Result;
        Assert.IsNotNull(cResult,  $"C Espresso failed for {relPath}");
        Assert.IsNotNull(csResult, $"C# Espresso failed for {relPath}");

        _results.Add(new BenchmarkResult(
            relPath,
            cResult.Value.Cubes, csResult.Value.Cubes,
            cResult.Value.Elapsed, csResult.Value.Elapsed));

        int deltaCubes = csResult.Value.Cubes - cResult.Value.Cubes;
        double speedup = cResult.Value.Elapsed.TotalMilliseconds / Math.Max(csResult.Value.Elapsed.TotalMilliseconds, 0.001);

        string cubeResult  = deltaCubes == 0 ? "equal cubes"
                           : deltaCubes <  0 ? $"C# better by {-deltaCubes} cube(s)"
                           :                   $"C  better by {deltaCubes} cube(s)";
        string speedResult = speedup > 1.05 ? $"C# faster ({speedup:F2}x)"
                           : speedup < 0.95 ? $"C  faster ({1/speedup:F2}x)"
                           :                  $"roughly equal speed ({speedup:F2}x)";

        TestContext.WriteLine($"[{relPath}]  C={cResult.Value.Cubes} cubes {cResult.Value.Elapsed.TotalMilliseconds:F1}ms" +
            $"  |  C#={csResult.Value.Cubes} cubes {csResult.Value.Elapsed.TotalMilliseconds:F1}ms" +
            $"  |  {cubeResult}  |  {speedResult}");
    }

    [ClassCleanup]
    public static void PrintSummary()
    {
        if (_results.IsEmpty) return;

        var results = _results.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== Benchmark Summary ===");
        sb.AppendLine();
        sb.AppendLine(string.Format("{0,-30} {1,8} {2,9} {3,8} {4,8} {5,8} {6,8}",
            "PLA", "C cubes", "C# cubes", "Δ cubes", "C ms", "C# ms", "Speedup"));
        sb.AppendLine(new string('-', 95));

        int cWinsCubes = 0, csWinsCubes = 0, tieCubes = 0;
        int csWinsTime = 0, inconclusiveTime = 0, tieTime = 0;
        double totalCMs = 0, totalCsMs = 0;

        foreach (var r in results)
        {
            int deltaCubes = r.CsCubes - r.CCubes;
            double speedup = r.CElapsed.TotalMilliseconds / Math.Max(r.CsElapsed.TotalMilliseconds, 0.001);

            string cubeInd = deltaCubes < 0 ? "C#" : deltaCubes > 0 ? "C " : "==";
            string speedInd = speedup > 1.0 ? "C#" : speedup < 1.0 ? "INC" : "==";

            sb.AppendLine($"{r.Name,-30} {r.CCubes,8} {r.CsCubes,9} {deltaCubes,7} {cubeInd} {r.CElapsed.TotalMilliseconds,8:F1} {r.CsElapsed.TotalMilliseconds,8:F1} {speedup,7:F2}x {speedInd}");

            if (deltaCubes < 0) csWinsCubes++;
            else if (deltaCubes > 0) cWinsCubes++;
            else tieCubes++;

            if (speedup > 1.0) csWinsTime++;
            else if (speedup < 1.0) inconclusiveTime++;
            else tieTime++;

            totalCMs += r.CElapsed.TotalMilliseconds;
            totalCsMs += r.CsElapsed.TotalMilliseconds;
        }

        sb.AppendLine(new string('-', 95));
        sb.AppendLine($"  Cubes: C wins {cWinsCubes}, C# wins {csWinsCubes}, tie {tieCubes}  (out of {results.Count})");
        sb.AppendLine($"  Speed: C# wins {csWinsTime}, inconclusive {inconclusiveTime}, tie {tieTime}  |  Total: C {totalCMs:F0}ms, C# {totalCsMs:F0}ms, overall {totalCMs / Math.Max(totalCsMs, 0.001):F2}x");

        Console.Error.WriteLine(sb.ToString());
    }

    public static IEnumerable<object[]> GetPlaFiles()
    {
        string dir = FindTestDir();
        if (!Directory.Exists(dir)) yield break;
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (string path in files)
            yield return new object[] { MakeRelKey(dir, path) };
    }

    private static (int Cubes, TimeSpan Elapsed)? RunCEspresso(string exePath, string plaText)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var sw = Stopwatch.StartNew();
            using var proc = Process.Start(psi)!;
            proc.StandardInput.Write(plaText);
            proc.StandardInput.Close();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(30_000);
            sw.Stop();

            if (proc.ExitCode != 0)
                return null;

            int cubes = CountCubesInPlaOutput(output);
            return (cubes, sw.Elapsed);
        }
        catch
        {
            return null;
        }
    }

    private static (int Cubes, TimeSpan Elapsed)? RunCSharpEspresso(string path)
    {
        try
        {
            using var fp = new StreamReader(path);
            int rc = PlaReader.Read(fp, true, out PlaData? pla);
            if (rc == -1 || pla == null) return null;

            CubeData cube = pla.Cube;
            var sw = Stopwatch.StartNew();
            pla.F = EspressoMinimizer.Minimize(cube, pla.F!, pla.D!, pla.R!);
            sw.Stop();

            return (pla.F.Count, sw.Elapsed);
        }
        catch
        {
            return null;
        }
    }

    private static int CountCubesInPlaOutput(string output)
    {
        // Look for ".p N" line; fall back to counting product term lines
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(".p ", StringComparison.Ordinal))
            {
                if (int.TryParse(trimmed.AsSpan(3), out int n))
                    return n;
            }
        }

        // Fallback: count non-empty lines that aren't dot-commands or .e
        int count = 0;
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('.') && !trimmed.StartsWith('#'))
                count++;
        }
        return count;
    }

    private static string MakeRelKey(string baseDir, string path)
    {
        string normBase = baseDir.Replace('\\', '/').TrimEnd('/');
        string normPath = path.Replace('\\', '/');
        if (normPath.StartsWith(normBase + "/", StringComparison.Ordinal))
            normPath = normPath[(normBase.Length + 1)..];
        return normPath;
    }

    private static string FindTestDir()
    {
        string? dir = Path.GetDirectoryName(typeof(BenchmarkTests).Assembly.Location);
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "tests");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        if (Directory.Exists("tests")) return Path.GetFullPath("tests");
        return Path.GetFullPath("tests");
    }

    private static string FindCEspresso()
    {
        // Walk up from assembly location to find Espresso.exe
        string? dir = Path.GetDirectoryName(typeof(BenchmarkTests).Assembly.Location);
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "x64", "Release", "Espresso.exe");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback paths
        string fallback = Path.GetFullPath(Path.Combine("x64", "Release", "Espresso.exe"));
        return fallback;
    }

    private record BenchmarkResult(string Name, int CCubes, int CsCubes, TimeSpan CElapsed, TimeSpan CsElapsed);
}

[TestClass]
public class PerformanceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void MinimizeAll()
    {
        string dir = FindTestDir();
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        int totalCubes = 0;
        int fileCount = 0;
        var sw = Stopwatch.StartNew();

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 8 }, path =>
        {
            using var fp = new StreamReader(path);
            int rc = PlaReader.Read(fp, true, out PlaData? pla);
            if (rc == -1 || pla == null) return;

            CubeData cube = pla.Cube;
            pla.F = EspressoMinimizer.Minimize(cube, pla.F!, pla.D!, pla.R!);
            Interlocked.Add(ref totalCubes, pla.F.Count);
            Interlocked.Increment(ref fileCount);
        });

        sw.Stop();
        TestContext.WriteLine($"{fileCount} files, {totalCubes} cubes total, {sw.Elapsed.TotalMilliseconds:F0}ms (8 threads)");
    }

    private static string FindTestDir()
    {
        string? dir = Path.GetDirectoryName(typeof(PerformanceTests).Assembly.Location);
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "tests");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        if (Directory.Exists("tests")) return Path.GetFullPath("tests");
        return Path.GetFullPath("tests");
    }
}
