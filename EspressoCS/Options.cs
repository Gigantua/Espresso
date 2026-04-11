namespace EspressoCS;

/// <summary>
/// Options — command-line option parsing and management.
/// Translated from main.h option tables.
/// Manages program options, debug flags, and Espresso-specific parameters.
/// </summary>
public static class Options
{
    // -----------------------------------------------------------------------
    // Option enumeration (from main.h enum keys)
    // -----------------------------------------------------------------------

    public enum OptionKey
    {
        KeyEspresso,
        KeyPlaVerify,
        KeyCheck,
        KeyContain,
        KeyD1Merge,
        KeyDisjoint,
        KeyDSharp,
        KeyEcho,
        KeyEssen,
        KeyExact,
        KeyExpand,
        KeyGasp,
        KeyIntersect,
        KeyIrred,
        KeyLexsort,
        KeyMakeSparse,
        KeyMap,
        KeyMapdc,
        KeyMinterms,
        KeyOpo,
        KeyOpoall,
        KeyPair,
        KeyPairall,
        KeyPrimes,
        KeyQm,
        KeyReduce,
        KeySharp,
        KeySimplify,
        KeySo,
        KeySoBoth,
        KeyStats,
        KeySuperGasp,
        KeyTaut,
        KeyTest,
        KeyEquiv,
        KeyUnion,
        KeyVerify,
        KeyManyEspresso,
        KeySeparate,
        KeyXor,
        KeyD1MergeIn,
        KeyFsm,
        KeySignature,
        KeyUnknown
    }

    // -----------------------------------------------------------------------
    // OptionTableEntry — mirrors C struct from main.h
    // -----------------------------------------------------------------------

    public class OptionTableEntry
    {
        public string Name { get; set; }
        public OptionKey Key { get; set; }
        public int NumPlas { get; set; }
        public bool NeedsOffset { get; set; }
        public bool NeedsDcset { get; set; }

        public OptionTableEntry(string name, OptionKey key, int numPlas, bool needsOffset, bool needsDcset)
        {
            Name = name;
            Key = key;
            NumPlas = numPlas;
            NeedsOffset = needsOffset;
            NeedsDcset = needsDcset;
        }
    }

    // -----------------------------------------------------------------------
    // DebugTableEntry — mirrors C struct from main.h
    // -----------------------------------------------------------------------

    public class DebugTableEntry
    {
        public string Name { get; set; }
        public uint Value { get; set; }

        public DebugTableEntry(string name, uint value)
        {
            Name = name;
            Value = value;
        }
    }

    // -----------------------------------------------------------------------
    // EspressOptionEntry — mirrors C struct from main.h
    // -----------------------------------------------------------------------

    public class EspressOptionEntry
    {
        public string Name { get; set; }
        public Action<bool> SetVariable { get; set; }
        public bool DefaultValue { get; set; }

        public EspressOptionEntry(string name, Action<bool> setVariable, bool defaultValue)
        {
            Name = name;
            SetVariable = setVariable;
            DefaultValue = defaultValue;
        }
    }

    // -----------------------------------------------------------------------
    // Option table (from main.h option_table[])
    // -----------------------------------------------------------------------

    private static readonly OptionTableEntry[] OptionTable = new[]
    {
        // Ways to minimize functions
        new OptionTableEntry("ESPRESSO", OptionKey.KeyEspresso, 1, true, true),     // must be first
        new OptionTableEntry("many", OptionKey.KeyManyEspresso, 1, true, true),
        new OptionTableEntry("exact", OptionKey.KeyExact, 1, true, true),
        new OptionTableEntry("qm", OptionKey.KeyQm, 1, true, true),
        new OptionTableEntry("single_output", OptionKey.KeySo, 1, true, true),
        new OptionTableEntry("so", OptionKey.KeySo, 1, true, true),
        new OptionTableEntry("so_both", OptionKey.KeySoBoth, 1, true, true),
        new OptionTableEntry("simplify", OptionKey.KeySimplify, 1, false, false),
        new OptionTableEntry("echo", OptionKey.KeyEcho, 1, false, false),
        new OptionTableEntry("signature", OptionKey.KeySignature, 1, true, true),

        // Output phase assignment and assignment of inputs to two-bit decoders
        new OptionTableEntry("opo", OptionKey.KeyOpo, 1, true, true),
        new OptionTableEntry("opoall", OptionKey.KeyOpoall, 1, true, true),
        new OptionTableEntry("pair", OptionKey.KeyPair, 1, true, true),
        new OptionTableEntry("pairall", OptionKey.KeyPairall, 1, true, true),

        // Ways to check covers
        new OptionTableEntry("check", OptionKey.KeyCheck, 1, true, true),
        new OptionTableEntry("stats", OptionKey.KeyStats, 1, false, false),
        new OptionTableEntry("verify", OptionKey.KeyVerify, 2, false, true),
        new OptionTableEntry("PLAverify", OptionKey.KeyPlaVerify, 2, false, true),

        // Hacks
        new OptionTableEntry("equiv", OptionKey.KeyEquiv, 1, true, true),
        new OptionTableEntry("map", OptionKey.KeyMap, 1, false, false),
        new OptionTableEntry("mapdc", OptionKey.KeyMapdc, 1, false, false),
        new OptionTableEntry("fsm", OptionKey.KeyFsm, 1, false, true),

        // Basic boolean operations on covers
        new OptionTableEntry("contain", OptionKey.KeyContain, 1, false, false),
        new OptionTableEntry("d1merge", OptionKey.KeyD1Merge, 1, false, false),
        new OptionTableEntry("d1merge_in", OptionKey.KeyD1MergeIn, 1, false, false),
        new OptionTableEntry("disjoint", OptionKey.KeyDisjoint, 1, true, false),
        new OptionTableEntry("dsharp", OptionKey.KeyDSharp, 2, false, false),
        new OptionTableEntry("intersect", OptionKey.KeyIntersect, 2, false, false),
        new OptionTableEntry("minterms", OptionKey.KeyMinterms, 1, false, false),
        new OptionTableEntry("primes", OptionKey.KeyPrimes, 1, false, true),
        new OptionTableEntry("separate", OptionKey.KeySeparate, 1, true, true),
        new OptionTableEntry("sharp", OptionKey.KeySharp, 2, false, false),
        new OptionTableEntry("union", OptionKey.KeyUnion, 2, false, false),
        new OptionTableEntry("xor", OptionKey.KeyXor, 2, true, true),

        // Debugging only -- call each step of the espresso algorithm
        new OptionTableEntry("essen", OptionKey.KeyEssen, 1, false, true),
        new OptionTableEntry("expand", OptionKey.KeyExpand, 1, true, false),
        new OptionTableEntry("gasp", OptionKey.KeyGasp, 1, true, true),
        new OptionTableEntry("irred", OptionKey.KeyIrred, 1, false, true),
        new OptionTableEntry("make_sparse", OptionKey.KeyMakeSparse, 1, true, true),
        new OptionTableEntry("reduce", OptionKey.KeyReduce, 1, false, true),
        new OptionTableEntry("taut", OptionKey.KeyTaut, 1, false, false),
        new OptionTableEntry("super_gasp", OptionKey.KeySuperGasp, 1, true, true),
        new OptionTableEntry("lexsort", OptionKey.KeyLexsort, 1, false, false),
        new OptionTableEntry("test", OptionKey.KeyTest, 1, true, true)
    };

    // -----------------------------------------------------------------------
    // Debug table (from main.h debug_table[])
    // -----------------------------------------------------------------------

    private static readonly DebugTableEntry[] DebugTable = new[]
    {
        new DebugTableEntry("", EspressoConstants.Expand | EspressoConstants.Essen | EspressoConstants.Irred | 
                                EspressoConstants.Reduce | EspressoConstants.Sparse | EspressoConstants.Gasp | 
                                EspressoConstants.Sharp | EspressoConstants.Mincov),
        new DebugTableEntry("compl", EspressoConstants.Compl),
        new DebugTableEntry("essen", EspressoConstants.Essen),
        new DebugTableEntry("expand", EspressoConstants.Expand),
        new DebugTableEntry("expand1", EspressoConstants.Expand1 | EspressoConstants.Expand),
        new DebugTableEntry("irred", EspressoConstants.Irred),
        new DebugTableEntry("irred1", EspressoConstants.Irred1 | EspressoConstants.Irred),
        new DebugTableEntry("reduce", EspressoConstants.Reduce),
        new DebugTableEntry("reduce1", EspressoConstants.Reduce1 | EspressoConstants.Reduce),
        new DebugTableEntry("mincov", EspressoConstants.Mincov),
        new DebugTableEntry("mincov1", EspressoConstants.Mincov1 | EspressoConstants.Mincov),
        new DebugTableEntry("sparse", EspressoConstants.Sparse),
        new DebugTableEntry("sharp", EspressoConstants.Sharp),
        new DebugTableEntry("taut", EspressoConstants.Taut),
        new DebugTableEntry("gasp", EspressoConstants.Gasp),
        new DebugTableEntry("exact", EspressoConstants.Exact)
    };

    // -----------------------------------------------------------------------
    // Espresso options table (from main.h esp_opt_table[])
    // -----------------------------------------------------------------------

    private static readonly EspressOptionEntry[] EspressOptionTable = new[]
    {
        new EspressOptionEntry("eat", v => Globals.EchoComments = v, false),
        new EspressOptionEntry("eatdots", v => Globals.EchoUnknownCommands = v, false),
        new EspressOptionEntry("fast", v => Globals.SingleExpand = v, true),
        new EspressOptionEntry("kiss", v => Globals.Kiss = v, true),
        new EspressOptionEntry("ness", v => Globals.RemoveEssential = v, false),
        new EspressOptionEntry("nirr", v => Globals.ForceIrredundant = v, false),
        new EspressOptionEntry("nunwrap", v => Globals.UnwrapOnset = v, false),
        new EspressOptionEntry("onset", v => Globals.RecomputeOnset = v, true),
        new EspressOptionEntry("pos", v => Globals.Pos = v, true),
        new EspressOptionEntry("random", v => Globals.UseRandomOrder = v, true),
        new EspressOptionEntry("strong", v => Globals.UseSuperGasp = v, true)
    };

    // -----------------------------------------------------------------------
    // ParseOptions — parse command-line options
    // -----------------------------------------------------------------------

    /// <summary>
    /// ParseOptions — parse command-line arguments into option key and parameters.
    /// Returns the option key for the given option name, or KeyUnknown if not found.
    /// </summary>
    public static OptionKey ParseOptions(string optionName)
    {
        foreach (var entry in OptionTable)
        {
            if (entry.Name.Equals(optionName, System.StringComparison.OrdinalIgnoreCase))
            {
                return entry.Key;
            }
        }
        return OptionKey.KeyUnknown;
    }

    // -----------------------------------------------------------------------
    // GetOption — retrieve option table entry by key
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetOption — get the option table entry for a given key.
    /// </summary>
    public static OptionTableEntry? GetOption(OptionKey key)
    {
        foreach (var entry in OptionTable)
        {
            if (entry.Key == key)
                return entry;
        }
        return null;
    }

    /// <summary>
    /// GetOption — get the option table entry for a given name.
    /// </summary>
    public static OptionTableEntry? GetOption(string name)
    {
        return GetOption(ParseOptions(name));
    }

    // -----------------------------------------------------------------------
    // ApplyOption — apply an espresso option by name
    // -----------------------------------------------------------------------

    /// <summary>
    /// ApplyOption — apply an Espresso option (from esp_opt_table).
    /// Sets the corresponding global variable to the given value.
    /// Returns true if option was found and applied.
    /// </summary>
    public static bool ApplyOption(string optionName, bool value)
    {
        foreach (var entry in EspressOptionTable)
        {
            if (entry.Name.Equals(optionName, System.StringComparison.OrdinalIgnoreCase))
            {
                entry.SetVariable(value);
                return true;
            }
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // GetDebugFlag — get debug flag value by name
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetDebugFlag — get the debug flag value for a given name.
    /// </summary>
    public static uint GetDebugFlag(string debugName)
    {
        foreach (var entry in DebugTable)
        {
            if (entry.Name.Equals(debugName, System.StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }
        return 0;
    }

    // -----------------------------------------------------------------------
    // SetDebugFlag — enable debug flag by name
    // -----------------------------------------------------------------------

    /// <summary>
    /// SetDebugFlag — enable a debug flag by name.
    /// </summary>
    public static void SetDebugFlag(string debugName)
    {
        uint flag = GetDebugFlag(debugName);
        if (flag != 0)
        {
            Globals.Debug |= flag;
        }
    }

    // -----------------------------------------------------------------------
    // ClearDebugFlag — disable debug flag by name
    // -----------------------------------------------------------------------

    /// <summary>
    /// ClearDebugFlag — disable a debug flag by name.
    /// </summary>
    public static void ClearDebugFlag(string debugName)
    {
        uint flag = GetDebugFlag(debugName);
        if (flag != 0)
        {
            Globals.Debug &= ~flag;
        }
    }

    // -----------------------------------------------------------------------
    // PrintOptions — print all available options
    // -----------------------------------------------------------------------

    /// <summary>
    /// PrintOptions — print all available program options to console.
    /// </summary>
    public static void PrintOptions()
    {
        Console.WriteLine("Available options:");
        foreach (var entry in OptionTable)
        {
            Console.Write($"  {entry.Name,-20}");
            Console.Write($" inputs: {entry.NumPlas,-2}");
            Console.Write($" needs_offset: {(entry.NeedsOffset ? "yes" : "no"),-3}");
            Console.Write($" needs_dcset: {(entry.NeedsDcset ? "yes" : "no"),-3}");
            Console.WriteLine();
        }
    }

    // -----------------------------------------------------------------------
    // PrintDebugOptions — print all available debug options
    // -----------------------------------------------------------------------

    /// <summary>
    /// PrintDebugOptions — print all available debug options to console.
    /// </summary>
    public static void PrintDebugOptions()
    {
        Console.WriteLine("Available debug options:");
        foreach (var entry in DebugTable)
        {
            if (entry.Name.Length > 0)
            {
                Console.WriteLine($"  {entry.Name,-20} (0x{entry.Value:X})");
            }
        }
    }

    // -----------------------------------------------------------------------
    // PrintEspressOptions — print all available Espresso options
    // -----------------------------------------------------------------------

    /// <summary>
    /// PrintEspressOptions — print all available Espresso options to console.
    /// </summary>
    public static void PrintEspressOptions()
    {
        Console.WriteLine("Available Espresso options:");
        foreach (var entry in EspressOptionTable)
        {
            Console.WriteLine($"  {entry.Name,-20} (default: {entry.DefaultValue})");
        }
    }

    // -----------------------------------------------------------------------
    // ValidateOption — check if option requires offset and/or dcset
    // -----------------------------------------------------------------------

    /// <summary>
    /// ValidateOption — check if an option meets requirements for offset and dcset.
    /// Returns error message if validation fails, null if valid.
    /// </summary>
    public static string? ValidateOption(OptionKey key, bool hasOffset, bool hasDcset)
    {
        var option = GetOption(key);
        if (option == null)
            return "Unknown option";

        if (option.NeedsOffset && !hasOffset)
            return $"Option '{option.Name}' requires offset (ON-set)";

        if (option.NeedsDcset && !hasDcset)
            return $"Option '{option.Name}' requires don't-care set";

        return null;
    }

    // -----------------------------------------------------------------------
    // GetOptionRequirements — get requirements for an option
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetOptionRequirements — get the requirements (inputs, offset, dcset) for an option.
    /// </summary>
    public static (int NumPlas, bool NeedsOffset, bool NeedsDcset)? GetOptionRequirements(OptionKey key)
    {
        var option = GetOption(key);
        if (option == null)
            return null;

        return (option.NumPlas, option.NeedsOffset, option.NeedsDcset);
    }

    // -----------------------------------------------------------------------
    // GetAllOptions — get all available options
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetAllOptions — return all available option entries.
    /// </summary>
    public static OptionTableEntry[] GetAllOptions()
    {
        return OptionTable;
    }

    // -----------------------------------------------------------------------
    // GetAllDebugOptions — get all available debug options
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetAllDebugOptions — return all available debug option entries.
    /// </summary>
    public static DebugTableEntry[] GetAllDebugOptions()
    {
        return DebugTable;
    }

    // -----------------------------------------------------------------------
    // ResetEspressOptions — reset all Espresso options to defaults
    // -----------------------------------------------------------------------

    /// <summary>
    /// ResetEspressOptions — reset all Espresso options to their default values.
    /// </summary>
    public static void ResetEspressOptions()
    {
        foreach (var entry in EspressOptionTable)
        {
            entry.SetVariable(entry.DefaultValue);
        }
    }
}
