using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Espresso;

/// <summary>
/// Keyed on a 128-bit hash of (schema, operation tag, serialized inputs).
/// Always enabled; persists to %APPDATA%/Espresso/cache.bin by default.
///
/// Override via env var:
///   ESPRESSO_CACHE_FILE=&lt;path&gt;   — use a different cache file path
///
/// Correctness note: keys are 128 bits; the probability of a false positive over
/// 10^9 entries is ~3e-21. We do not store the original inputs for verification,
/// so a collision would silently produce a wrong result — the 128-bit width
/// keeps this statistically negligible.
/// </summary>
public static class MemoCache
{
    public const byte TagIsTautology   = 1;
    public const byte TagIsCubeCovered = 2;
    public const byte TagMinimize      = 3;
    public const byte TagFindIrredundant = 4;
    public const byte TagExpandCover     = 5;
    public const byte TagMakeSparse      = 6;
    public const byte TagComplement      = 7;

    private const uint Magic   = 0x4D50534Eu;
    private const uint Version = 1;

    public readonly record struct Key(ulong Hi, ulong Lo);

    private static readonly Dictionary<Key, byte[]> _store = new(capacity: 1 << 14);
    private static readonly object _sync = new();
    private static string? _persistPath;
    private static bool _enabled = true;
    private static bool _dirty;
    private static long _hits, _misses, _puts;

    private static readonly ConditionalWeakTable<CubeData, object> _schemaHashes = new();

    public static bool Enabled => _enabled;
    public static long Hits => Interlocked.Read(ref _hits);
    public static long Misses => Interlocked.Read(ref _misses);
    public static long Puts => Interlocked.Read(ref _puts);
    public static int EntryCount { get { lock (_sync) return _store.Count; } }

    public static void Init()
    {
        string? envPath = Environment.GetEnvironmentVariable("ESPRESSO_CACHE_FILE");
        _persistPath = !string.IsNullOrEmpty(envPath)
            ? envPath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Espresso", "cache.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(_persistPath)!);
        TryLoad(_persistPath);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TrySave();
    }

    public static void Flush() => TrySave();

    public static void Clear()
    {
        lock (_sync) { _store.Clear(); _dirty = true; }
    }

    public static ulong SchemaHash(CubeData cube)
    {
        if (_schemaHashes.TryGetValue(cube, out var boxed)) return (ulong)boxed!;
        var h = new Hash128();
        h.MixU64((ulong)cube.Size);
        h.MixU64((ulong)cube.NumVars);
        h.MixU64((ulong)cube.NumBinaryVars);
        h.MixU64((ulong)cube.InWord);
        h.MixU64(cube.InMask);
        foreach (int p in cube.PartSize) h.MixU64((ulong)p);
        ulong hashed = h.Hi ^ h.Lo;
        _schemaHashes.Add(cube, hashed);
        return hashed;
    }

    public static bool TryGet(in Key key, out byte[] value)
    {
        if (!_enabled) { value = null!; return false; }
        lock (_sync)
        {
            if (_store.TryGetValue(key, out var got)) { Interlocked.Increment(ref _hits); value = got; return true; }
        }
        Interlocked.Increment(ref _misses);
        value = null!;
        return false;
    }

    public static void Put(in Key key, byte[] value)
    {
        if (!_enabled) return;
        lock (_sync)
        {
            _store[key] = value;
            _dirty = true;
        }
        Interlocked.Increment(ref _puts);
    }

    public static bool TryGetBool(in Key key, out bool value)
    {
        if (TryGet(key, out var bytes) && bytes.Length == 1) { value = bytes[0] != 0; return true; }
        value = false; return false;
    }

    public static void PutBool(in Key key, bool value) => Put(key, new[] { value ? (byte)1 : (byte)0 });

    // ---- Key building ----

    public static Key BuildCubeListKey(byte tag, CubeData cube, CubeList T)
    {
        var h = new Hash128();
        h.MixU64(SchemaHash(cube));
        h.MixU64(tag);
        h.MixU64((ulong)T.Count);
        h.MixSpan(T.CofSpan);
        for (int i = 0; i < T.Count; i++) h.MixSpan(T.GetSpan(i));
        h.Finalize128();
        return new Key(h.Hi, h.Lo);
    }

    public static Key BuildCubeListCubeKey(byte tag, CubeData cube, CubeList T, ReadOnlySpan<uint> c)
    {
        var h = new Hash128();
        h.MixU64(SchemaHash(cube));
        h.MixU64(tag);
        h.MixU64((ulong)T.Count);
        h.MixSpan(T.CofSpan);
        h.MixSpan(c);
        for (int i = 0; i < T.Count; i++) h.MixSpan(T.GetSpan(i));
        h.Finalize128();
        return new Key(h.Hi, h.Lo);
    }

    public static Key BuildMinimizeKey(CubeData cube, BitVectorFamily F, BitVectorFamily D, BitVectorFamily R)
        => BuildFamiliesKey(TagMinimize, cube, F, D, R, extra: 0);

    public static Key BuildFamiliesKey(byte tag, CubeData cube, BitVectorFamily A, BitVectorFamily? B, BitVectorFamily? C, long extra)
    {
        var h = new Hash128();
        h.MixU64(SchemaHash(cube));
        h.MixU64(tag);
        h.MixU64((ulong)extra);
        MixFamily(ref h, A);
        if (B is not null) MixFamily(ref h, B);
        if (C is not null) MixFamily(ref h, C);
        h.Finalize128();
        return new Key(h.Hi, h.Lo);
    }

    public static Key BuildCubeListFamilyKey(byte tag, CubeData cube, CubeList T)
    {
        var h = new Hash128();
        h.MixU64(SchemaHash(cube));
        h.MixU64(tag);
        h.MixU64((ulong)T.Count);
        h.MixSpan(T.CofSpan);
        // Row-order-invariant across T rows.
        ulong accHi = 0, accLo = 0, sumHi = 0, sumLo = 0;
        for (int i = 0; i < T.Count; i++)
        {
            var rh = Hash128.Of(T.GetSpan(i));
            accHi ^= rh.Hi; accLo ^= rh.Lo;
            sumHi += rh.Hi; sumLo += rh.Lo;
        }
        h.MixU64(accHi); h.MixU64(accLo);
        h.MixU64(sumHi); h.MixU64(sumLo);
        h.Finalize128();
        return new Key(h.Hi, h.Lo);
    }

    private static void MixFamily(ref Hash128 h, BitVectorFamily fam)
    {
        h.MixU64((ulong)fam.SfSize);
        h.MixU64((ulong)fam.Count);
        int words = fam.Words;
        // Row-order-invariant: combine per-row 128-bit hashes via commutative XOR + ADD.
        ulong accHi = 0, accLo = 0;
        ulong sumHi = 0, sumLo = 0;
        for (int i = 0; i < fam.Count; i++)
        {
            var rh = Hash128.Of(fam.GetSpan(i));
            accHi ^= rh.Hi; accLo ^= rh.Lo;
            sumHi += rh.Hi; sumLo += rh.Lo;
        }
        h.MixU64(accHi); h.MixU64(accLo);
        h.MixU64(sumHi); h.MixU64(sumLo);
    }

    public static bool TryGetFamily(in Key key, int expectedSize, out BitVectorFamily fam)
    {
        fam = null!;
        if (!TryGet(key, out var bytes)) return false;
        if (bytes.Length < 8) return false;
        int sfSize = BitConverter.ToInt32(bytes, 0);
        int count  = BitConverter.ToInt32(bytes, 4);
        if (sfSize != expectedSize) return false;
        int words = (sfSize + 31) >> 5;
        int stride = words + 1;
        int expectedBytes = 8 + count * stride * 4;
        if (bytes.Length != expectedBytes) return false;
        fam = BitVectorFamily.Create(Math.Max(count, 1), sfSize);
        fam.Count = count;
        fam.ActiveCount = 0;
        Buffer.BlockCopy(bytes, 8, fam.Data, 0, count * stride * 4);
        return true;
    }

    public static void PutFamily(in Key key, BitVectorFamily fam)
    {
        if (!_enabled) return;
        int stride = fam.Stride;
        int payload = fam.Count * stride * 4;
        byte[] bytes = new byte[8 + payload];
        BitConverter.GetBytes(fam.SfSize).CopyTo(bytes, 0);
        BitConverter.GetBytes(fam.Count).CopyTo(bytes, 4);
        Buffer.BlockCopy(fam.Data, 0, bytes, 8, payload);
        Put(key, bytes);
    }

    // ---- Disk persistence ----

    private static void TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != Magic) return;
            if (br.ReadUInt32() != Version) return;
            int count = br.ReadInt32();
            lock (_sync)
            {
                _store.EnsureCapacity(count);
                for (int i = 0; i < count; i++)
                {
                    ulong hi = br.ReadUInt64();
                    ulong lo = br.ReadUInt64();
                    int len = br.ReadInt32();
                    var data = br.ReadBytes(len);
                    _store[new Key(hi, lo)] = data;
                }
            }
        }
        catch { /* corrupt/partial cache → ignore */ }
    }

    private static void TrySave()
    {
        if (_persistPath is null) return;
        if (!_dirty) return;
        try
        {
            string tmp = _persistPath + ".tmp";
            using (var fs = File.Create(tmp))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Magic);
                bw.Write(Version);
                KeyValuePair<Key, byte[]>[] snapshot;
                lock (_sync) snapshot = _store.ToArray();
                bw.Write(snapshot.Length);
                foreach (var kv in snapshot)
                {
                    bw.Write(kv.Key.Hi);
                    bw.Write(kv.Key.Lo);
                    bw.Write(kv.Value.Length);
                    bw.Write(kv.Value);
                }
            }
            File.Move(tmp, _persistPath!, overwrite: true);
            _dirty = false;
        }
        catch { /* non-fatal */ }
    }
}

/// <summary>
/// Streaming 128-bit hash using MurmurHash3 x64 128-bit algorithm.
/// </summary>
internal struct Hash128
{
    private List<byte> _bytes;
    public ulong Hi;
    public ulong Lo;

    public Hash128() { _bytes = new(); Hi = 0; Lo = 0; }

    public void MixU64(ulong v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, v);
        _bytes.AddRange((ReadOnlySpan<byte>)buf);
    }

    public void MixSpan(ReadOnlySpan<uint> s)
    {
        // Mix length, then the raw bytes of the span.
        MixU64((ulong)s.Length);
        _bytes.AddRange(MemoryMarshal.AsBytes(s));
    }

    public void Finalize128()
    {
        var span = CollectionsMarshal.AsSpan(_bytes);
        (Lo, Hi) = Murmur3_x64_128(span);
    }

    public static Hash128 Of(ReadOnlySpan<uint> s)
    {
        var h = new Hash128();
        h.MixSpan(s);
        h.Finalize128();
        return h;
    }

    private static (ulong h1, ulong h2) Murmur3_x64_128(ReadOnlySpan<byte> data)
    {
        const ulong C1 = 0x87C37B91114253D5UL;
        const ulong C2 = 0x4CF5AD432745937FUL;
        ulong h1 = 0, h2 = 0;
        ulong len = (ulong)data.Length;
        int nblocks = data.Length / 16;
        for (int i = 0; i < nblocks; i++)
        {
            ulong k1 = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i * 16));
            ulong k2 = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i * 16 + 8));
            k1 *= C1; k1 = RotL64(k1, 31); k1 *= C2; h1 ^= k1;
            h1 = RotL64(h1, 27); h1 += h2; h1 = h1 * 5 + 0x52DCE729UL;
            k2 *= C2; k2 = RotL64(k2, 33); k2 *= C1; h2 ^= k2;
            h2 = RotL64(h2, 31); h2 += h1; h2 = h2 * 5 + 0x38495AB5UL;
        }
        var tail = data.Slice(nblocks * 16);
        ulong tk1 = 0, tk2 = 0;
        switch (tail.Length)
        {
            case 15: tk2 ^= (ulong)tail[14] << 48; goto case 14;
            case 14: tk2 ^= (ulong)tail[13] << 40; goto case 13;
            case 13: tk2 ^= (ulong)tail[12] << 32; goto case 12;
            case 12: tk2 ^= (ulong)tail[11] << 24; goto case 11;
            case 11: tk2 ^= (ulong)tail[10] << 16; goto case 10;
            case 10: tk2 ^= (ulong)tail[ 9] <<  8; goto case 9;
            case  9: tk2 ^= (ulong)tail[ 8];
                     tk2 *= C2; tk2 = RotL64(tk2, 33); tk2 *= C1; h2 ^= tk2;
                     goto case 8;
            case  8: tk1 ^= (ulong)tail[7] << 56; goto case 7;
            case  7: tk1 ^= (ulong)tail[6] << 48; goto case 6;
            case  6: tk1 ^= (ulong)tail[5] << 40; goto case 5;
            case  5: tk1 ^= (ulong)tail[4] << 32; goto case 4;
            case  4: tk1 ^= (ulong)tail[3] << 24; goto case 3;
            case  3: tk1 ^= (ulong)tail[2] << 16; goto case 2;
            case  2: tk1 ^= (ulong)tail[1] <<  8; goto case 1;
            case  1: tk1 ^= (ulong)tail[0];
                     tk1 *= C1; tk1 = RotL64(tk1, 31); tk1 *= C2; h1 ^= tk1;
                     break;
        }
        h1 ^= len; h2 ^= len;
        h1 += h2; h2 += h1;
        h1 = FMix64(h1); h2 = FMix64(h2);
        h1 += h2; h2 += h1;
        return (h1, h2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotL64(ulong x, int r) => (x << r) | (x >> (64 - r));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong FMix64(ulong k)
    {
        k ^= k >> 33; k *= 0xFF51AFD7ED558CCDUL;
        k ^= k >> 33; k *= 0xC4CEB9FE1A85EC53UL;
        k ^= k >> 33;
        return k;
    }
}
