namespace EspressoCS;

using static CubeContext;
using static SetOps;
using static SetFamily;

/// <summary>Port of hack.c — symbolic variable mapping and FSM disassembly.</summary>
public static class Hack
{
    // GETINPUT encoding (mirrors CvrOut private constants)
    private const int HackZero = 1;
    private const int HackOne  = 2;
    private const int HackTwo  = 3; // DC / don't-care (both bits set)

    private static int GetInput(PSet c, int var) =>
        (int)((c[WhichWord(2 * var)] >> WhichBit(2 * var)) & 3u);

    // -----------------------------------------------------------------------
    // map_dcset
    // -----------------------------------------------------------------------
    public static void MapDcSet(Pla PLA)
    {
        if (PLA.Label == null || PLA.Label[0] == null)
            return;

        int var = -1;
        for (int i = 0; i < NumBinaryVars * 2; i++)
        {
            string? lbl = PLA.Label[i];
            if (lbl == null) continue;
            if (lbl.StartsWith("DONT_CARE", StringComparison.Ordinal) ||
                lbl.StartsWith("DONTCARE",  StringComparison.Ordinal) ||
                lbl.StartsWith("dont_care", StringComparison.Ordinal) ||
                lbl.StartsWith("dontcare",  StringComparison.Ordinal))
            {
                var = i / 2;
                break;
            }
        }
        if (var == -1) return;

        PSet cplus  = SetSave(FullSet);
        PSet cminus = SetSave(FullSet);
        SetRemove(cplus,  var * 2);
        SetRemove(cminus, var * 2 + 1);

        var listPlus  = Cofactor.Cube1List(PLA.F!);
        var cofPlus   = Cofactor.GetCofactor(listPlus, cplus);
        Compl.SimpComp(cofPlus, out SetFamily Tplus, out SetFamily Tplusbar);

        var listMinus = Cofactor.Cube1List(PLA.F!);
        var cofMinus  = Cofactor.GetCofactor(listMinus, cminus);
        Compl.SimpComp(cofMinus, out SetFamily Tminus, out SetFamily Tminusbar);

        SetFamily term1 = Sharp.CvIntersect(Tplus, Tminusbar);
        SetFamily term2 = Sharp.CvIntersect(Tminus, Tplusbar);
        SetFamily dcset = Contain.SfUnion(term1, term2);
        Compl.SimpComp(Cofactor.Cube1List(dcset), out PLA.D!, out SetFamily dcsetbar);
        SetFamily newf = Sharp.CvIntersect(PLA.F!, dcsetbar);
        PLA.F = newf;

        // Remove cubes dependent on the DONT_CARE variable
        SfActive(PLA.F);
        for (int _i = 0; _i < PLA.F.Count; _i++)
        {
            var p = PLA.F.GetSet(_i);
            if (!IsInSet(p, var * 2) || !IsInSet(p, var * 2 + 1))
                ResetFlag(p, Active);
        }
        PLA.F = SfInactive(PLA.F);

        // Resize cube and delete the don't-care variable
        SetdownCube();
        for (int i = 2 * var + 2; i < Size; i++)
            PLA.Label![i - 2] = PLA.Label[i];
        for (int i = var + 1; i < NumVars; i++)
            PartSize![i - 1] = PartSize[i];
        NumBinaryVars--;
        NumVars--;
        CubeSetup();
        PLA.F = SfDelc(PLA.F, 2 * var, 2 * var + 1);
        PLA.D = SfDelc(PLA.D!, 2 * var, 2 * var + 1);
    }

    // -----------------------------------------------------------------------
    // map_output_symbolic
    // -----------------------------------------------------------------------
    public static void MapOutputSymbolic(Pla PLA)
    {
        SetFamily? newF = null;
        SetFamily? newD = null;

        // Remove the DC-set from the ON-set
        if (PLA.D!.Count > 0)
            PLA.F = Stubs.Complement(Cofactor.Cube2List(PLA.D, PLA.R!));

        // tot_size = width added for all symbolic variables
        int totSize = 0;
        for (var p1 = PLA.SymbolicOutput; p1 != null; p1 = p1.Next)
        {
            for (var p2 = p1.SymbolicListHead; p2 != null; p2 = p2.Next)
            {
                if (p2.Pos < 0 || p2.Pos >= PartSize![Output])
                    throw new InvalidOperationException("symbolic-output index out of range");
            }
            totSize += 1 << p1.SymbolicListLength;
        }

        // Adjust indices to skip over new outputs
        for (var p1 = PLA.SymbolicOutput; p1 != null; p1 = p1.Next)
            for (var p2 = p1.SymbolicListHead; p2 != null; p2 = p2.Next)
                p2.Pos += totSize;

        // Resize cube structure
        int oldSize = Size;
        PartSize![Output] += totSize;
        SetdownCube();
        CubeSetup();

        // Insert space in the output part
        int baseCol = FirstPart![Output];
        PLA.F = SfAddcol(PLA.F!, baseCol, totSize);
        PLA.D = SfAddcol(PLA.D,  baseCol, totSize);
        PLA.R = SfAddcol(PLA.R!, baseCol, totSize);

        // Do the real work
        for (var p1 = PLA.SymbolicOutput; p1 != null; p1 = p1.Next)
        {
            newF = SfNew(100, Size);
            newD = SfNew(100, Size);
            FindInputs(null, PLA, p1.SymbolicListHead, baseCol, 0, ref newF, ref newD);
            PLA.F = newF;
            // retain OLD DC-set
            baseCol += 1 << p1.SymbolicListLength;
        }

        // Delete the old outputs and resize
        PSet compress = SetFull(newF!.SfSize);
        for (var p1 = PLA.SymbolicOutput; p1 != null; p1 = p1.Next)
            for (var p2 = p1.SymbolicListHead; p2 != null; p2 = p2.Next)
            {
                int bit = FirstPart![Output] + p2.Pos;
                SetRemove(compress, bit);
            }
        PartSize![Output] -= newF.SfSize - SetOrd(compress);
        SetdownCube();
        CubeSetup();
        PLA.F = SfCompress(PLA.F!, compress);
        PLA.D = SfCompress(PLA.D!, compress);
        if (Size != PLA.F.SfSize)
            throw new InvalidOperationException("error");

        // Quick minimization
        PLA.F = Contain.SfContain(PLA.F);
        PLA.D = Contain.SfContain(PLA.D!);
        for (int i = 0; i < NumVars; i++)
        {
            PLA.F = Stubs.D1Merge(PLA.F, i);
            PLA.D = Stubs.D1Merge(PLA.D!, i);
        }
        PLA.F = Contain.SfContain(PLA.F);
        PLA.D = Contain.SfContain(PLA.D!);

        PLA.R = SfNew(0, Size);

        SymbolicHackLabels(PLA, PLA.SymbolicOutput, compress, Size, oldSize, totSize);
    }

    // -----------------------------------------------------------------------
    // find_inputs
    // -----------------------------------------------------------------------
    private static void FindInputs(SetFamily? A, Pla PLA, SymbolicList? list,
        int baseCol, int value, ref SetFamily newF, ref SetFamily newD)
    {
        if (list == null)
        {
            // Simulate inputs against on-set
            SetFamily S = Sharp.CvIntersect(A!, PLA.F!);
            for (int _i = 0; _i < S.Count; _i++)
            {
                var p = S.GetSet(_i);
                SetInsert(p, baseCol + value);
            }
            newF = SfAppend(newF, S);
        }
        else
        {
            // Intersect and recur with the OFF-set
            SetFamily S = CvrM.CofOutput(PLA.R!, FirstPart![Output] + list.Pos);
            if (A != null)
                S = Sharp.CvIntersect(A, S);
            FindInputs(S, PLA, list.Next, baseCol, value * 2, ref newF, ref newD);

            // Intersect and recur with the ON-set
            S = CvrM.CofOutput(PLA.F!, FirstPart![Output] + list.Pos);
            if (A != null)
                S = Sharp.CvIntersect(A, S);
            FindInputs(S, PLA, list.Next, baseCol, value * 2 + 1, ref newF, ref newD);
        }
    }

    // -----------------------------------------------------------------------
    // map_symbolic
    // -----------------------------------------------------------------------
    public static void MapSymbolic(Pla PLA)
    {
        // Verify legal values
        for (var p1 = PLA.SymbolicData; p1 != null; p1 = p1.Next)
            for (var p2 = p1.SymbolicListHead; p2 != null; p2 = p2.Next)
                if (p2.Variable < 0 || p2.Variable >= NumBinaryVars)
                    throw new InvalidOperationException(".symbolic requires binary variables");

        int sizeAdded = 0;
        int numAddedVars = 0;
        for (var p1 = PLA.SymbolicData; p1 != null; p1 = p1.Next)
        {
            sizeAdded += 1 << p1.SymbolicListLength;
            numAddedVars++;
        }

        PSet compress = SetFull(PLA.F!.SfSize + sizeAdded);
        for (var p1 = PLA.SymbolicData; p1 != null; p1 = p1.Next)
            for (var p2 = p1.SymbolicListHead; p2 != null; p2 = p2.Next)
            {
                SetRemove(compress, p2.Variable * 2);
                SetRemove(compress, p2.Variable * 2 + 1);
            }
        int numDeletedVars = ((PLA.F.SfSize + sizeAdded) - SetOrd(compress)) / 2;

        int numVars       = NumVars - numDeletedVars + numAddedVars;
        int numBinaryVars = NumBinaryVars - numDeletedVars;
        int[] newPartSize = new int[numVars];
        newPartSize[numVars - 1] = PartSize![NumVars - 1];
        for (int v = NumBinaryVars; v < NumVars - 1; v++)
            newPartSize[v - numDeletedVars] = PartSize[v];

        // Re-size covers
        int baseCol = FirstPart![Output];
        PLA.F = SfAddcol(PLA.F, baseCol, sizeAdded);
        PLA.D = SfAddcol(PLA.D!, baseCol, sizeAdded);
        PLA.R = SfAddcol(PLA.R!, baseCol, sizeAdded);

        // Compute values for new mv variables
        int newvar = (NumVars - 1) - numDeletedVars;
        for (var p1 = PLA.SymbolicData; p1 != null; p1 = p1.Next)
        {
            PLA.F = MapSymbolicCover(PLA.F, p1.SymbolicListHead, baseCol);
            PLA.D = MapSymbolicCover(PLA.D!, p1.SymbolicListHead, baseCol);
            PLA.R = MapSymbolicCover(PLA.R!, p1.SymbolicListHead, baseCol);
            baseCol += 1 << p1.SymbolicListLength;
            newPartSize[newvar++] = 1 << p1.SymbolicListLength;
        }

        // Delete the binary variables
        PLA.F = SfCompress(PLA.F, compress);
        PLA.D = SfCompress(PLA.D!, compress);
        PLA.R = SfCompress(PLA.R!, compress);

        SymbolicHackLabels(PLA, PLA.SymbolicData, compress,
            NumVars - numDeletedVars * 2 + sizeAdded, Size, sizeAdded);
        SetdownCube();
        NumVars       = numVars;
        NumBinaryVars = numBinaryVars;
        PartSize      = newPartSize;
        CubeSetup();
    }

    // -----------------------------------------------------------------------
    // map_symbolic_cover
    // -----------------------------------------------------------------------
    private static SetFamily MapSymbolicCover(SetFamily T, SymbolicList? list, int baseCol)
    {
        for (int _i = 0; _i < T.Count; _i++)
        {
            var p = T.GetSet(_i);
            FormBitVector(ref p, baseCol, 0, list);
        }
        return T;
    }

    // -----------------------------------------------------------------------
    // form_bitvector
    // -----------------------------------------------------------------------
    private static void FormBitVector(ref PSet p, int baseCol, int value, SymbolicList? list)
    {
        if (list == null)
        {
            SetInsert(p, baseCol + value);
        }
        else
        {
            switch (GetInput(p, list.Variable))
            {
                case HackZero:
                    FormBitVector(ref p, baseCol, value * 2, list.Next);
                    break;
                case HackOne:
                    FormBitVector(ref p, baseCol, value * 2 + 1, list.Next);
                    break;
                case HackTwo: // DC
                    FormBitVector(ref p, baseCol, value * 2, list.Next);
                    FormBitVector(ref p, baseCol, value * 2 + 1, list.Next);
                    break;
                default:
                    throw new InvalidOperationException("bad cube in form_bitvector");
            }
        }
    }

    // -----------------------------------------------------------------------
    // symbolic_hack_labels
    // -----------------------------------------------------------------------
    private static void SymbolicHackLabels(Pla PLA, Symbolic? list, PSet compress,
        int newSize, int oldSize, int sizeAdded)
    {
        if (PLA.Label == null) return;

        string?[] oldlabel = PLA.Label;
        PLA.Label = new string?[newSize];

        // Copy binary / unchanged mv labels
        int baseIdx = 0;
        for (int i = 0; i < FirstPart![Output]; i++)
        {
            if (IsInSet(compress, i))
                PLA.Label[baseIdx++] = oldlabel[i];
            // else: old label discarded (GC)
        }

        // Add user-defined labels for symbolic outputs
        for (var p1 = list; p1 != null; p1 = p1.Next)
        {
            var p3 = p1.SymbolicLabelHead;
            for (int i = 0; i < (1 << p1.SymbolicListLength); i++)
            {
                if (p3 == null)
                    PLA.Label[baseIdx + i] = $"X{i}";
                else
                {
                    PLA.Label[baseIdx + i] = p3.Label;
                    p3 = p3.Next;
                }
            }
            baseIdx += 1 << p1.SymbolicListLength;
        }

        // Copy labels for binary outputs which remain
        for (int i = FirstPart![Output]; i < oldSize; i++)
        {
            if (IsInSet(compress, i + sizeAdded))
                PLA.Label[baseIdx++] = oldlabel[i];
        }
    }

    // -----------------------------------------------------------------------
    // fsm_simplify (static helper)
    // -----------------------------------------------------------------------
    private static SetFamily FsmSimplify(SetFamily F)
    {
        SetFamily D = SfNew(0, Size);
        SetFamily R = Stubs.Complement(Cofactor.Cube1List(F));
        F = Stubs.Espresso(F, D, R);
        return F;
    }

    // -----------------------------------------------------------------------
    // disassemble_fsm
    // -----------------------------------------------------------------------
    public static void DisassembleFsm(Pla PLA, bool verboseMode)
    {
        if (NumVars - NumBinaryVars != 2)
        {
            Console.Error.WriteLine("use .symbolic and .symbolic-output to specify");
            Console.Error.WriteLine("the present state and next state field information");
            throw new InvalidOperationException("disassemble_pla: need two multiple-valued variables\n");
        }

        int nin     = NumBinaryVars;
        int nstates = PartSize![NumBinaryVars];
        int nout    = PartSize[NumVars - 1];
        if (nout < nstates)
        {
            Console.Error.WriteLine("use .symbolic and .symbolic-output to specify");
            Console.Error.WriteLine("the present state and next state field information");
            throw new InvalidOperationException("disassemble_pla: # outputs < # states\n");
        }

        int presentState = FirstPart![NumBinaryVars];
        PSet presentStateMask = SetNew(Size);
        for (int i = 0; i < nstates; i++)
            SetInsert(presentStateMask, i + presentState);

        int nextState = FirstPart![NumBinaryVars + 1];
        PSet nextStateMask = SetNew(Size);
        for (int i = 0; i < nstates; i++)
            SetInsert(nextStateMask, i + nextState);

        PSet stateMask = SetNew(Size);
        SetOr(stateMask, nextStateMask, presentStateMask);

        SetFamily F = SfNew(10, Size);

        // Check for arcs from ANY state to state #i
        for (int i = 0; i < nstates; i++)
        {
            SetFamily tF = SfNew(10, Size);
            for (int _i = 0; _i < PLA.F!.Count; _i++)
            {
                var p = PLA.F.GetSet(_i);
                if (SetpImplies(presentStateMask, p))
                {
                    if (IsInSet(p, nextState + i))
                        tF = SfAddSet(tF, p);
                }
            }
            int before = tF.Count;
            if (before > 0)
            {
                tF = FsmSimplify(tF);
                for (int _i = 0; _i < tF.Count; _i++)
                {
                    var p = tF.GetSet(_i);
                    SetInsert(p, nextState + i);
                }
                int after = tF.Count;
                F = SfAppend(F, tF);
                if (verboseMode)
                    Console.WriteLine($"# state EVERY to {i}, before={before} after={after}");
            }
        }

        // Arcs with no next state
        SetFamily goNowhere = SfNew(10, Size);
        for (int _i = 0; _i < PLA.F!.Count; _i++)
        {
            var p = PLA.F.GetSet(_i);
            if (SetpDisjoint(p, nextStateMask))
                goNowhere = SfAddSet(goNowhere, p);
        }
        int before2 = goNowhere.Count;
        goNowhere = CvrM.UnravelRange(goNowhere, NumBinaryVars, NumBinaryVars);
        int after2 = goNowhere.Count;
        F = SfAppend(F, goNowhere);
        if (verboseMode)
            Console.WriteLine($"# state ANY to NOWHERE, before={before2} after={after2}");

        // Minimize cover for all arcs from state #i to state #j
        for (int i = 0; i < nstates; i++)
        {
            for (int j = 0; j < nstates; j++)
            {
                SetFamily tF = SfNew(10, Size);
                for (int _i = 0; _i < PLA.F!.Count; _i++)
                {
                    var p = PLA.F.GetSet(_i);
                    if (!SetpImplies(presentStateMask, p))
                    {
                        if (IsInSet(p, presentState + i) && IsInSet(p, nextState + j))
                        {
                            PSet p1 = SetSave(p);
                            SetDiff(p1, p1, stateMask);
                            SetInsert(p1, presentState + i);
                            SetInsert(p1, nextState + j);
                            tF = SfAddSet(tF, p1);
                        }
                    }
                }
                int before3 = tF.Count;
                if (before3 > 0)
                {
                    tF = FsmSimplify(tF);
                    for (int _i = 0; _i < tF.Count; _i++)
                    {
                        var p = tF.GetSet(_i);
                        SetInsert(p, nextState + j);
                    }
                    int after3 = tF.Count;
                    F = SfAppend(F, tF);
                    if (verboseMode)
                        Console.WriteLine($"# state {i} to {j}, before={before3} after={after3}");
                }
            }
        }

        PLA.F = F;
        PLA.D = SfNew(0, Size);

        SetdownCube();
        NumBinaryVars = nin;
        NumVars       = nin + 3;
        PartSize      = new int[NumVars];
        PartSize[NumBinaryVars]     = nstates;
        PartSize[NumBinaryVars + 1] = nstates;
        PartSize[NumBinaryVars + 2] = nout - nstates;
        CubeSetup();

        for (int _i = 0; _i < PLA.F.Count; _i++)
        {
            var p = PLA.F.GetSet(_i);
            CvrOut.KissPrintCube(Console.Out, PLA, p, "~1");
        }
    }
}
