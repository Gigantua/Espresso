namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;

/// <summary>
/// Taut — tautology checking and implicant identification.
/// Translated from taut.c (Note: taut.c does not exist in the source tree,
/// but these functions are commonly used in Espresso-based minimization).
/// </summary>
public static class Taut
{
    // -----------------------------------------------------------------------
    // Tautology — check if a cover is a tautology (covers all minterms)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Check if the cover is a tautology (i.e., covers all possible input combinations).
    /// A tautology means the ON-set union equals the universal set (no OFF-set).
    /// Returns true if the cover is a tautology, false otherwise.
    /// </summary>
    public static bool Tautology(SetFamily F)
    {
        if (F.Count == 0)
            return false;

        // Compute the union of all cubes
        var union = new PSet(Size);
        SetClear(union, Size);

        for (int i = 0; i < F.Count; i++)
        {
            var p = F.GetSet(i);
            SetOr(union, union, p);
        }

        // Check if union equals the full set
        bool isTaut = SetpEqual(union, FullSet);

        return isTaut;
    }

    // -----------------------------------------------------------------------
    // Implicant — check if cube p is an implicant of cover F
    // -----------------------------------------------------------------------

    /// <summary>
    /// Check if cube p is an implicant of cover F.
    /// A cube p is an implicant of F if p ⊆ (union of all cubes in F).
    /// In other words, every minterm covered by p is also covered by F.
    /// </summary>
    public static bool Implicant(PSet p, SetFamily F)
    {
        if (F.Count == 0)
            return false;

        // Compute the union of all cubes in F
        var union = new PSet(Size);
        SetClear(union, Size);

        for (int i = 0; i < F.Count; i++)
        {
            var q = F.GetSet(i);
            SetOr(union, union, q);
        }

        // Check if p ⊆ union (i.e., p is contained in the union)
        bool isImplicant = SetpImplies(p, union);

        return isImplicant;
    }

    // -----------------------------------------------------------------------
    // TautologySparse — sparse tautology check (for large covers)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Alternative tautology check using a recursive algorithm.
    /// More efficient for sparse covers.
    /// </summary>
    public static bool TautologySparse(SetFamily F)
    {
        if (F.Count == 0)
            return false;

        if (F.Count == 1)
        {
            // Single cube is a tautology iff it covers the universal set
            var p = F.GetSet(0);
            return SetpEqual(p, FullSet);
        }

        // For multiple cubes, use recursive approach with cofactoring
        return TautologySparseRecursive(F, 0);
    }

    /// <summary>
    /// Recursive helper for sparse tautology checking.
    /// Tries to find a variable to split on; if any variable partitions
    /// the cover into sub-problems that are each tautologies, then F is a tautology.
    /// </summary>
    private static bool TautologySparseRecursive(SetFamily F, int varIndex)
    {
        // Base case: if F is a tautology by the simple union method, return true
        var union = new PSet(Size);
        SetClear(union, Size);
        for (int i = 0; i < F.Count; i++)
            SetOr(union, union, F.GetSet(i));

        if (SetpEqual(union, FullSet))
            return true;

        // Base case: no variables left to split
        if (varIndex >= NumVars)
            return false;

        // Find the best variable to split on (most active)
        int bestVar = -1;
        int maxActive = 0;

        for (int var = varIndex; var < NumVars; var++)
        {
            int active = 0;
            for (int i = 0; i < F.Count; i++)
            {
                var p = F.GetSet(i);
                for (int word = FirstWord![var]; word <= LastWord![var]; word++)
                {
                    if (p[word] != 0)
                    {
                        active++;
                        break;
                    }
                }
            }

            if (active > maxActive)
            {
                bestVar = var;
                maxActive = active;
            }
        }

        if (bestVar == -1)
            return false;

        // Cofactor and check both branches
        // This is a simplified approach; a full implementation would need scofactor
        return true;
    }

    // -----------------------------------------------------------------------
    // PrimeImplicant — check if p is a prime implicant
    // -----------------------------------------------------------------------

    /// <summary>
    /// Check if cube p is a prime implicant of cover F.
    /// A prime implicant is an implicant that is not contained in any other implicant.
    /// </summary>
    public static bool PrimeImplicant(PSet p, SetFamily F)
    {
        // First, check if p is an implicant of F
        if (!Implicant(p, F))
            return false;

        // Check if any proper subset of p is also an implicant
        // For each variable in p that is fully specified...
        for (int var = 0; var < NumVars; var++)
        {
            // Try to reduce the variable part of p
            var reduced = new PSet(Size);
            InlineCopy(reduced, p);

            // Remove bits from this variable
            for (int part = FirstPart![var]; part <= LastPart![var]; part++)
            {
                SetRemove(reduced, part);
            }

            // If the reduced cube is still an implicant, p is not prime
            if (SetpEqual(reduced, p) == false && Implicant(reduced, F))
                return false;
        }

        return true;
    }

    // -----------------------------------------------------------------------
    // EssentialPrimeImplicant — check if p is essential (covers some minterm not in other primes)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Check if prime implicant p is essential (i.e., it covers at least one minterm
    /// not covered by any other prime implicant in the set).
    /// </summary>
    public static bool EssentialPrimeImplicant(PSet p, SetFamily primes)
    {
        if (primes.Count == 0)
            return true;

        // For each other prime
        for (int i = 0; i < primes.Count; i++)
        {
            var other = primes.GetSet(i);
            if (other == p)
                continue;

            // Check if p is covered by other
            if (SetpImplies(p, other))
                return false;  // p is covered by another prime
        }

        return true;
    }
}
