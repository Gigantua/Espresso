using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EspressoII.Tests;

[TestClass]
public static class TestCacheSetup
{
    [AssemblyInitialize]
    public static void Init(TestContext _)
    {
        // Default to a persistent cache file next to the test assembly so that a
        // second run reuses work done by the first run. Can be overridden by
        // explicitly setting ESPRESSO_CACHE_FILE, or disabled with ESPRESSO_TEST_NOCACHE=1.
        if (Environment.GetEnvironmentVariable("ESPRESSO_TEST_NOCACHE") == "1")
            return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ESPRESSO_CACHE_FILE")))
        {
            string dir = Path.GetDirectoryName(typeof(TestCacheSetup).Assembly.Location)!;
            string path = Path.Combine(dir, "espresso_test_cache.bin");
            Environment.SetEnvironmentVariable("ESPRESSO_CACHE_FILE", path);
        }
        Espresso.MemoCache.Init();
    }

    [AssemblyCleanup]
    public static void Flush()
    {
        Espresso.MemoCache.Flush();
    }
}
