using System.Text;
namespace Espresso;
public class PlaData
{
    public const int FType = 1, DType = 2, RType = 4, EqntottType = 16;
    public const int FdType = FType | DType, FrType = FType | RType, DrType = DType | RType, FdrType = FType | DType | RType;
    public BitVectorFamily? F, D, R;
    public CubeData Cube = CubeData.Empty;
    public int PlaType;
    public string?[]? Label;
}
public static class PlaReader
{
    public static int Read(TextReader fp, bool needsOffset, out PlaData? plaReturn)
    {
        var pla = plaReturn = new PlaData { PlaType = PlaData.FdType };
        var reader = new LineReader(fp);
        CubeData cube = CubeData.Empty;
        int? pendingBinaryVars = null;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line[0] == '.')
            {
                int spaceIdx = line.IndexOf(' ');
                string directive = spaceIdx < 0 ? line[1..] : line[1..spaceIdx];
                if (spaceIdx >= 0) reader.ResetToLine(line[(spaceIdx + 1)..]);
                switch (directive)
                {
                    case "i":
                        if (!cube.FullSet.IsNull) break;
                        int n = reader.NextInt();
                        if (n < 0) throw new InvalidOperationException("num_binary_vars cannot be negative");
                        pendingBinaryVars = n;
                        break;
                    case "o":
                        if (!cube.FullSet.IsNull) break;
                        if (!pendingBinaryVars.HasValue) throw new InvalidOperationException(".o cannot appear before .i");
                        // --- inlined CreateBinary ---
                        {
                            int cbNumBin = pendingBinaryVars.Value;
                            int cbOut = reader.NextInt();
                            if (cbNumBin < 0) throw new InvalidOperationException("num_binary_vars cannot be negative");
                            var cbPartSize = new int[cbNumBin + 1];
                            cbPartSize[cbNumBin] = cbOut;
                            cube = CubeFactory.Build(cbNumBin + 1, cbNumBin, cbPartSize);
                        }
                        // --- end inlined CreateBinary ---
                        pendingBinaryVars = null;
                        pla.Label = new string?[cube.Size];
                        break;
                    case "mv":
                        if (!cube.FullSet.IsNull || pendingBinaryVars.HasValue) break;
                        int numVars = reader.NextInt(), numBinaryVars = reader.NextInt();
                        Span<int> partSize = numVars <= 128 ? stackalloc int[128] : new int[numVars];
                        partSize.Clear();
                        for (int var = numBinaryVars; var < numVars; var++) partSize[var] = reader.NextInt();
                        // --- inlined CreateMultiValued ---
                        {
                            if (numBinaryVars < 0) throw new InvalidOperationException("num_binary_vars (second field of .mv) cannot be negative");
                            if (numVars < numBinaryVars) throw new InvalidOperationException("num_vars (1st field of .mv) must exceed num_binary_vars (2nd field of .mv)");
                            if (partSize.Length != numVars) throw new InvalidOperationException("part_size length mismatch");
                            cube = CubeFactory.Build(numVars, numBinaryVars, partSize);
                        }
                        // --- end inlined CreateMultiValued ---
                        pla.Label = new string?[cube.Size];
                        break;
                    case "p": reader.NextToken(); break;
                    case "e" or "end": goto parseDone;
                    case "type":
                        pla.PlaType = reader.NextToken() switch
                        {
                            "f" => PlaData.FType, "r" => PlaData.RType, "d" => PlaData.DType,
                            "fd" => PlaData.FdType, "fr" => PlaData.FrType, "dr" => PlaData.DrType,
                            "fdr" => PlaData.FdrType, "eqn" or "eqntott" => PlaData.EqntottType,
                            _ => throw new InvalidOperationException("unknown type in .type command"),
                        };
                        break;
                    case "ilb":
                        if (cube.FullSet.IsNull) throw new InvalidOperationException("PLA size must be declared before .ilb");
                        pla.Label ??= new string?[cube.Size];
                        for (int var = 0; var < cube.NumBinaryVars; var++)
                        {
                            string w = reader.NextToken()!; int bi = cube.FirstPart[var];
                            pla.Label[bi + 1] = w; pla.Label[bi] = $"{w}.bar";
                        }
                        break;
                    case "ob":
                        if (cube.FullSet.IsNull) throw new InvalidOperationException("PLA size must be declared before .ob");
                        pla.Label ??= new string?[cube.Size];
                        for (int i = cube.FirstPart[cube.NumVars - 1]; i <= cube.LastPart[cube.NumVars - 1]; i++)
                            pla.Label[i] = reader.NextToken();
                        break;
                    case "label":
                        if (cube.FullSet.IsNull) throw new InvalidOperationException("PLA size must be declared before .label");
                        pla.Label ??= new string?[cube.Size];
                        string varWord = reader.NextToken()!;
                        if (!varWord.StartsWith("var=", StringComparison.Ordinal) || !int.TryParse(varWord[4..], out int labelVar))
                            throw new InvalidOperationException("Error reading labels");
                        for (int i = cube.FirstPart[labelVar]; i <= cube.LastPart[labelVar]; i++)
                            pla.Label[i] = reader.NextToken();
                        break;
                    case "symbolic" or "symbolic-output" or "phase" or "pair":
                        throw new InvalidOperationException($".{directive} is not supported by EspressoII");
                }
            }
            else
            {
                if (cube.FullSet.IsNull) continue;
                if (pla.F == null) { pla.F = BitVectorFamily.Create(10, cube.Size); pla.D = BitVectorFamily.Create(10, cube.Size); pla.R = BitVectorFamily.Create(10, cube.Size); }
                reader.ResetToLine(line);
                Span<uint> scf = cube.Temp[0].AsSpan(), scr = cube.Temp[1].AsSpan(), scd = cube.Temp[2].AsSpan();
                bool savef = false, saved = false, saver = false;
                scf.Clear();
                string? token = reader.NextToken();
                if (token == null) continue;
                int ci = 0;
                for (int var = 0; var < cube.NumBinaryVars; var++)
                {
                    while (ci >= token.Length) { token = reader.NextToken(); if (token == null) goto nextLine; ci = 0; }
                    switch (token[ci++])
                    {
                        case '2': case '-': BitVectorOps.Insert(scf, var * 2 + 1); BitVectorOps.Insert(scf, var * 2); break;
                        case '0': BitVectorOps.Insert(scf, var * 2); break;
                        case '1': BitVectorOps.Insert(scf, var * 2 + 1); break;
                        case '?': break;
                        default: goto nextLine;
                    }
                }
                for (int var = cube.NumBinaryVars; var < cube.NumVars - 1; var++)
                {
                    if (cube.PartSize[var] < 0)
                    {
                        token = reader.NextToken();
                        if (token == null) goto nextLine;
                        if (token is "-" or "ANY") BitVectorOps.Or(scf, scf, cube.VarMask[var].AsSpan());
                        else if (token != "~")
                        {
                            bool found = false;
                            for (int ii = cube.FirstPart[var]; ii <= cube.LastPart[var]; ii++)
                            {
                                if (pla.Label![ii] == null) { pla.Label[ii] = token; BitVectorOps.Insert(scf, ii); found = true; break; }
                                if (pla.Label[ii] == token) { BitVectorOps.Insert(scf, ii); found = true; break; }
                            }
                            if (!found) throw new InvalidOperationException($"declared size of variable {var} is too small");
                        }
                    }
                    else
                    {
                        token = reader.NextToken();
                        if (token == null) goto nextLine;
                        int ti = 0;
                        for (int ii = cube.FirstPart[var]; ii <= cube.LastPart[var]; ii++)
                        {
                            while (ti >= token.Length) { token = reader.NextToken(); if (token == null) goto nextLine; ti = 0; }
                            if (token[ti++] == '1') BitVectorOps.Insert(scf, ii);
                        }
                    }
                }
                BitVectorOps.Copy(scr, scf);
                BitVectorOps.Copy(scd, scf);
                token = reader.NextToken();
                if (token == null) goto nextLine;
                int oi = 0;
                for (int ii = cube.FirstPart[cube.NumVars - 1]; ii <= cube.LastPart[cube.NumVars - 1]; ii++)
                {
                    while (oi >= token.Length) { token = reader.NextToken(); if (token == null) goto nextLine; oi = 0; }
                    switch (token[oi++])
                    {
                        case '4': case '1': if ((pla.PlaType & PlaData.FType) != 0) { BitVectorOps.Insert(scf, ii); savef = true; } break;
                        case '3': case '0': if ((pla.PlaType & PlaData.RType) != 0) { BitVectorOps.Insert(scr, ii); saver = true; } break;
                        case '2': case '-': if ((pla.PlaType & PlaData.DType) != 0) { BitVectorOps.Insert(scd, ii); saved = true; } break;
                        case '~': break;
                    }
                }
                if (savef) pla.F = BitVectorFamily.Add(pla.F!, cube.Temp[0]);
                if (saved) pla.D = BitVectorFamily.Add(pla.D!, cube.Temp[2]);
                if (saver) pla.R = BitVectorFamily.Add(pla.R!, cube.Temp[1]);
                nextLine:;
            }
        }
        parseDone:
        pla.Cube = cube;
        if (pla.F == null) return -1;
        for (int i = 0; i < pla.Cube.NumVars; i++)
            pla.Cube.PartSize[i] = Math.Abs(pla.Cube.PartSize[i]);
        if (needsOffset && pla.PlaType is PlaData.FType or PlaData.FdType)
            pla.R = Complement.ComputeComplement(pla.Cube, Cofactor.BuildCubeList(pla.Cube, pla.F!, pla.D!));
        else if (pla.PlaType == PlaData.FrType)
        {
            // --- inlined MergeByDistance ---
            BitVectorFamily mbdA = BitVectorFamily.Join(pla.F!, pla.R!);
            BitVector mbdMask = pla.Cube.VarMask![pla.Cube.NumVars - 1];
            BitVectorOps.Copy(pla.Cube.Temp[0].AsSpan(), mbdMask.AsSpan());
            Comparison<BitVector> mbdCompare = (a, b) => CubeDistance.Distance1Order(pla.Cube, a, b);
            BitVector[] mbdA1 = BitVectorFamily.ToSortedArray(mbdA, mbdCompare);
            int mbdLen = mbdA.Count;
            BitVectorFamily mbdResult;
            if (mbdLen == 0) mbdResult = BitVectorFamily.FromSortedArray(mbdA1, 0, mbdA.SfSize);
            else
            {
                int mbdDest = 0, mbdii = 0;
                for (int mbdj = 1; mbdj < mbdLen; mbdj++)
                {
                    if (mbdCompare(mbdA1[mbdii], mbdA1[mbdj]) == 0) BitVectorOps.Or(mbdA1[mbdii].AsSpan(), mbdA1[mbdii].AsSpan(), mbdA1[mbdj].AsSpan());
                    else { mbdA1[mbdDest++] = mbdA1[mbdii]; mbdii = mbdj; }
                }
                mbdA1[mbdDest++] = mbdA1[mbdii];
                mbdResult = BitVectorFamily.FromSortedArray(mbdA1, mbdDest, mbdA.SfSize);
            }
            // --- end inlined MergeByDistance ---
            BitVectorFamily.ReturnSortedArray(mbdA1);
            pla.D = Complement.ComputeComplement(pla.Cube, Cofactor.BuildCubeList(pla.Cube, mbdResult));
        }
        else if (pla.PlaType is PlaData.RType or PlaData.DrType)
            pla.F = Complement.ComputeComplement(pla.Cube, Cofactor.BuildCubeList(pla.Cube, pla.D!, pla.R!));
        return 1;
    }

    // Line-oriented reader that strips comments and provides token/int parsing
    private sealed class LineReader
    {
        private readonly TextReader _fp;
        private string? _currentLine;
        private int _pos;
        public LineReader(TextReader fp) => _fp = fp;

        public string? ReadLine()
        {
            _currentLine = null;
            _pos = 0;
            while (true)
            {
                string? line = _fp.ReadLine();
                if (line == null) return null;
                line = line.Trim();
                if (line.Length > 0 && line[0] != '#')
                {
                    _currentLine = line;
                    _pos = 0;
                    return line;
                }
            }
        }

        // Read the next whitespace-delimited token from the current line, or read a new line
        public string? NextToken()
        {
            while (true)
            {
                if (_currentLine != null)
                {
                    while (_pos < _currentLine.Length && char.IsWhiteSpace(_currentLine[_pos])) _pos++;
                    if (_pos < _currentLine.Length)
                    {
                        int start = _pos;
                        while (_pos < _currentLine.Length && !char.IsWhiteSpace(_currentLine[_pos])) _pos++;
                        return _currentLine[start.._pos];
                    }
                }
                if (ReadLine() == null) return null;
            }
        }

        public int NextInt() =>
            int.TryParse(NextToken(), out int v) ? v : throw new InvalidOperationException("parse error reading integer");

        // Return remaining unread portion of current line
        public ReadOnlySpan<char> RemainingLine =>
            _currentLine != null ? _currentLine.AsSpan(_pos) : ReadOnlySpan<char>.Empty;

        public void ResetToLine(string line) { _currentLine = line; _pos = 0; }
    }

}
public static class PlaWriter
{
    private const int Dash = 3, Zero = 1;

    public static void Write(TextWriter fp, PlaData pla, int outputType)
    {
        CubeData cube = pla.Cube;
        if (outputType == PlaData.EqntottType)
        {
            if (cube.Output == -1) throw new InvalidOperationException("Cannot have no-output function for EQNTOTT output mode");
            if (cube.NumMvVars != 1) throw new InvalidOperationException("Must have binary-valued function for EQNTOTT output mode");
            pla.Label ??= new string?[cube.Size];
            for (int var = 0; var < cube.NumVars; var++)
                for (int ei = 0; ei < cube.PartSize[var]; ei++)
                {
                    int ind = cube.FirstPart[var] + ei;
                    pla.Label[ind] ??= var < cube.NumBinaryVars
                        ? (ei % 2 == 0 ? $"v{var}.bar" : $"v{var}")
                        : $"v{var}.{ei}";
                }
            for (int i = 0; i < cube.PartSize[cube.Output]; i++)
            {
                string ol = GetLabel(pla, cube, cube.Output, i);
                fp.Write($"{ol} = ");
                int col = ol.Length + 3;
                bool firstOr = true;
                for (int si = 0; si < pla.F!.Count; si++)
                {
                    ReadOnlySpan<uint> sp = pla.F.GetSpan(si);
                    if (!BitVectorOps.Contains(sp, i + cube.FirstPart[cube.Output])) continue;
                    if (firstOr) { fp.Write('('); col++; }
                    else { fp.Write(" | ("); col += 4; }
                    firstOr = false;
                    bool firstAnd = true;
                    for (int var = 0; var < cube.NumBinaryVars; var++)
                    {
                        int x = GetInput(sp, var);
                        if (x == Dash) continue;
                        string il = GetLabel(pla, cube, var, 1);
                        if (col + il.Length > 72) { fp.Write("\n    "); col = 4; }
                        if (!firstAnd) { fp.Write('&'); col++; }
                        firstAnd = false;
                        if (x == Zero) { fp.Write('!'); col++; }
                        fp.Write(il); col += il.Length;
                    }
                    fp.Write(')'); col++;
                }
                fp.Write(";\n\n");
            }
        }
        else if (outputType == PlaData.FType)
        {
            if (cube.NumMvVars <= 1)
            {
                fp.WriteLine($".i {cube.NumBinaryVars}");
                if (cube.Output != -1) fp.WriteLine($".o {cube.PartSize[cube.Output]}");
            }
            else
            {
                fp.Write($".mv {cube.NumVars} {cube.NumBinaryVars}");
                for (int var = cube.NumBinaryVars; var < cube.NumVars; var++) fp.Write($" {cube.PartSize[var]}");
                fp.Write('\n');
            }
            if (pla.Label != null && cube.NumBinaryVars > 0 && pla.Label[1] != null)
            {
                fp.Write(".ilb");
                for (int var = 0; var < cube.NumBinaryVars; var++) fp.Write($" {GetLabel(pla, cube, var, 1)}");
                fp.Write('\n');
            }
            if (pla.Label != null && cube.Output != -1 && pla.Label[cube.FirstPart[cube.Output]] != null)
            {
                fp.Write(".ob");
                for (int i = 0; i < cube.PartSize[cube.Output]; i++) fp.Write($" {GetLabel(pla, cube, cube.Output, i)}");
                fp.Write('\n');
            }
            for (int var = cube.NumBinaryVars; var < cube.NumVars - 1; var++)
            {
                if (pla.Label != null && pla.Label[cube.FirstPart[var]] != null)
                {
                    fp.Write($".label var={var}");
                    for (int i = cube.FirstPart[var]; i <= cube.LastPart[var]; i++) fp.Write($" {pla.Label[i]}");
                    fp.Write('\n');
                }
            }
            fp.WriteLine($".p {pla.F!.Count}");
            for (int i = 0; i < pla.F.Count; i++)
            {
                BitVector c = pla.F.GetSet(i);
                ReadOnlySpan<uint> sc = c.AsSpan();
                for (int var = 0; var < cube.NumBinaryVars; var++) fp.Write("?01-"[GetInput(sc, var)]);
                for (int var = cube.NumBinaryVars; var < cube.NumVars - 1; var++)
                {
                    fp.Write(' ');
                    for (int j = cube.FirstPart[var]; j <= cube.LastPart[var]; j++) fp.Write("01"[BitVectorOps.Contains(sc, j) ? 1 : 0]);
                }
                if (cube.Output != -1)
                {
                    fp.Write(' ');
                    for (int j = cube.FirstPart[cube.Output]; j <= cube.LastPart[cube.Output]; j++) fp.Write("01"[BitVectorOps.Contains(sc, j) ? 1 : 0]);
                }
                fp.Write('\n');
            }
            fp.WriteLine(".e");
        }
        else throw new InvalidOperationException("Unsupported output type");
    }

    private static int GetInput(ReadOnlySpan<uint> c, int var) =>
        (int)((c[BitVectorOps.WhichWord(2 * var)] >> BitVectorOps.WhichBit(2 * var)) & 3u);

    private static string GetLabel(PlaData pla, CubeData cube, int var, int offset) =>
        pla.Label![cube.FirstPart[var] + offset]!;

}
