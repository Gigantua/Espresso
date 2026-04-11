using System.Text;

namespace EspressoCS;

/// <summary>Ports cvrin.c — cube and cover input routines.</summary>
public static class CvrIn
{
    private static int _lineno;
    private static int _pushback = -2;   // -2 = empty; -1 = EOF sentinel

    // -----------------------------------------------------------------------
    // Low-level character I/O helpers (simulate getc / ungetc)
    // -----------------------------------------------------------------------

    private static int GetChar(TextReader fp)
    {
        if (_pushback != -2) { int c = _pushback; _pushback = -2; return c; }
        return fp.Read();
    }

    private static void UngetChar(int c) { _pushback = c; }

    // -----------------------------------------------------------------------
    // skip_line -- skip to end of line, optionally echoing
    // -----------------------------------------------------------------------
    public static void SkipLine(TextReader fpin, TextWriter fpout, bool echo)
    {
        int ch;
        while ((ch = GetChar(fpin)) != -1 && ch != '\n')
            if (echo) fpout.Write((char)ch);
        if (echo) fpout.Write('\n');
        _lineno++;
    }

    // -----------------------------------------------------------------------
    // get_word -- read next whitespace-delimited token from fp
    // -----------------------------------------------------------------------
    public static string GetWord(TextReader fp)
    {
        int ch;
        while ((ch = GetChar(fp)) != -1 && char.IsWhiteSpace((char)ch))
            ;
        if (ch == -1) return "";

        var sb = new StringBuilder();
        sb.Append((char)ch);
        while ((ch = GetChar(fp)) != -1 && !char.IsWhiteSpace((char)ch))
            sb.Append((char)ch);
        // trailing whitespace or EOF consumed — not pushed back (mirrors C behaviour)
        return sb.ToString();
    }

    // ReadInt -- read an integer from fp (mirrors fscanf(fp,"%d",&n))
    // Returns 1 on success, 0 on failure.  Leaves terminator in stream.
    private static int ReadInt(TextReader fp, out int val)
    {
        int ch;
        while ((ch = GetChar(fp)) != -1 &&
               (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r' || ch == '\f'))
            ;
        if (ch == -1) { val = 0; return 0; }

        bool negative = false;
        if (ch == '-') { negative = true; ch = GetChar(fp); }

        if (ch == -1 || !char.IsDigit((char)ch))
        {
            if (ch != -1) UngetChar(ch);
            val = 0; return 0;
        }

        int n = 0;
        while (ch != -1 && char.IsDigit((char)ch))
        {
            n  = n * 10 + (ch - '0');
            ch = GetChar(fp);
        }
        if (ch != -1) UngetChar(ch);
        val = negative ? -n : n;
        return 1;
    }

    // ScanInt -- read an integer or throw
    private static int ScanInt(TextReader fp)
    {
        if (ReadInt(fp, out int v) != 1)
            throw new InvalidOperationException("parse error reading integer");
        return v;
    }

    // -----------------------------------------------------------------------
    // read_cube -- read one cube from the PLA file
    // -----------------------------------------------------------------------
    public static void ReadCube(TextReader fp, Pla PLA)
    {
        PSet cf = CubeContext.Temp![0];
        PSet cr = CubeContext.Temp![1];
        PSet cd = CubeContext.Temp![2];
        bool savef = false, saved = false, saver = false;
        string token;
        int varx, first, last, offset;

        SetOps.SetClear(cf, CubeContext.Size);

        // Loop for binary variables
        for (int var = 0; var < CubeContext.NumBinaryVars; var++)
        {
            int ch = GetChar(fp);
            switch (ch)
            {
                case -1:   goto bad_char;
                case '\r': var--; break;
                case '\n': _lineno++; var--; break;
                case ' ': case '|': case '\t': var--; break;
                case '2':
                case '-':
                    SetOps.SetInsert(cf, var * 2 + 1);
                    goto case '0';
                case '0':
                    SetOps.SetInsert(cf, var * 2);
                    break;
                case '1':
                    SetOps.SetInsert(cf, var * 2 + 1);
                    break;
                case '?':
                    break;
                default:
                    goto bad_char;
            }
        }

        // Loop for all but last MV variable
        int varLast = CubeContext.NumVars - 1;
        for (int var = CubeContext.NumBinaryVars; var < varLast; var++)
        {
            if (CubeContext.PartSize![var] < 0)
            {
                // Symbolic MV variable
                token = GetWord(fp);
                if (token == "")
                    goto bad_char;

                if (token == "-" || token == "ANY")
                {
                    if (Globals.Kiss && var == CubeContext.NumVars - 2)
                    {
                        // leave empty
                    }
                    else
                    {
                        SetOps.SetOr(cf, cf, CubeContext.VarMask![var]);
                    }
                }
                else if (token == "~")
                {
                    // leave empty
                }
                else
                {
                    if (Globals.Kiss && var == CubeContext.NumVars - 2)
                    { varx = var - 1; offset = Math.Abs(CubeContext.PartSize![var - 1]); }
                    else
                    { varx = var; offset = 0; }

                    first = CubeContext.FirstPart![varx];
                    last  = CubeContext.LastPart![varx];

                    int found = 0;
                    for (int i = first; i <= last; i++)
                    {
                        if (PLA.Label![i] == null)
                        {
                            PLA.Label[i] = token;
                            SetOps.SetInsert(cf, i + offset);
                            found = 1;
                            break;
                        }
                        else if (PLA.Label[i] == token)
                        {
                            SetOps.SetInsert(cf, i + offset);
                            found = 1;
                            break;
                        }
                    }

                    if (found == 0)
                    {
                        Console.Error.WriteLine(
                            $"declared size of variable {var} (counting from variable 0) is too small");
                        System.Environment.Exit(-1);
                    }
                }
            }
            else
            {
                for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                {
                    int ch2 = GetChar(fp);
                    switch (ch2)
                    {
                        case -1:   goto bad_char;
                        case '\r': i--; break;
                        case '\n': _lineno++; i--; break;
                        case ' ': case '|': case '\t': i--; break;
                        case '1':
                            SetOps.SetInsert(cf, i);
                            break;
                        case '0':
                            break;
                        default:
                            goto bad_char;
                    }
                }
            }
        }

        // Loop for last MV variable (output)
        if (Globals.Kiss)
        {
            saver = savef = true;
            SetOps.SetXor(cr, cf, CubeContext.VarMask![CubeContext.NumVars - 2]);
        }
        else
        {
            SetOps.SetCopy(cr, cf);
        }
        SetOps.SetCopy(cd, cf);

        int varOut = varLast;
        for (int i = CubeContext.FirstPart![varOut]; i <= CubeContext.LastPart![varOut]; i++)
        {
            int ch3 = GetChar(fp);
            switch (ch3)
            {
                case -1:   goto bad_char;
                case '\r': i--; break;
                case '\n': _lineno++; i--; break;
                case ' ': case '|': case '\t': i--; break;
                case '4':
                case '1':
                    if ((PLA.PlaType & Pla.FType) != 0) { SetOps.SetInsert(cf, i); savef = true; }
                    break;
                case '3':
                case '0':
                    if ((PLA.PlaType & Pla.RType) != 0) { SetOps.SetInsert(cr, i); saver = true; }
                    break;
                case '2':
                case '-':
                    if ((PLA.PlaType & Pla.DType) != 0) { SetOps.SetInsert(cd, i); saved = true; }
                    break;
                case '~':
                    break;
                default:
                    goto bad_char;
            }
        }

        if (savef) PLA.F = SetFamily.SfAddSet(PLA.F!, cf);
        if (saved) PLA.D = SetFamily.SfAddSet(PLA.D!, cd);
        if (saver) PLA.R = SetFamily.SfAddSet(PLA.R!, cr);
        return;

        bad_char:
        Console.Error.WriteLine($"(warning): input line #{_lineno} ignored");
        SkipLine(fp, Console.Out, true);
    }

    // -----------------------------------------------------------------------
    // parse_pla -- parse the PLA format keywords and data
    // -----------------------------------------------------------------------
    public static void ParsePla(TextReader fp, Pla PLA)
    {
        _lineno = 1;
        while (true)
        {
            int ch = GetChar(fp);
            switch (ch)
            {
                case -1:
                    return;
                case '\n':
                    _lineno++;
                    break;
                case ' ': case '\t': case '\f': case '\r':
                    break;
                case '#':
                    UngetChar(ch);
                    SkipLine(fp, Console.Out, Globals.EchoComments);
                    break;
                case '.':
                {
                    string word = GetWord(fp);

                    if (word == "i")
                    {
                        if (!CubeContext.FullSet.IsNull)
                        {
                            Console.Error.WriteLine("extra .i ignored");
                            SkipLine(fp, Console.Out, false);
                        }
                        else
                        {
                            CubeContext.NumBinaryVars = ScanInt(fp);
                            CubeContext.NumVars       = CubeContext.NumBinaryVars + 1;
                            CubeContext.PartSize      = new int[CubeContext.NumVars];
                        }
                    }
                    else if (word == "o")
                    {
                        if (!CubeContext.FullSet.IsNull)
                        {
                            Console.Error.WriteLine("extra .o ignored");
                            SkipLine(fp, Console.Out, false);
                        }
                        else
                        {
                            if (CubeContext.PartSize == null)
                                throw new InvalidOperationException(".o cannot appear before .i");
                            CubeContext.PartSize[CubeContext.NumVars - 1] = ScanInt(fp);
                            CubeContext.CubeSetup();
                            PlaLabels(PLA);
                        }
                    }
                    else if (word == "mv")
                    {
                        if (!CubeContext.FullSet.IsNull)
                        {
                            Console.Error.WriteLine("extra .mv ignored");
                            SkipLine(fp, Console.Out, false);
                        }
                        else
                        {
                            if (CubeContext.PartSize != null)
                                throw new InvalidOperationException("cannot mix .i and .mv");
                            CubeContext.NumVars       = ScanInt(fp);
                            CubeContext.NumBinaryVars = ScanInt(fp);
                            if (CubeContext.NumBinaryVars < 0)
                                throw new InvalidOperationException("num_binary_vars (second field of .mv) cannot be negative");
                            if (CubeContext.NumVars < CubeContext.NumBinaryVars)
                                throw new InvalidOperationException("num_vars (1st field of .mv) must exceed num_binary_vars (2nd field of .mv)");
                            CubeContext.PartSize = new int[CubeContext.NumVars];
                            for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars; var++)
                                CubeContext.PartSize[var] = ScanInt(fp);
                            CubeContext.CubeSetup();
                            PlaLabels(PLA);
                        }
                    }
                    else if (word == "p")
                    {
                        ReadInt(fp, out _);   // ignore .p count
                    }
                    else if (word == "e" || word == "end")
                    {
                        return;
                    }
                    else if (word == "kiss")
                    {
                        Globals.Kiss = true;
                    }
                    else if (word == "type")
                    {
                        string tword = GetWord(fp);
                        int ti;
                        for (ti = 0; ti < PlaTypes.Table.Length; ti++)
                        {
                            if (PlaTypes.Table[ti].Key[1..] == tword)
                            {
                                PLA.PlaType = PlaTypes.Table[ti].Value;
                                break;
                            }
                        }
                        if (ti >= PlaTypes.Table.Length)
                            throw new InvalidOperationException("unknown type in .type command");
                    }
                    else if (word == "ilb")
                    {
                        if (CubeContext.FullSet.IsNull)
                            throw new InvalidOperationException("PLA size must be declared before .ilb or .ob");
                        if (PLA.Label == null) PlaLabels(PLA);
                        for (int var = 0; var < CubeContext.NumBinaryVars; var++)
                        {
                            string w  = GetWord(fp);
                            int    bi = CubeContext.FirstPart![var];
                            PLA.Label![bi + 1] = w;
                            PLA.Label![bi]     = $"{w}.bar";
                        }
                    }
                    else if (word == "ob")
                    {
                        if (CubeContext.FullSet.IsNull)
                            throw new InvalidOperationException("PLA size must be declared before .ilb or .ob");
                        if (PLA.Label == null) PlaLabels(PLA);
                        int outVar = CubeContext.NumVars - 1;
                        for (int i = CubeContext.FirstPart![outVar]; i <= CubeContext.LastPart![outVar]; i++)
                            PLA.Label![i] = GetWord(fp);
                    }
                    else if (word == "label")
                    {
                        if (CubeContext.FullSet.IsNull)
                            throw new InvalidOperationException("PLA size must be declared before .label");
                        if (PLA.Label == null) PlaLabels(PLA);

                        // Read "var=N"
                        string varWord = GetWord(fp);
                        if (!varWord.StartsWith("var=", StringComparison.Ordinal) ||
                            !int.TryParse(varWord[4..], out int labelVar))
                            throw new InvalidOperationException("Error reading labels");

                        for (int i = CubeContext.FirstPart![labelVar]; i <= CubeContext.LastPart![labelVar]; i++)
                            PLA.Label![i] = GetWord(fp);
                    }
                    else if (word == "symbolic")
                    {
                        if (ReadSymbolic(fp, PLA, out Symbolic? newlist))
                        {
                            if (PLA.SymbolicData == null)
                            {
                                PLA.SymbolicData = newlist;
                            }
                            else
                            {
                                var p1 = PLA.SymbolicData;
                                while (p1.Next != null) p1 = p1.Next;
                                p1.Next = newlist;
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("error reading .symbolic");
                        }
                    }
                    else if (word == "symbolic-output")
                    {
                        if (ReadSymbolic(fp, PLA, out Symbolic? newlist))
                        {
                            if (PLA.SymbolicOutput == null)
                            {
                                PLA.SymbolicOutput = newlist;
                            }
                            else
                            {
                                var p1 = PLA.SymbolicOutput;
                                while (p1.Next != null) p1 = p1.Next;
                                p1.Next = newlist;
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("error reading .symbolic-output");
                        }
                    }
                    else if (word == "phase")
                    {
                        if (CubeContext.FullSet.IsNull)
                            throw new InvalidOperationException("PLA size must be declared before .phase");
                        if (!PLA.Phase.IsNull)
                        {
                            Console.Error.WriteLine("extra .phase ignored");
                            SkipLine(fp, Console.Out, false);
                        }
                        else
                        {
                            int pc;
                            while ((pc = GetChar(fp)) == ' ' || pc == '\t')
                                ;
                            UngetChar(pc);

                            PLA.Phase = SetOps.SetSave(CubeContext.FullSet);
                            int pLast  = CubeContext.LastPart![CubeContext.NumVars - 1];
                            for (int i = CubeContext.FirstPart![CubeContext.NumVars - 1]; i <= pLast; i++)
                            {
                                int pc2 = GetChar(fp);
                                if (pc2 == '0')
                                    SetOps.SetRemove(PLA.Phase, i);
                                else if (pc2 != '1')
                                    throw new InvalidOperationException("only 0 or 1 allowed in phase description");
                            }
                        }
                    }
                    else if (word == "pair")
                    {
                        if (PLA.PairData != null)
                        {
                            Console.Error.WriteLine("extra .pair ignored");
                        }
                        else
                        {
                            var pair = new Pair();
                            PLA.PairData = pair;
                            pair.Cnt  = ScanInt(fp);
                            pair.Var1 = new int[pair.Cnt];
                            pair.Var2 = new int[pair.Cnt];

                            for (int i = 0; i < pair.Cnt; i++)
                            {
                                string w = GetWord(fp);
                                if (w.StartsWith('(')) w = w[1..];

                                if (LabelIndex(PLA, w, out int pvar, out _))
                                    pair.Var1[i] = pvar + 1;
                                else
                                    throw new InvalidOperationException("syntax error in .pair");

                                w = GetWord(fp);
                                if (w.EndsWith(')')) w = w[..^1];

                                if (LabelIndex(PLA, w, out pvar, out _))
                                    pair.Var2[i] = pvar + 1;
                                else
                                    throw new InvalidOperationException("syntax error in .pair");
                            }
                        }
                    }
                    else
                    {
                        if (Globals.EchoUnknownCommands)
                            Console.Write($"{(char)ch}{word} ");
                        SkipLine(fp, Console.Out, Globals.EchoUnknownCommands);
                    }
                    break;
                }
                default:
                {
                    UngetChar(ch);

                    if (CubeContext.FullSet.IsNull)
                    {
                        if (Globals.EchoComments) Console.Write('#');
                        SkipLine(fp, Console.Out, Globals.EchoComments);
                        break;
                    }

                    if (PLA.F == null)
                    {
                        PLA.F = SetFamily.SfNew(10, CubeContext.Size);
                        PLA.D = SetFamily.SfNew(10, CubeContext.Size);
                        PLA.R = SetFamily.SfNew(10, CubeContext.Size);
                    }

                    ReadCube(fp, PLA);
                    break;
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // read_pla -- read a PLA from a file; returns -1 (EOF) or 1 (success)
    // -----------------------------------------------------------------------
    public static int ReadPla(TextReader fp, bool needsDcset, bool needsOffset,
                               int plaType, out Pla? plaReturn)
    {
        var PLA = plaReturn = Pla.NewPla();
        PLA.PlaType = plaType;

        long time = Stubs.PTime();
        ParsePla(fp, PLA);

        if (PLA.F == null)
            return -1;    // EOF

        // Fixup: make all part_sizes positive
        for (int i = 0; i < CubeContext.NumVars; i++)
            CubeContext.PartSize![i] = Math.Abs(CubeContext.PartSize![i]);

        if (Globals.Kiss)
        {
            int third  = CubeContext.NumVars - 3;
            int second = CubeContext.NumVars - 2;

            if (CubeContext.PartSize![third] != CubeContext.PartSize![second])
            {
                Console.Error.WriteLine(" with .kiss option, third to last and second");
                Console.Error.WriteLine("to last variables must be the same size.");
                return -1;
            }

            for (int i = 0; i < CubeContext.PartSize![second]; i++)
                PLA.Label![i + CubeContext.FirstPart![second]] =
                    PLA.Label![i + CubeContext.FirstPart![third]];

            CubeContext.PartSize![second] += CubeContext.PartSize![CubeContext.NumVars - 1];
            CubeContext.NumVars--;
            CubeContext.SetdownCube();
            CubeContext.CubeSetup();
        }

        if (Globals.Trace)
        {
            var cost = new Cost();
            CvrMisc.Totals(time, Globals.ReadTime, PLA.F!, cost);
        }

        time = Stubs.PTime();
        if (Globals.Pos || !PLA.Phase.IsNull || PLA.SymbolicOutput != null)
            needsOffset = true;

        if (needsOffset && (PLA.PlaType == Pla.FType || PLA.PlaType == Pla.FdType))
        {
            PLA.R = Stubs.Complement(Stubs.Cube2List(PLA.F!, PLA.D!));
        }
        else if (needsDcset && PLA.PlaType == Pla.FrType)
        {
            var X = Stubs.D1Merge(SetFamily.SfJoin(PLA.F!, PLA.R!), CubeContext.NumVars - 1);
            PLA.D = Stubs.Complement(Stubs.Cube1List(X));
        }
        else if (PLA.PlaType == Pla.RType || PLA.PlaType == Pla.DrType)
        {
            PLA.F = Stubs.Complement(Stubs.Cube2List(PLA.D!, PLA.R!));
        }

        if (Globals.Trace)
        {
            var cost = new Cost();
            CvrMisc.Totals(time, Globals.ComplTime, PLA.R!, cost);
        }

        if (Globals.Pos)
        {
            var onset = PLA.F!;
            PLA.F    = PLA.R;
            PLA.R    = onset;
            PLA.Phase = SetOps.SetNew(CubeContext.Size);
            SetOps.SetDiff(PLA.Phase, CubeContext.FullSet, CubeContext.VarMask![CubeContext.NumVars - 1]);
        }
        else if (!PLA.Phase.IsNull)
        {
            Stubs.SetPhase(PLA);
        }

        if (PLA.PairData != null)
            Stubs.SetPair(PLA);

        if (PLA.SymbolicData != null)
            Stubs.MapSymbolic(PLA);

        if (PLA.SymbolicOutput != null)
        {
            Stubs.MapOutputSymbolic(PLA);
            if (needsOffset)
            {
                var cost2 = new Cost();
                long t2   = Stubs.PTime();
                PLA.R     = Stubs.Complement(Stubs.Cube2List(PLA.F!, PLA.D!));
                CvrMisc.Totals(t2, Globals.ComplTime, PLA.R, cost2);
            }
        }

        return 1;
    }

    // -----------------------------------------------------------------------
    // PLA_summary
    // -----------------------------------------------------------------------
    public static void PlaSummary(Pla PLA)
    {
        Console.Write($"# PLA is {PLA.Filename}");
        if (CubeContext.NumBinaryVars == CubeContext.NumVars - 1)
            Console.WriteLine($" with {CubeContext.NumBinaryVars} inputs and {CubeContext.PartSize![CubeContext.NumVars - 1]} outputs");
        else
        {
            Console.Write($" with {CubeContext.NumVars} variables ({CubeContext.NumBinaryVars} binary, mv sizes");
            for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars; var++)
                Console.Write($" {CubeContext.PartSize![var]}");
            Console.WriteLine(')');
        }
        Console.WriteLine($"# ON-set cost is  {CvrMisc.PrintCost(PLA.F!)}");
        Console.WriteLine($"# OFF-set cost is {CvrMisc.PrintCost(PLA.R!)}");
        Console.WriteLine($"# DC-set cost is  {CvrMisc.PrintCost(PLA.D!)}");
        if (!PLA.Phase.IsNull)
            Console.WriteLine($"# phase is {CvrOut.Pc1(PLA.Phase)}");
        if (PLA.PairData != null)
        {
            Console.Write("# two-bit decoders:");
            for (int i = 0; i < PLA.PairData.Cnt; i++)
                Console.Write($" ({PLA.PairData.Var1![i]} {PLA.PairData.Var2![i]})");
            Console.WriteLine();
        }
        for (var p1 = PLA.SymbolicData; p1 != null; p1 = p1.Next)
        {
            Console.Write("# symbolic:");
            for (var p2 = p1.SymbolicListHead; p2 != null; p2 = p2.Next)
                Console.Write($" {p2.Variable}");
            Console.WriteLine();
        }
        for (var p1 = PLA.SymbolicOutput; p1 != null; p1 = p1.Next)
        {
            Console.Write("# output symbolic:");
            for (var p2 = p1.SymbolicListHead; p2 != null; p2 = p2.Next)
                Console.Write($" {p2.Pos}");
            Console.WriteLine();
        }
        Console.Out.Flush();
    }

    // -----------------------------------------------------------------------
    // PLA_labels -- allocate label array
    // -----------------------------------------------------------------------
    public static void PlaLabels(Pla PLA)
    {
        PLA.Label = new string?[CubeContext.Size];
    }

    // -----------------------------------------------------------------------
    // read_symbolic -- read a .symbolic or .symbolic-output directive
    // -----------------------------------------------------------------------
    public static bool ReadSymbolic(TextReader fp, Pla PLA, out Symbolic? retval)
    {
        var newlist = new Symbolic
        {
            Next                 = null,
            SymbolicListHead     = null,
            SymbolicListLength   = 0,
            SymbolicLabelHead    = null,
            SymbolicLabelLength  = 0,
        };

        SymbolicList?  prevListp  = null;
        SymbolicLabel? prevLabelp = null;

        // Read variable list until ";"
        for (;;)
        {
            string word = GetWord(fp);
            if (word == ";") break;

            if (LabelIndex(PLA, word, out int var, out int idx))
            {
                var listp = new SymbolicList
                {
                    Variable = var,
                    Pos      = idx,
                    Next     = null,
                };
                if (prevListp == null)
                    newlist.SymbolicListHead = listp;
                else
                    prevListp.Next = listp;
                prevListp = listp;
                newlist.SymbolicListLength++;
            }
            else
            {
                retval = null;
                return false;
            }
        }

        // Read label list until ";"
        for (;;)
        {
            string word = GetWord(fp);
            if (word == ";") break;

            var labelp = new SymbolicLabel
            {
                Label = word,
                Next  = null,
            };
            if (prevLabelp == null)
                newlist.SymbolicLabelHead = labelp;
            else
                prevLabelp.Next = labelp;
            prevLabelp = labelp;
            newlist.SymbolicLabelLength++;
        }

        retval = newlist;
        return true;
    }

    // -----------------------------------------------------------------------
    // label_index -- find variable/position for a label string
    // -----------------------------------------------------------------------
    public static bool LabelIndex(Pla PLA, string word, out int varp, out int ip)
    {
        varp = 0; ip = 0;

        if (PLA.Label == null || PLA.Label[0] == null)
        {
            if (int.TryParse(word, out int n))
            {
                varp = ip = n;
                return true;
            }
        }
        else
        {
            for (int var = 0; var < CubeContext.NumVars; var++)
                for (int i = 0; i < CubeContext.PartSize![var]; i++)
                    if (PLA.Label[CubeContext.FirstPart![var] + i] == word)
                    {
                        varp = var;
                        ip   = i;
                        return true;
                    }
        }
        return false;
    }
}
