namespace EspressoCS;

/// <summary>Ports cvrout.c — cube and cover output routines.</summary>
public static class CvrOut
{
    // Dash/One/Zero constants for GETINPUT results
    private const int Dash = 3;
    private const int One  = 2;
    private const int Zero = 1;

    // GETINPUT(c, var) -- extract 2-bit input encoding for binary variable var
    private static int GetInput(PSet c, int var) =>
        (int)((c[SetOps.WhichWord(2 * var)] >> SetOps.WhichBit(2 * var)) & 3u);

    // INLABEL(var) -- label string for input variable var (the positive literal)
    private static string InLabel(Pla PLA, int var) =>
        PLA.Label![CubeContext.FirstPart![var] + 1]!;

    // OUTLABEL(i) -- label string for output bit i
    private static string OutLabel(Pla PLA, int i) =>
        PLA.Label![CubeContext.FirstPart![CubeContext.Output] + i]!;

    // fprint_pla -- print a PLA to fp in the requested format
    public static void FprintPla(TextWriter fp, Pla PLA, int outputType)
    {
        if ((outputType & Pla.ConstraintsType) != 0)
        {
            OutputSymbolicConstraints(fp, PLA, 0);
            outputType &= ~Pla.ConstraintsType;
            if (outputType == 0) return;
        }

        if ((outputType & Pla.SymbolicConstraintsType) != 0)
        {
            OutputSymbolicConstraints(fp, PLA, 1);
            outputType &= ~Pla.SymbolicConstraintsType;
            if (outputType == 0) return;
        }

        if (outputType == Pla.PleasureType)
        {
            PlsOutput(fp, PLA);
        }
        else if (outputType == Pla.EqntottType)
        {
            EqnOutput(fp, PLA);
        }
        else if (outputType == Pla.KissType)
        {
            KissOutput(fp, PLA);
        }
        else
        {
            FprHeader(fp, PLA, outputType);

            int num = 0;
            if ((outputType & Pla.FType) != 0) num += PLA.F!.Count;
            if ((outputType & Pla.DType) != 0) num += PLA.D!.Count;
            if ((outputType & Pla.RType) != 0) num += PLA.R!.Count;
            fp.WriteLine($".p {num}");

            if (outputType == Pla.FType)
            {
                for (int si = 0; si < PLA.F!.Count; si++)
                    PrintCube(fp, PLA.F.GetSet(si), "01");
                fp.WriteLine(".e");
            }
            else
            {
                if ((outputType & Pla.FType) != 0)
                    for (int si = 0; si < PLA.F!.Count; si++)
                        PrintCube(fp, PLA.F.GetSet(si), "~1");
                if ((outputType & Pla.DType) != 0)
                    for (int si = 0; si < PLA.D!.Count; si++)
                        PrintCube(fp, PLA.D.GetSet(si), "~2");
                if ((outputType & Pla.RType) != 0)
                    for (int si = 0; si < PLA.R!.Count; si++)
                        PrintCube(fp, PLA.R.GetSet(si), "~0");
                fp.WriteLine(".end");
            }
        }
    }

    // fpr_header -- print PLA header
    public static void FprHeader(TextWriter fp, Pla PLA, int outputType)
    {
        if (outputType != Pla.FType)
        {
            fp.Write(".type ");
            if ((outputType & Pla.FType) != 0) fp.Write('f');
            if ((outputType & Pla.DType) != 0) fp.Write('d');
            if ((outputType & Pla.RType) != 0) fp.Write('r');
            fp.Write('\n');
        }

        if (CubeContext.NumMvVars <= 1)
        {
            fp.WriteLine($".i {CubeContext.NumBinaryVars}");
            if (CubeContext.Output != -1)
                fp.WriteLine($".o {CubeContext.PartSize![CubeContext.Output]}");
        }
        else
        {
            fp.Write($".mv {CubeContext.NumVars} {CubeContext.NumBinaryVars}");
            for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars; var++)
                fp.Write($" {CubeContext.PartSize![var]}");
            fp.Write('\n');
        }

        // binary-valued labels
        if (PLA.Label != null && CubeContext.NumBinaryVars > 0 &&
            PLA.Label[1] != null)
        {
            fp.Write(".ilb");
            for (int var = 0; var < CubeContext.NumBinaryVars; var++)
                fp.Write($" {InLabel(PLA, var)}");
            fp.Write('\n');
        }

        // output-part labels
        if (PLA.Label != null && CubeContext.Output != -1 &&
            PLA.Label[CubeContext.FirstPart![CubeContext.Output]] != null)
        {
            fp.Write(".ob");
            for (int i = 0; i < CubeContext.PartSize![CubeContext.Output]; i++)
                fp.Write($" {OutLabel(PLA, i)}");
            fp.Write('\n');
        }

        // multiple-valued labels
        for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
        {
            int first = CubeContext.FirstPart![var];
            int last  = CubeContext.LastPart![var];
            if (PLA.Label != null && PLA.Label[first] != null)
            {
                fp.Write($".label var={var}");
                for (int i = first; i <= last; i++)
                    fp.Write($" {PLA.Label[i]}");
                fp.Write('\n');
            }
        }

        if (!PLA.Phase.IsNull)
        {
            int first = CubeContext.FirstPart![CubeContext.Output];
            int last  = CubeContext.LastPart![CubeContext.Output];
            fp.Write("#.phase ");
            for (int i = first; i <= last; i++)
                fp.Write(SetOps.IsInSet(PLA.Phase, i) ? '1' : '0');
            fp.Write('\n');
        }
    }

    // pls_output
    public static void PlsOutput(Pla PLA)
    {
        PlsOutput(Console.Out, PLA);
    }

    public static void PlsOutput(TextWriter fp, Pla PLA)
    {
        fp.WriteLine(".option unmerged");
        MakeupLabels(PLA);
        PlsLabel(PLA, fp);
        PlsGroup(PLA, fp);
        fp.WriteLine($".p {PLA.F!.Count}");
        for (int si = 0; si < PLA.F.Count; si++)
            PrintExpandedCube(fp, PLA.F.GetSet(si), PLA.Phase);
        fp.WriteLine(".end");
    }

    // pls_group
    public static void PlsGroup(Pla PLA, TextWriter fp)
    {
        fp.Write("\n.group");
        int col = 6;
        for (int var = 0; var < CubeContext.NumVars - 1; var++)
        {
            fp.Write(" ("); col += 2;
            for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
            {
                string lbl = PLA.Label![i]!;
                int len = lbl.Length;
                if (col + len > 75) { fp.Write(" \\\n"); col = 0; }
                else if (i != 0)    { fp.Write(' '); col += 1; }
                fp.Write(lbl); col += len;
            }
            fp.Write(')'); col += 1;
        }
        fp.Write('\n');
    }

    // pls_label
    public static void PlsLabel(Pla PLA, TextWriter fp)
    {
        fp.Write(".label");
        int col = 6;
        for (int var = 0; var < CubeContext.NumVars; var++)
            for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
            {
                string lbl = PLA.Label![i]!;
                int len = lbl.Length;
                if (col + len > 75) { fp.Write(" \\\n"); col = 0; }
                else                { fp.Write(' '); col += 1; }
                fp.Write(lbl); col += len;
            }
    }

    // eqn_output -- output algebraic equations
    public static void EqnOutput(Pla PLA)
    {
        EqnOutput(Console.Out, PLA);
    }

    public static void EqnOutput(TextWriter fp, Pla PLA)
    {
        if (CubeContext.Output == -1)
            throw new InvalidOperationException("Cannot have no-output function for EQNTOTT output mode");
        if (CubeContext.NumMvVars != 1)
            throw new InvalidOperationException("Must have binary-valued function for EQNTOTT output mode");
        MakeupLabels(PLA);

        for (int i = 0; i < CubeContext.PartSize![CubeContext.Output]; i++)
        {
            string ol = OutLabel(PLA, i);
            fp.Write($"{ol} = ");
            int col = ol.Length + 3;
            bool firstor = true;

            for (int si = 0; si < PLA.F!.Count; si++)
            {
                var p = PLA.F.GetSet(si);
                if (SetOps.IsInSet(p, i + CubeContext.FirstPart![CubeContext.Output]))
                {
                    if (firstor) { fp.Write('('); col += 1; }
                    else         { fp.Write(" | ("); col += 4; }
                    firstor = false;
                    bool firstand = true;

                    for (int var = 0; var < CubeContext.NumBinaryVars; var++)
                    {
                        int x = GetInput(p, var);
                        if (x != Dash)
                        {
                            string il = InLabel(PLA, var);
                            int len = il.Length;
                            if (col + len > 72) { fp.Write("\n    "); col = 4; }
                            if (!firstand) { fp.Write('&'); col += 1; }
                            firstand = false;
                            if (x == Zero) { fp.Write('!'); col += 1; }
                            fp.Write(il); col += len;
                        }
                    }
                    fp.Write(')'); col += 1;
                }
            }
            fp.Write(";\n\n");
        }
    }

    // fmt_cube -- format a cube into string s
    public static string FmtCube(PSet c, string outMap, char[] s)
    {
        int len = 0;
        for (int var = 0; var < CubeContext.NumBinaryVars; var++)
            s[len++] = "?01-"[GetInput(c, var)];

        for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
        {
            s[len++] = ' ';
            for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                s[len++] = "01"[SetOps.IsInSet(c, i) ? 1 : 0];
        }

        if (CubeContext.Output != -1)
        {
            int last = CubeContext.LastPart![CubeContext.Output];
            s[len++] = ' ';
            for (int i = CubeContext.FirstPart![CubeContext.Output]; i <= last; i++)
                s[len++] = outMap[SetOps.IsInSet(c, i) ? 1 : 0];
        }
        s[len] = '\0';
        return new string(s, 0, len);
    }

    // print_cube -- print a cube to fp
    public static void PrintCube(TextWriter fp, PSet c, string outMap)
    {
        for (int var = 0; var < CubeContext.NumBinaryVars; var++)
            fp.Write("?01-"[GetInput(c, var)]);

        for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
        {
            fp.Write(' ');
            for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                fp.Write("01"[SetOps.IsInSet(c, i) ? 1 : 0]);
        }

        if (CubeContext.Output != -1)
        {
            int last = CubeContext.LastPart![CubeContext.Output];
            fp.Write(' ');
            for (int i = CubeContext.FirstPart![CubeContext.Output]; i <= last; i++)
                fp.Write(outMap[SetOps.IsInSet(c, i) ? 1 : 0]);
        }
        fp.Write('\n');
    }

    // print_expanded_cube
    public static void PrintExpandedCube(TextWriter fp, PSet c, PSet phase)
    {
        for (int var = 0; var < CubeContext.NumBinaryVars; var++)
            for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                fp.Write("~1"[SetOps.IsInSet(c, i) ? 1 : 0]);

        for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
            for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                fp.Write("1~"[SetOps.IsInSet(c, i) ? 1 : 0]);

        if (CubeContext.Output != -1)
        {
            int var2 = CubeContext.NumVars - 1;
            fp.Write(' ');
            for (int i = CubeContext.FirstPart![var2]; i <= CubeContext.LastPart![var2]; i++)
            {
                string outMap = (phase.IsNull || SetOps.IsInSet(phase, i)) ? "~1" : "~0";
                fp.Write(outMap[SetOps.IsInSet(c, i) ? 1 : 0]);
            }
        }
        fp.Write('\n');
    }

    // pc1, pc2 -- debug helpers
    public static string Pc1(PSet c)
    {
        char[] s = new char[256];
        return FmtCube(c, "01", s);
    }

    public static string Pc2(PSet c)
    {
        char[] s = new char[256];
        return FmtCube(c, "01", s);
    }

    // debug_print -- print a cubelist
    public static void DebugPrint(PSet[] T, string name, int level)
    {
        int cnt = CubeListSize(T);
        var temp = SetOps.SetNew(CubeContext.Size);
        if (Globals.VerboseDebug && level == 0) Console.WriteLine();
        Console.WriteLine($"{name}[{level}]: ord(T)={cnt}");
        if (Globals.VerboseDebug)
        {
            Console.WriteLine($"cofactor={Pc1(T[0])}");
            for (int ti = 2, c2 = 1; ti < T.Length && !T[ti].IsNull; ti++, c2++)
                Console.WriteLine($"{c2,4}. {Pc1(SetOps.SetOr(temp, T[ti], T[0]))}");
        }
    }

    // debug1_print -- print a set family
    public static void Debug1Print(SetFamily T, string name, int num)
    {
        if (Globals.VerboseDebug && num == 0) Console.WriteLine();
        Console.WriteLine($"{name}[{num}]: ord(T)={T.Count}");
        if (Globals.VerboseDebug)
        {
            int cnt = 1;
            for (int si = 0; si < T.Count; si++)
                Console.WriteLine($"{cnt++,4}. {Pc1(T.GetSet(si))}");
        }
    }

    // cprint
    public static void Cprint(SetFamily T)
    {
        for (int si = 0; si < T.Count; si++)
            Console.WriteLine(Pc1(T.GetSet(si)));
    }

    // makeup_labels -- fill in any missing labels
    public static void MakeupLabels(Pla PLA)
    {
        if (PLA.Label == null)
            CvrIn.PlaLabels(PLA);

        for (int var = 0; var < CubeContext.NumVars; var++)
            for (int i = 0; i < CubeContext.PartSize![var]; i++)
            {
                int ind = CubeContext.FirstPart![var] + i;
                if (PLA.Label![ind] == null)
                {
                    if (var < CubeContext.NumBinaryVars)
                        PLA.Label[ind] = (i % 2 == 0)
                            ? $"v{var}.bar"
                            : $"v{var}";
                    else
                        PLA.Label[ind] = $"v{var}.{i}";
                }
            }
    }

    // kiss_output
    public static void KissOutput(TextWriter fp, Pla PLA)
    {
        for (int si = 0; si < PLA.F!.Count; si++)
            KissPrintCube(fp, PLA, PLA.F.GetSet(si), "~1");
        for (int si = 0; si < PLA.D!.Count; si++)
            KissPrintCube(fp, PLA, PLA.D.GetSet(si), "~2");
    }

    // kiss_print_cube
    public static void KissPrintCube(TextWriter fp, Pla PLA, PSet p, string outString)
    {
        for (int var = 0; var < CubeContext.NumBinaryVars; var++)
            fp.Write("?01-"[GetInput(p, var)]);

        for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
        {
            fp.Write(' ');
            if (SetOps.SetpImplies(CubeContext.VarMask![var], p))
            {
                fp.Write('-');
            }
            else
            {
                int part = -1;
                for (int i = CubeContext.FirstPart![var]; i <= CubeContext.LastPart![var]; i++)
                {
                    if (SetOps.IsInSet(p, i))
                    {
                        if (part != -1)
                            throw new InvalidOperationException("more than 1 part in a symbolic variable\n");
                        part = i;
                    }
                }
                if (part == -1)
                    fp.Write('~');
                else
                    fp.Write(PLA.Label![part]);
            }
        }

        int outVar = CubeContext.Output;
        if (outVar != -1)
        {
            fp.Write(' ');
            for (int i = CubeContext.FirstPart![outVar]; i <= CubeContext.LastPart![outVar]; i++)
                fp.Write(outString[SetOps.IsInSet(p, i) ? 1 : 0]);
        }
        fp.Write('\n');
    }

    // output_symbolic_constraints
    public static void OutputSymbolicConstraints(TextWriter fp, Pla PLA, int outputSymbolic)
    {
        if ((CubeContext.NumVars - CubeContext.NumBinaryVars) <= 1)
            return;
        MakeupLabels(PLA);

        for (int var = CubeContext.NumBinaryVars; var < CubeContext.NumVars - 1; var++)
        {
            int npermute = CubeContext.PartSize![var];
            int[] permute = new int[npermute];
            for (int i = 0; i < npermute; i++)
                permute[i] = CubeContext.FirstPart![var] + i;

            SetFamily A = SetFamily.SfPermute(SetFamily.SfSave(PLA.F!), permute, npermute);

            int noweight = 0;
            for (int i = 0; i < A.Count; i++)
            {
                int size = SetOps.SetOrd(A.GetSet(i));
                if (size == 1 || size == A.SfSize)
                {
                    SetFamily.SfDelSet(A, i--);
                    noweight++;
                }
            }

            int[] weight = new int[A.Count];
            for (int i = 0; i < A.Count; i++)
                SetOps.ResetFlag(A.GetSet(i), SetOps.Covered);
            for (int i = 0; i < A.Count; i++)
            {
                weight[i] = 0;
                if (!SetOps.TestP(A.GetSet(i), SetOps.Covered))
                {
                    weight[i] = 1;
                    for (int j = i + 1; j < A.Count; j++)
                    {
                        if (SetOps.SetpEqual(A.GetSet(i), A.GetSet(j)))
                        {
                            weight[i]++;
                            SetOps.SetFlag(A.GetSet(j), SetOps.Covered);
                        }
                    }
                }
            }

            if (outputSymbolic == 0)
            {
                fp.WriteLine($"# Symbolic constraints for variable {var} (Numeric form)");
                fp.WriteLine($"# unconstrained weight = {noweight}");
                fp.WriteLine($"num_codes={CubeContext.PartSize![var]}");
                for (int i = 0; i < A.Count; i++)
                {
                    if (weight[i] > 0)
                    {
                        fp.Write($"weight={weight[i]}:");
                        for (int j = 0; j < A.SfSize; j++)
                            if (SetOps.IsInSet(A.GetSet(i), j))
                                fp.Write($" {j}");
                        fp.Write('\n');
                    }
                }
            }
            else
            {
                fp.WriteLine($"# Symbolic constraints for variable {var} (Symbolic form)");
                for (int i = 0; i < A.Count; i++)
                {
                    if (weight[i] > 0)
                    {
                        fp.Write($"#   w={weight[i]}: (");
                        for (int j = 0; j < A.SfSize; j++)
                            if (SetOps.IsInSet(A.GetSet(i), j))
                                fp.Write($" {PLA.Label![CubeContext.FirstPart![var] + j]}");
                        fp.Write(" )\n");
                    }
                }
            }
        }
    }

    // CubeListSize -- count cubes in a cubelist (T[2..] until null sentinel)
    private static int CubeListSize(PSet[] T)
    {
        int n = 0;
        for (int i = 2; i < T.Length && !T[i].IsNull; i++) n++;
        return n;
    }
}
