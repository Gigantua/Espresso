namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;

public static class Verify
{
    // -----------------------------------------------------------------------
    // verify — check that F and Fold cover each other modulo Dold
    // -----------------------------------------------------------------------

    /// <summary>
    /// verify — check that all minterms of F are in (Fold u Dold) and
    /// all minterms of Fold are in (F u Dold).
    /// Returns true if a verification error was found.
    /// </summary>
    public static bool VerifyCovers(SetFamily F, SetFamily Fold, SetFamily Dold)
    {
        bool verifyError = false;

        PSet[] FD = Cofactor.Cube2List(Fold, Dold);
        for (int i = 0; i < F.Count; i++)
        {
            PSet p = F.GetSet(i);
            if (!Stubs.CubeIsCovered(FD, p))
            {
                Console.WriteLine("some minterm in F is not covered by Fold u Dold");
                verifyError = true;
                if (!Globals.VerboseDebug) break;
                else Console.WriteLine(CvrOut.Pc1(p));
            }
        }
        Stubs.FreeCubelist(FD);

        FD = Cofactor.Cube2List(F, Dold);
        for (int i = 0; i < Fold.Count; i++)
        {
            PSet p = Fold.GetSet(i);
            if (!Stubs.CubeIsCovered(FD, p))
            {
                Console.WriteLine("some minterm in Fold is not covered by F u Dold");
                verifyError = true;
                if (!Globals.VerboseDebug) break;
                else Console.WriteLine(CvrOut.Pc1(p));
            }
        }
        Stubs.FreeCubelist(FD);

        return verifyError;
    }

    // -----------------------------------------------------------------------
    // PLA_verify — verify that two PLAs are identical
    // -----------------------------------------------------------------------

    /// <summary>
    /// PLA_verify — verify that two PLAs are identical (by permuting columns
    /// to match names, then checking cover equivalence).
    /// Returns true if a verification error was found.
    /// </summary>
    public static bool PlaVerify(Pla PLA1, Pla PLA2)
    {
        if (PLA1.Label != null && PLA1.Label.Length > 0 && PLA1.Label[0] != null &&
            PLA2.Label != null && PLA2.Label.Length > 0 && PLA2.Label[0] != null)
        {
            PlaPermute(PLA1, PLA2);
        }
        else
        {
            Console.Error.WriteLine("Warning: cannot permute columns without names");
            return true;
        }

        if (PLA1.F!.SfSize != PLA2.F!.SfSize)
        {
            Console.Error.WriteLine("PLA_verify: PLA's are not the same size");
            return true;
        }

        return VerifyCovers(PLA2.F!, PLA1.F!, PLA1.D!);
    }

    // -----------------------------------------------------------------------
    // PLA_permute — permute columns of PLA1 to match PLA2's column order
    // -----------------------------------------------------------------------

    /// <summary>
    /// PLA_permute — permute the columns of PLA1 so they match the order of PLA2.
    /// Columns are matched by label name; unmatched columns are discarded.
    /// </summary>
    public static void PlaPermute(Pla PLA1, Pla PLA2)
    {
        int npermute = 0;
        int[] permute = new int[PLA2.F!.SfSize];

        for (int i = 0; i < PLA2.F.SfSize; i++)
        {
            string? labi = PLA2.Label![i];
            for (int j = 0; j < PLA1.F!.SfSize; j++)
            {
                if (labi == PLA1.Label![j])
                {
                    permute[npermute++] = j;
                    break;
                }
            }
        }

        if (PLA1.F != null)
            PLA1.F = SfPermute(SfSave(PLA1.F), permute, npermute);
        if (PLA1.R != null)
            PLA1.R = SfPermute(SfSave(PLA1.R), permute, npermute);
        if (PLA1.D != null)
            PLA1.D = SfPermute(SfSave(PLA1.D), permute, npermute);

        string?[] label = new string?[Size];
        for (int i = 0; i < npermute; i++)
            label[i] = PLA1.Label![permute[i]];
        for (int i = npermute; i < Size; i++)
            label[i] = null;
        PLA1.Label = label;
    }

    // -----------------------------------------------------------------------
    // check_consistency — verify ON/OFF/DC sets partition the Boolean space
    // -----------------------------------------------------------------------

    /// <summary>
    /// check_consistency — test that the ON-set, OFF-set and DC-set form a
    /// partition of the Boolean space.
    /// Returns true if a consistency error was found.
    /// </summary>
    public static bool CheckConsistency(Pla PLA)
    {
        bool verifyError = false;

        SetFamily T = Sharp.CvIntersect(PLA.F!, PLA.D!);
        if (T.Count == 0)
            Console.WriteLine("ON-SET and DC-SET are disjoint");
        else
        {
            Console.WriteLine("Some minterm(s) belong to both the ON-SET and DC-SET !");
            if (Globals.VerboseDebug) CvrOut.Cprint(T);
            verifyError = true;
        }
        SfFree(T);

        T = Sharp.CvIntersect(PLA.F!, PLA.R!);
        if (T.Count == 0)
            Console.WriteLine("ON-SET and OFF-SET are disjoint");
        else
        {
            Console.WriteLine("Some minterm(s) belong to both the ON-SET and OFF-SET !");
            if (Globals.VerboseDebug) CvrOut.Cprint(T);
            verifyError = true;
        }
        SfFree(T);

        T = Sharp.CvIntersect(PLA.D!, PLA.R!);
        if (T.Count == 0)
            Console.WriteLine("DC-SET and OFF-SET are disjoint");
        else
        {
            Console.WriteLine("Some minterm(s) belong to both the OFF-SET and DC-SET !");
            if (Globals.VerboseDebug) CvrOut.Cprint(T);
            verifyError = true;
        }
        SfFree(T);

        PSet[] union = Cofactor.Cube3List(PLA.F!, PLA.D!, PLA.R!);
        if (Irred.Tautology(union))
            Console.WriteLine("Union of ON-SET, OFF-SET and DC-SET is the universe");
        else
        {
            T = Compl.Complement(Cofactor.Cube3List(PLA.F!, PLA.D!, PLA.R!));
            Console.WriteLine("There are minterms left unspecified !");
            if (Globals.VerboseDebug) CvrOut.Cprint(T);
            verifyError = true;
            SfFree(T);
        }
        Stubs.FreeCubelist(union);

        return verifyError;
    }
}
