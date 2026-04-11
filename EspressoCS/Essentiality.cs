namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;

/// <summary>
/// Essentiality — essentiality computation and non-essential cube reduction.
/// Translated from essentiality.c.
/// Module for performing essentiality test and reduction of signature cubes.
/// </summary>
public static class Essentiality
{
    // Global state for recursive essentiality testing
    private static int[]? c_free_list;              // List of raised variables in cube c
    private static int c_free_count;                // active size

    private static int[]? r_free_list;              // List of subset of raised variables in cube c raised in each cube of offset R
    private static int r_free_count;                // active size

    private static int[]? reduced_c_free_list;      // c_free_list - r_free_list
    private static int reduced_c_free_count;        // active size

    private class VarInfo
    {
        public int variable;
        public int free_count;
    }

    private static VarInfo[]? unate_list;           // List of unate variables
    private static int unate_count;                 // active size

    private static VarInfo[]? binate_list;          // List of binate variables
    private static int binate_count;                // active size

    private static int[]? variable_order;           // permutation of reduced c_free_count
    private static int variable_count;              // active size
    private static int variable_head;               // current position

    private static SetFamily? COVER;                // Global cover of inessential signature cubes

    // -----------------------------------------------------------------------
    // ComputeEssentiality — main entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// ComputeEssentiality — compute essentiality of given signature cube.
    /// Determines essentiality of the given signature cube and returns
    /// cover of the signature cube if found inessential.
    /// </summary>
    public static SetFamily ComputeEssentiality(SetFamily F, SetFamily E, SetFamily R, PSet c, PSet d)
    {
        int num_binary_vars = NumVars;
        
        // Allocate memory for working lists
        c_free_list = new int[num_binary_vars];
        r_free_list = new int[num_binary_vars];
        reduced_c_free_list = new int[num_binary_vars];
        unate_list = new VarInfo[num_binary_vars];
        binate_list = new VarInfo[num_binary_vars];
        variable_order = new int[num_binary_vars];

        for (int i = 0; i < num_binary_vars; i++)
        {
            unate_list[i] = new VarInfo();
            binate_list[i] = new VarInfo();
        }

        // 1. Identify free variables of cube c
        c_free_count = 0;
        for (int v = 0; v < num_binary_vars; v++)
        {
            int e0 = v << 1;
            int e1 = e0 + 1;
            if (IsInSet(d, e0) && IsInSet(d, e1))
            {
                c_free_list[c_free_count++] = v;
            }
        }

        // 2. Identify corresponding free variables of R
        r_free_count = 0;
        reduced_c_free_count = 0;
        
        for (int i = 0; i < c_free_count; i++)
        {
            int v = c_free_list[i];
            int e0 = v << 1;
            int e1 = e0 + 1;
            bool free_var = true;
            
            for (int j = 0; j < R.Count; j++)
            {
                PSet r = R.GetSet(j);
                if (!IsInSet(r, e0) || !IsInSet(r, e1))
                {
                    free_var = false;
                    break;
                }
            }
            
            if (free_var)
            {
                r_free_list[r_free_count++] = v;
            }
            else
            {
                reduced_c_free_list[reduced_c_free_count++] = v;
            }
        }

        // 3. Identify unate and binate variables and sort them
        unate_count = 0;
        binate_count = 0;
        
        for (int i = 0; i < reduced_c_free_count; i++)
        {
            int v = reduced_c_free_list[i];
            int e0 = v << 1;
            int e1 = e0 + 1;
            int even_count = 0;
            int odd_count = 0;
            int free_count = 0;
            
            for (int j = 0; j < R.Count; j++)
            {
                PSet r = R.GetSet(j);
                bool odd = IsInSet(r, e0);
                bool even = IsInSet(r, e1);
                
                if (odd && even)
                    free_count++;
                else if (odd)
                    odd_count++;
                else
                    even_count++;
            }
            
            if (odd_count == 0 || even_count == 0)
            {
                unate_list[unate_count].variable = v;
                unate_list[unate_count].free_count = free_count;
                unate_count++;
            }
            else
            {
                binate_list[binate_count].variable = v;
                binate_list[binate_count].free_count = free_count;
                binate_count++;
            }
        }

        // Sort unate and binate lists by free_count (ascending)
        System.Array.Sort(unate_list, 0, unate_count, new VarInfoComparer());
        System.Array.Sort(binate_list, 0, binate_count, new VarInfoComparer());

        // 4. Build variable order: binate first, then unate
        variable_head = 0;
        variable_count = 0;
        
        for (int i = 0; i < binate_count; i++)
        {
            variable_order[variable_count++] = binate_list[i].variable;
        }
        
        for (int i = 0; i < unate_count; i++)
        {
            variable_order[variable_count++] = unate_list[i].variable;
        }

        // 5. Initialize cover and perform recursive reduction
        COVER = SfNew(10, Size);
        
        AuxComputeEssentiality(F, E, R, c, d);

        SetFamily result = COVER;
        COVER = null;

        // Cleanup
        c_free_list = null;
        r_free_list = null;
        reduced_c_free_list = null;
        unate_list = null;
        binate_list = null;
        variable_order = null;

        return result;
    }

    /// <summary>
    /// VarInfoComparer — comparator for sorting VarInfo by free_count (ascending).
    /// </summary>
    private class VarInfoComparer : System.Collections.Generic.IComparer<VarInfo>
    {
        public int Compare(VarInfo? x, VarInfo? y)
        {
            if (x == null || y == null) return 0;
            
            if (x.free_count > y.free_count) return 1;
            if (x.free_count < y.free_count) return -1;
            return 0;
        }
    }

    // -----------------------------------------------------------------------
    // AuxComputeEssentiality — recursive routine for essentiality reduction
    // -----------------------------------------------------------------------

    /// <summary>
    /// AuxComputeEssentiality — main recursive routine for reducing inessential signature cube.
    /// </summary>
    private static void AuxComputeEssentiality(SetFamily F, SetFamily E, SetFamily R, PSet c, PSet d)
    {
        if (COVER == null) return;

        // Special case: if d is already covered by F ∪ E ∪ COVER
        PSet[] local_dc = Stubs.Cube2List(F, E);
        if (Stubs.CubeIsCovered(local_dc, d))
        {
            Stubs.FreeCubelist(local_dc);
            return;
        }

        // Check if all minterms of d have been used up (end of recursion)
        if (variable_head >= variable_count!)
        {
            // We've explored all variables; if d is not covered, add it to COVER
            SetFamily minterms = GetMinterms(d);
            for (int i = 0; i < minterms.Count; i++)
            {
                PSet d_minterm = minterms.GetSet(i);
                if (!Stubs.CubeIsCovered(local_dc, d_minterm))
                {
                    // Found an uncovered minterm; add sigma(d_minterm) to cover
                    PSet sigma_d = GetSigma(R, d_minterm);
                    COVER = SfAddSet(COVER, sigma_d);
                    SetFree(sigma_d);
                }
            }
            SfFree(minterms);
            Stubs.FreeCubelist(local_dc);
            return;
        }

        Stubs.FreeCubelist(local_dc);

        // Get next variable to branch on
        int v_index = variable_order![variable_head];
        int e0 = v_index << 1;
        int e1 = e0 + 1;

        variable_head++;

        // Try removing e1 (lower bit)
        SetRemove(d, e1);
        AuxComputeEssentiality(F, E, R, c, d);
        SetInsert(d, e1);

        // Try removing e0 (upper bit)
        SetRemove(d, e0);
        AuxComputeEssentiality(F, E, R, c, d);
        SetInsert(d, e0);

        variable_head--;
    }

    // -----------------------------------------------------------------------
    // EssenPartOfCube — check if cube is part of essentiality set
    // -----------------------------------------------------------------------

    /// <summary>
    /// EssenPartOfCube — check if a cube is part of essentiality computation.
    /// Helper function to test essentiality constraints.
    /// </summary>
    public static bool EssenPartOfCube(SetFamily F, PSet cube)
    {
        for (int i = 0; i < F.Count; i++)
        {
            PSet p = F.GetSet(i);
            if (SetpImplies(p, cube))
                return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // EssenCube — essentiality check for a single cube
    // -----------------------------------------------------------------------

    /// <summary>
    /// EssenCube — check if a single cube is essential.
    /// </summary>
    public static bool EssenCube(SetFamily F, SetFamily D, PSet c)
    {
        // Check using Essen module if available
        return Essen.EssenCube(F, D, c);
    }

    // -----------------------------------------------------------------------
    // EssenExpand — check essentiality of expanded cube
    // -----------------------------------------------------------------------

    /// <summary>
    /// EssenExpand — check essentiality of an expanded cube.
    /// </summary>
    public static bool EssenExpand(SetFamily F, SetFamily D, SetFamily R, PSet p)
    {
        // Use expand module to check expansion essentiality
        return !TestP(p, NonEssen);
    }

    // -----------------------------------------------------------------------
    // ComputeNonEssentials — compute non-essential cubes in F
    // -----------------------------------------------------------------------

    /// <summary>
    /// ComputeNonEssentials — compute non-essential cubes from F.
    /// Returns cover of non-essential prime implicants.
    /// </summary>
    public static SetFamily ComputeNonEssentials(SetFamily F, SetFamily D)
    {
        SetFamily nonessentials = SfNew(F.Count, Size);

        for (int i = 0; i < F.Count; i++)
        {
            PSet p = F.GetSet(i);
            
            // Check if this cube is marked as nonessential
            if (TestP(p, NonEssen))
            {
                nonessentials = SfAddSet(nonessentials, p);
            }
        }

        return nonessentials;
    }

    // -----------------------------------------------------------------------
    // GetMinterms — expand a cube into its minterms
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetMinterms — expand a cube into its minterms.
    /// </summary>
    private static SetFamily GetMinterms(PSet c)
    {
        SetFamily minterms = SfNew(1, Size);
        PSet d_minterm = SetNew(Size);
        
        SetCopy(d_minterm, c);
        SetAnd(d_minterm, d_minterm, BinaryMask!);
        
        for (int i = NumBinaryVars; i < NumVars; i++)
        {
            for (int j = FirstPart![i]; j <= LastPart![i]; j++)
            {
                if (IsInSet(c, j))
                {
                    SetInsert(d_minterm, j);
                    minterms = SfAddSet(minterms, d_minterm);
                    SetRemove(d_minterm, j);
                }
            }
        }
        
        SetFree(d_minterm);
        return minterms;
    }

    // -----------------------------------------------------------------------
    // GetSigma — compute signature/complement of cube with respect to offset
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetSigma — compute the signature (complement) of a minterm with respect to the offset.
    /// This returns the largest cube in offset that contains the minterm.
    /// </summary>
    private static PSet GetSigma(SetFamily R, PSet minterm)
    {
        PSet sigma = SetNew(Size);
        SetCopy(sigma, minterm);
        
        // Find all elements that are in all cubes of R that cover minterm
        for (int i = 0; i < R.Count; i++)
        {
            PSet r = R.GetSet(i);
            if (SetpImplies(r, minterm))
            {
                // This cube of R covers the minterm; intersect with sigma
                for (int j = 0; j < Loop(sigma); j++)
                {
                    sigma[j + 1] &= r[j + 1];
                }
            }
        }
        
        return sigma;
    }

    // -----------------------------------------------------------------------
    // EssenParts — determine parts that must be lowered
    // -----------------------------------------------------------------------

    /// <summary>
    /// EssenParts — determine which parts of RAISE must be lowered based on R and F.
    /// Stub implementation for GASP algorithm.
    /// </summary>
    public static void EssenParts(SetFamily R, SetFamily F, PSet RAISE, PSet FREESET)
    {
        /* Stub: no-op for now */
    }

    // -----------------------------------------------------------------------
    // EssenRaising — determine parts that can always be raised
    // -----------------------------------------------------------------------

    /// <summary>
    /// EssenRaising — determine which parts can always be raised without violating OFF-set.
    /// Stub implementation for GASP algorithm.
    /// </summary>
    public static void EssenRaising(SetFamily R, PSet RAISE, PSet FREESET)
    {
        /* Stub: no-op for now */
    }
}
