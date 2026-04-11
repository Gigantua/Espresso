namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static Stubs;

/// <summary>
/// Equiv — equivalence checking and variable reduction.
/// Translated from equiv.c.
/// Functions:
///   - EquivalentVariables() — find variables that always have the same value
///   - EquivSweep() — sweep to find equivalences
///   - RemoveEquivalences() — simplify cover by removing equivalent variables
///   - RenameVariables() — rename variables after equivalence elimination
///   - CheckEquiv() — check if two set families are equivalent
///   - FindEquivOutputs() — find outputs that always have the same value
/// </summary>
public static class Equiv
{
    // -----------------------------------------------------------------------
    // find_equiv_outputs — find outputs that are equivalent
    // -----------------------------------------------------------------------

    /// <summary>
    /// Find outputs of a PLA that are equivalent.
    /// Outputs can be equivalent, complementary equivalent, etc.
    /// </summary>
    public static void FindEquivOutputs(Pla PLA)
    {
        if (PLA == null)
            throw new ArgumentNullException(nameof(PLA));

        int some_equiv = 0;

        CvrOut.MakeupLabels(PLA);

        // Allocate arrays for ON and OFF sets of each output
        var F = new SetFamily[CubeContext.PartSize![CubeContext.Output]];
        var R = new SetFamily[CubeContext.PartSize![CubeContext.Output]];

        for (int i = 0; i < CubeContext.PartSize![CubeContext.Output]; i++)
        {
            int ipart = CubeContext.FirstPart![CubeContext.Output] + i;
            R[i] = CofactorOutput(PLA.R, ipart);
            F[i] = Complement(Cube1List(R[i]));
        }

        // Compare all pairs of outputs
        for (int i = 0; i < CubeContext.PartSize![CubeContext.Output] - 1; i++)
        {
            for (int j = i + 1; j < CubeContext.PartSize![CubeContext.Output]; j++)
            {
                int ipart = CubeContext.FirstPart![CubeContext.Output] + i;
                int jpart = CubeContext.FirstPart![CubeContext.Output] + j;

                if (CheckEquiv(F[i], F[j]))
                {
                    Console.WriteLine($"# Outputs {i} and {j} ({PLA.Label![ipart]} and {PLA.Label![jpart]}) are equivalent");
                    some_equiv = 1;
                }
                else if (CheckEquiv(F[i], R[j]))
                {
                    Console.WriteLine($"# Outputs {i} and NOT {j} ({PLA.Label![ipart]} and {PLA.Label![jpart]}) are equivalent");
                    some_equiv = 1;
                }
                else if (CheckEquiv(R[i], F[j]))
                {
                    Console.WriteLine($"# Outputs NOT {i} and {j} ({PLA.Label![ipart]} and {PLA.Label![jpart]}) are equivalent");
                    some_equiv = 1;
                }
                else if (CheckEquiv(R[i], R[j]))
                {
                    Console.WriteLine($"# Outputs NOT {i} and NOT {j} ({PLA.Label![ipart]} and {PLA.Label![jpart]}) are equivalent");
                    some_equiv = 1;
                }
            }
        }

        if (some_equiv == 0)
        {
            Console.WriteLine("# No outputs are equivalent");
        }

        // Clean up
        for (int i = 0; i < CubeContext.PartSize![CubeContext.Output]; i++)
        {
            SetFamily.SfFree(F[i]);
            SetFamily.SfFree(R[i]);
        }
    }

    // -----------------------------------------------------------------------
    // check_equiv — check if two set families are equivalent
    // -----------------------------------------------------------------------

    /// <summary>
    /// Check if two set families f1 and f2 are equivalent.
    /// They are equivalent if f1 covers all of f2 AND f2 covers all of f1.
    /// </summary>
    public static bool CheckEquiv(SetFamily f1, SetFamily f2)
    {
        if (f1 == null || f2 == null)
            return false;

        // Convert f1 and f2 to cube lists
        PSet[] f1list = Cube1List(f1);
        PSet[] f2list = Cube1List(f2);

        // Check if every set in f2 is covered by some set in f1
        int idx = 2;
        while (!f2list[idx].IsNull)
        {
            PSet p = f2list[idx];
            if (!CubeIsCovered(f1list, p))
            {
                FreeCubelist(f1list);
                FreeCubelist(f2list);
                return false;
            }
            idx++;
        }

        // Check if every set in f1 is covered by some set in f2
        idx = 2;
        while (!f1list[idx].IsNull)
        {
            PSet p = f1list[idx];
            if (!CubeIsCovered(f2list, p))
            {
                FreeCubelist(f1list);
                FreeCubelist(f2list);
                return false;
            }
            idx++;
        }

        FreeCubelist(f1list);
        FreeCubelist(f2list);
        return true;
    }

    // -----------------------------------------------------------------------
    // Helper functions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cofactor a cover with respect to an output variable.
    /// This is a wrapper that gets the output part and cofactors.
    /// </summary>
    private static SetFamily CofactorOutput(SetFamily? R, int part)
    {
        if (R == null)
            return SetFamily.SfNew(0, Size);

        // For each cube in R, check if it has the specified output part
        var result = SetFamily.SfNew(R.Count, Size);
        int resultIdx = 0;

        for (int i = 0; i < R.Count; i++)
        {
            PSet cube = R.GetSet(i);
            // Check if this cube has the output variable set in the specified part
            if (IsPartSet(cube, part))
            {
                // Copy this cube to the result
                Array.Copy(cube._d, cube._o, result.Data, resultIdx * result.WSize, result.WSize);
                resultIdx++;
            }
        }

        result.Count = resultIdx;
        result.ActiveCount = resultIdx;
        return result;
    }

    /// <summary>
    /// Check if a specific part (variable bit) is set in a cube.
    /// </summary>
    private static bool IsPartSet(PSet cube, int part)
    {
        int word = WhichWord(part);
        uint mask = (uint)(1 << (part % 32));
        return (cube[word] & mask) != 0;
    }

    /// <summary>
    /// Check if a cube p is covered by any cube in the cube list.
    /// </summary>
    private static bool CubeIsCovered(PSet[] cubeList, PSet p)
    {
        int idx = 2;
        while (!cubeList[idx].IsNull)
        {
            PSet c = cubeList[idx];
            // Check if c contains p (c is a superset of p)
            bool covers = true;
            int loop = Loop(p);
            for (int i = 1; i <= loop; i++)
            {
                if ((c[i] & p[i]) != p[i])
                {
                    covers = false;
                    break;
                }
            }
            if (covers)
                return true;
            idx++;
        }
        return false;
    }
}
