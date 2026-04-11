namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;

/// <summary>
/// Opo — output phase optimization (Sasao's technique).
/// Translated 1:1 from opo.c.
/// </summary>
public static class Opo
{
    // Static state (C static locals promoted to class fields)
    private static int _opoNoMakeSparse;
    private static int _opoRepeated;
    private static int _opoExact;

    // -----------------------------------------------------------------------
    // phase_assignment
    // -----------------------------------------------------------------------

    public static void PhaseAssignment(Pla PLA, int opoStrategy)
    {
        _opoNoMakeSparse = opoStrategy % 2;
        Globals.SkipMakeSparse = _opoNoMakeSparse != 0;
        _opoRepeated = (opoStrategy / 2) % 2;
        _opoExact    = (opoStrategy / 4) % 2;

        if (!PLA.Phase.IsNull)
        {
            // set_free is a no-op in C#
        }

        if (_opoRepeated != 0)
        {
            PLA.Phase = SetSave(FullSet);
            RepeatedPhaseAssignment(PLA);
        }
        else
        {
            PLA.Phase = FindPhase(PLA, 0, PSet.Null);
        }

        Globals.SkipMakeSparse = false;
        SetPhase(PLA);
        Minimize(PLA);
    }

    // -----------------------------------------------------------------------
    // repeated_phase_assignment
    // -----------------------------------------------------------------------

    public static void RepeatedPhaseAssignment(Pla PLA)
    {
        for (int i = 0; i < PartSize![Output]; i++)
        {
            PSet phase = FindPhase(PLA, i, PLA.Phase);

            if (!IsInSet(phase, FirstPart![Output] + i))
            {
                SetRemove(PLA.Phase, FirstPart![Output] + i);
            }

            if (Globals.Trace || Globals.Summary)
            {
                Console.WriteLine($"\nOPO loop for output #{i}");
                Console.WriteLine($"PLA->phase is {CvrOut.Pc1(PLA.Phase)}");
                Console.WriteLine($"phase      is {CvrOut.Pc1(phase)}");
            }
        }
    }

    // -----------------------------------------------------------------------
    // find_phase
    // -----------------------------------------------------------------------

    public static PSet FindPhase(Pla PLA, int firstOutput, PSet phase1)
    {
        PSet phase = SetSave(FullSet);

        Pla PLA1 = new Pla();
        PLA1.F = SfSave(PLA.F!);
        PLA1.R = SfSave(PLA.R!);
        PLA1.D = SfSave(PLA.D!);
        if (!phase1.IsNull)
        {
            PLA1.Phase = SetSave(phase1);
            SetPhase(PLA1);
        }

        // EXEC_S(output_phase_setup, "OPO-SETUP", PLA1->F)
        {
            long t = Stubs.PTime();
            OutputPhaseSetup(PLA1, firstOutput);
            if (Globals.Summary) CvrMisc.SizeStamp(PLA1.F!, "OPO-SETUP ");
        }

        Minimize(PLA1);

        // EXEC_S(PLA1->F = opo(...), "OPO", PLA1->F)
        {
            long t = Stubs.PTime();
            PLA1.F = OpoMethod(phase, PLA1.F!, PLA1.D!, PLA1.R!, firstOutput);
            if (Globals.Summary) CvrMisc.SizeStamp(PLA1.F!, "OPO       ");
        }

        Pla.FreePla(PLA1);

        SetdownCube();
        PartSize![Output] -= (PartSize![Output] - firstOutput) / 2;
        CubeSetup();

        return phase;
    }

    // -----------------------------------------------------------------------
    // opo — multiply expression out to determine a minimum subset of primes
    // -----------------------------------------------------------------------

    public static SetFamily OpoMethod(PSet phase, SetFamily T, SetFamily D, SetFamily R, int firstOutput)
    {
        int offset, output, ind;

        PSet select = SetFull(T.Count);
        for (output = 0; output < firstOutput; output++)
        {
            ind = FirstPart![Output] + output;
            for (int i = 0; i < T.Count; i++)
            {
                PSet p = T.GetSet(i);
                if (IsInSet(p, ind))
                {
                    SetRemove(select, i);
                }
            }
        }

        offset = (PartSize![Output] - firstOutput) / 2;
        int lastOutput = firstOutput + offset - 1;
        SetFamily temp = OpoRecur(T, D, select, offset, firstOutput, lastOutput);

        PSet pdest = temp.GetSet(0);
        SetFamily T1 = SfNew(T.Count, Size);
        for (int i = 0; i < T.Count; i++)
        {
            PSet p = T.GetSet(i);
            if (!IsInSet(pdest, i))
            {
                T1 = SfAddSet(T1, p);
            }
        }

        SetFamily T2 = Stubs.Complement(Stubs.Cube1List(T1));
        PSet notCovered = SetNew(Size);
        PSet tmp = SetNew(Size);

        for (int pi = 0; pi < T.Count; pi++)
        {
            PSet p = T.GetSet(pi);
            for (int p1i = 0; p1i < T2.Count; p1i++)
            {
                PSet p1 = T2.GetSet(p1i);
                if (SetC.Cdist0(p, p1))
                {
                    SetOr(notCovered, notCovered, SetAnd(tmp, p, p1));
                }
            }
        }

        for (output = firstOutput; output <= lastOutput; output++)
        {
            ind = FirstPart![Output] + output;
            if (IsInSet(notCovered, ind))
            {
                if (IsInSet(notCovered, ind + offset))
                    throw new InvalidOperationException("error in output phase assignment");
                else
                    SetRemove(phase, ind);
            }
        }

        return T1;
    }

    // -----------------------------------------------------------------------
    // opo_recur
    // -----------------------------------------------------------------------

    private static int _opoRecurLevel = 0;

    private static SetFamily OpoRecur(SetFamily T, SetFamily D, PSet select, int offset, int first, int last)
    {
        SetFamily temp;

        _opoRecurLevel++;
        if (first == last)
        {
            temp = OpoLeaf(T, select, first, first + offset);
        }
        else
        {
            int middle = (first + last) / 2;
            SetFamily sl = OpoRecur(T, D, select, offset, first, middle);
            SetFamily sr = OpoRecur(T, D, select, offset, middle + 1, last);
            temp = Unate.UnateIntersect(sl, sr, _opoRecurLevel == 1);
            if (Globals.Trace)
            {
                Console.WriteLine($"# OPO[{_opoRecurLevel - 1}]: {temp.Count,4} = {sl.Count,4} x {sr.Count,4}, time = {Stubs.PrintTime(Stubs.PTime())}");
                Console.Out.Flush();
            }
        }
        _opoRecurLevel--;
        return temp;
    }

    // -----------------------------------------------------------------------
    // opo_leaf
    // -----------------------------------------------------------------------

    private static SetFamily OpoLeaf(SetFamily T, PSet select, int out1, int out2)
    {
        out1 += FirstPart![Output];
        out2 += FirstPart![Output];

        SetFamily temp = SfNew(2, T.Count);

        // ON-set primes
        PSet pdest = temp.GetSet(temp.Count++);
        SetCopy(pdest, select);
        for (int i = 0; i < T.Count; i++)
        {
            PSet p = T.GetSet(i);
            if (IsInSet(p, out1))
            {
                SetRemove(pdest, i);
            }
        }

        // OFF-set primes
        pdest = temp.GetSet(temp.Count++);
        SetCopy(pdest, select);
        for (int i = 0; i < T.Count; i++)
        {
            PSet p = T.GetSet(i);
            if (IsInSet(p, out2))
            {
                SetRemove(pdest, i);
            }
        }

        return temp;
    }

    // -----------------------------------------------------------------------
    // output_phase_setup
    // -----------------------------------------------------------------------

    public static void OutputPhaseSetup(Pla PLA, int firstOutput)
    {
        if (Output == -1)
            throw new InvalidOperationException("output_phase_setup: must have an output");

        SetFamily F = PLA.F!;
        SetFamily D = PLA.D!;
        SetFamily R = PLA.R!;
        int firstPart = FirstPart![Output] + firstOutput;
        int lastPart  = LastPart![Output];
        int offset    = PartSize![Output] - firstOutput;

        SetdownCube();
        PartSize![Output] += offset;
        CubeSetup();

        PSet mask  = SetSave(FullSet);
        for (int i = firstPart; i < Size; i++)
            SetRemove(mask, i);
        PSet mask1 = SetSave(mask);
        for (int i = FirstPart![Output]; i < firstPart; i++)
            SetRemove(mask1, i);

        PLA.F = SfNew(F.Count + R.Count, Size);
        PLA.R = SfNew(F.Count + R.Count, Size);
        PLA.D = SfNew(D.Count, Size);

        for (int pi = 0; pi < F.Count; pi++)
        {
            PSet p  = ExpandToCurrentSize(F.GetSet(pi));
            PSet pf = PLA.F.GetSet(PLA.F.Count++);
            PSet pr = PLA.R.GetSet(PLA.R.Count++);
            InlineAnd(pf, mask, p);
            InlineAnd(pr, mask1, p);
            for (int i = firstPart; i <= lastPart; i++)
                if (IsInSet(p, i))
                    SetInsert(pf, i);
            bool save = false;
            for (int i = firstPart; i <= lastPart; i++)
            {
                if (IsInSet(p, i))
                {
                    save = true;
                    SetInsert(pr, i + offset);
                }
            }
            if (!save) PLA.R.Count--;
        }

        for (int pi = 0; pi < R.Count; pi++)
        {
            PSet p  = ExpandToCurrentSize(R.GetSet(pi));
            PSet pf = PLA.F.GetSet(PLA.F.Count++);
            PSet pr = PLA.R.GetSet(PLA.R.Count++);
            InlineAnd(pf, mask1, p);
            InlineAnd(pr, mask, p);
            bool save = false;
            for (int i = firstPart; i <= lastPart; i++)
            {
                if (IsInSet(p, i))
                {
                    save = true;
                    SetInsert(pf, i + offset);
                }
            }
            if (!save) PLA.F.Count--;
            for (int i = firstPart; i <= lastPart; i++)
                if (IsInSet(p, i))
                    SetInsert(pr, i);
        }

        for (int pi = 0; pi < D.Count; pi++)
        {
            PSet p  = ExpandToCurrentSize(D.GetSet(pi));
            PSet pf = PLA.D.GetSet(PLA.D.Count++);
            InlineAnd(pf, mask, p);
            for (int i = firstPart; i <= lastPart; i++)
            {
                if (IsInSet(p, i))
                {
                    SetInsert(pf, i);
                    SetInsert(pf, i + offset);
                }
            }
        }
    }

    private static PSet ExpandToCurrentSize(PSet p)
    {
        if (Nelem(p) >= Size)
            return p;

        PSet expanded = SetNew(Size);
        SetCopy(expanded, p);
        return expanded;
    }

    // -----------------------------------------------------------------------
    // set_phase — rearrange covers according to phase cube
    // -----------------------------------------------------------------------

    public static Pla SetPhase(Pla PLA)
    {
        PSet outmask = VarMask![NumVars - 1];
        PSet temp   = Temp![0];
        PSet phase  = PLA.Phase;
        PSet phase1 = Temp![1];

        SetDiff(phase1, outmask, phase);
        SetOr(phase1, SetDiff(temp, FullSet, outmask), phase1);

        SetFamily F1 = SfNew(PLA.F!.Count + PLA.R!.Count, Size);
        SetFamily R1 = SfNew(PLA.F!.Count + PLA.R!.Count, Size);

        for (int pi = 0; pi < PLA.F.Count; pi++)
        {
            PSet p = PLA.F.GetSet(pi);
            if (!SetpDisjoint(SetAnd(temp, p, phase), outmask))
                SetCopy(F1.GetSet(F1.Count++), temp);
            if (!SetpDisjoint(SetAnd(temp, p, phase1), outmask))
                SetCopy(R1.GetSet(R1.Count++), temp);
        }

        for (int pi = 0; pi < PLA.R.Count; pi++)
        {
            PSet p = PLA.R.GetSet(pi);
            if (!SetpDisjoint(SetAnd(temp, p, phase), outmask))
                SetCopy(R1.GetSet(R1.Count++), temp);
            if (!SetpDisjoint(SetAnd(temp, p, phase1), outmask))
                SetCopy(F1.GetSet(F1.Count++), temp);
        }

        PLA.F = F1;
        PLA.R = R1;
        return PLA;
    }

    // -----------------------------------------------------------------------
    // opoall — exhaustive phase search
    // -----------------------------------------------------------------------

    public static void Opoall(Pla PLA, int firstOutput, int lastOutput, int opoStrategy)
    {
        _opoExact = opoStrategy;

        if (!PLA.Phase.IsNull)
        {
            // set_free no-op
        }

        PSet bestphase = SetSave(FullSet);
        SetFamily bestF = SfSave(PLA.F!);
        SetFamily bestD = SfSave(PLA.D!);
        SetFamily bestR = SfSave(PLA.R!);

        int numOutputs = lastOutput - firstOutput + 1;
        int pow2 = 1 << numOutputs;

        for (int i = 0; i < pow2; i++)
        {
            SetFamily F = SfSave(PLA.F!);
            SetFamily D = SfSave(PLA.D!);
            SetFamily R = SfSave(PLA.R!);

            PLA.Phase = SetSave(FullSet);
            int num = i;
            for (int j = lastOutput; j >= firstOutput; j--)
            {
                if (num % 2 == 0)
                {
                    int ind = FirstPart![Output] + j;
                    SetRemove(PLA.Phase, ind);
                }
                num /= 2;
            }

            SetPhase(PLA);
            Console.Write($"# phase is {CvrOut.Pc1(PLA.Phase)}\n");
            Globals.Summary = true;
            Minimize(PLA);

            if (PLA.F!.Count < bestF.Count)
            {
                SetCopy(bestphase, PLA.Phase);
                bestF = PLA.F;
                bestD = PLA.D!;
                bestR = PLA.R!;
            }

            PLA.F = F;
            PLA.D = D;
            PLA.R = R;
        }

        PLA.Phase = bestphase;
        PLA.F = bestF;
        PLA.D = bestD;
        PLA.R = bestR;
    }

    // -----------------------------------------------------------------------
    // minimize (private helper)
    // -----------------------------------------------------------------------

    private static void Minimize(Pla PLA)
    {
        if (_opoExact != 0)
        {
            long t = Stubs.PTime();
            PLA.F = Stubs.MinimizeExact(PLA.F!, PLA.D!, PLA.R!, 1);
            if (Globals.Summary) CvrMisc.SizeStamp(PLA.F, "EXACT     ");
        }
        else
        {
            long t = Stubs.PTime();
            PLA.F = Stubs.Espresso(PLA.F!, PLA.D!, PLA.R!);
            if (Globals.Summary) CvrMisc.SizeStamp(PLA.F, "ESPRESSO  ");
        }
    }
}
