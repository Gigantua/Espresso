namespace EspressoCS;

using static SetOps;
using static CubeContext;

/// <summary>
/// SetC — cube-specific distance, consensus, and comparison operations.
/// Translated 1:1 from setc.c (BPI=32 only).
/// </summary>
public static class SetC
{
    // -----------------------------------------------------------------------
    // full_row — true if p | cof covers the full set in every word
    // -----------------------------------------------------------------------

    public static bool FullRow(PSet p, PSet cof)
    {
        int i = Loop(p);
        do
        {
            if ((p[i] | cof[i]) != FullSet[i]) return false;
        } while (--i > 0);
        return true;
    }

    // -----------------------------------------------------------------------
    // cdist0 — true if a and b are distance 0 apart (their intersection is non-null)
    // -----------------------------------------------------------------------

    public static bool Cdist0(PSet a, PSet b)
    {
        // Check binary variables
        {
            int last = InWord;
            if (last != -1)
            {
                uint x = a[last] & b[last];
                if ((~(x | (x >> 1)) & InMask) != 0) return false;

                for (int w = 1; w < last; w++)
                {
                    x = a[w] & b[w];
                    if ((~(x | (x >> 1)) & Disjoint) != 0) return false;
                }
            }
        }

        // Check multiple-valued variables
        {
            for (int var = NumBinaryVars; var < NumVars; var++)
            {
                PSet mask = VarMask![var];
                int last  = LastWord![var];
                bool found = false;
                for (int w = FirstWord![var]; w <= last; w++)
                    if ((a[w] & b[w] & mask[w]) != 0) { found = true; break; }
                if (!found) return false;
            }
        }

        return true;
    }

    // -----------------------------------------------------------------------
    // cdist01 — return distance, capped at 2 when it would exceed 1
    // -----------------------------------------------------------------------

    public static int Cdist01(PSet a, PSet b)
    {
        int dist = 0;

        // Check binary variables
        {
            int last = InWord;
            if (last != -1)
            {
                uint x = a[last] & b[last];
                x = ~(x | (x >> 1)) & InMask;
                if (x != 0)
                {
                    dist = CountOnes(x);
                    if (dist > 1) return 2;
                }

                for (int w = 1; w < last; w++)
                {
                    x = a[w] & b[w];
                    x = ~(x | (x >> 1)) & Disjoint;
                    if (x != 0)
                    {
                        if (dist == 1 || (dist += CountOnes(x)) > 1) return 2;
                    }
                }
            }
        }

        // Check multiple-valued variables
        {
            for (int var = NumBinaryVars; var < NumVars; var++)
            {
                PSet mask = VarMask![var];
                int last  = LastWord![var];
                bool found = false;
                for (int w = FirstWord![var]; w <= last; w++)
                    if ((a[w] & b[w] & mask[w]) != 0) { found = true; break; }
                if (!found && ++dist > 1) return 2;
            }
        }

        return dist;
    }

    // -----------------------------------------------------------------------
    // cdist — return the full distance between two cubes
    // -----------------------------------------------------------------------

    public static int Cdist(PSet a, PSet b)
    {
        int dist = 0;

        // Check binary variables
        {
            int last = InWord;
            if (last != -1)
            {
                uint x = a[last] & b[last];
                x = ~(x | (x >> 1)) & InMask;
                if (x != 0) dist = CountOnes(x);

                for (int w = 1; w < last; w++)
                {
                    x = a[w] & b[w];
                    x = ~(x | (x >> 1)) & Disjoint;
                    if (x != 0) dist += CountOnes(x);
                }
            }
        }

        // Check multiple-valued variables
        {
            for (int var = NumBinaryVars; var < NumVars; var++)
            {
                PSet mask = VarMask![var];
                int last  = LastWord![var];
                bool found = false;
                for (int w = FirstWord![var]; w <= last; w++)
                    if ((a[w] & b[w] & mask[w]) != 0) { found = true; break; }
                if (!found) dist++;
            }
        }

        return dist;
    }

    // -----------------------------------------------------------------------
    // force_lower — determine which variables of a do not intersect b
    // -----------------------------------------------------------------------

    public static PSet ForceLower(PSet xlower, PSet a, PSet b)
    {
        // Check binary variables
        {
            int last = InWord;
            if (last != -1)
            {
                uint x = a[last] & b[last];
                x = ~(x | (x >> 1)) & InMask;
                if (x != 0) xlower[last] |= (x | (x << 1)) & a[last];

                for (int w = 1; w < last; w++)
                {
                    x = a[w] & b[w];
                    x = ~(x | (x >> 1)) & Disjoint;
                    if (x != 0) xlower[w] |= (x | (x << 1)) & a[w];
                }
            }
        }

        // Check multiple-valued variables
        {
            for (int var = NumBinaryVars; var < NumVars; var++)
            {
                PSet mask = VarMask![var];
                int last  = LastWord![var];
                bool found = false;
                for (int w = FirstWord![var]; w <= last; w++)
                    if ((a[w] & b[w] & mask[w]) != 0) { found = true; break; }
                if (!found)
                    for (int w = FirstWord![var]; w <= last; w++)
                        xlower[w] |= a[w] & mask[w];
            }
        }

        return xlower;
    }

    // -----------------------------------------------------------------------
    // consensus — multiple-valued consensus of two cubes
    // -----------------------------------------------------------------------

    public static void Consensus(PSet r, PSet a, PSet b)
    {
        InlineClear(r, Size);

        // Check binary variables
        {
            int last = InWord;
            if (last != -1)
            {
                uint x = a[last] & b[last];
                r[last] = x;
                x = ~(x | (x >> 1)) & InMask;
                if (x != 0) r[last] |= (x | (x << 1)) & (a[last] | b[last]);

                for (int w = 1; w < last; w++)
                {
                    x = a[w] & b[w];
                    r[w] = x;
                    x = ~(x | (x >> 1)) & Disjoint;
                    if (x != 0) r[w] |= (x | (x << 1)) & (a[w] | b[w]);
                }
            }
        }

        // Check multiple-valued variables
        {
            for (int var = NumBinaryVars; var < NumVars; var++)
            {
                PSet mask = VarMask![var];
                int last  = LastWord![var];
                bool empty = true;
                for (int w = FirstWord![var]; w <= last; w++)
                {
                    uint x = a[w] & b[w] & mask[w];
                    if (x != 0) { empty = false; r[w] |= x; }
                }
                if (empty)
                    for (int w = FirstWord![var]; w <= last; w++)
                        r[w] |= mask[w] & (a[w] | b[w]);
            }
        }
    }

    // -----------------------------------------------------------------------
    // cactive — return index of the single active variable, or -1
    // -----------------------------------------------------------------------

    public static int Cactive(PSet a)
    {
        int active = -1, dist = 0;

        // Check binary variables
        {
            int last = InWord;
            if (last != -1)
            {
                uint x = a[last];
                x = ~(x & (x >> 1)) & InMask;
                if (x != 0)
                {
                    dist = CountOnes(x);
                    if (dist > 1) return -1;
                    active = (last - 1) * (Bpi / 2) + BitIndex(x) / 2;
                }

                for (int w = 1; w < last; w++)
                {
                    x = a[w];
                    x = ~(x & (x >> 1)) & Disjoint;
                    if (x != 0)
                    {
                        dist += CountOnes(x);
                        if (dist > 1) return -1;
                        active = (w - 1) * (Bpi / 2) + BitIndex(x) / 2;
                    }
                }
            }
        }

        // Check multiple-valued variables
        {
            for (int var = NumBinaryVars; var < NumVars; var++)
            {
                PSet mask = VarMask![var];
                int last  = LastWord![var];
                for (int w = FirstWord![var]; w <= last; w++)
                {
                    if ((mask[w] & ~a[w]) != 0)
                    {
                        if (++dist > 1) return -1;
                        active = var;
                        break;
                    }
                }
            }
        }

        return active;
    }

    // -----------------------------------------------------------------------
    // ccommon — true if a and b share at least one active variable (wrt cof)
    // -----------------------------------------------------------------------

    public static bool Ccommon(PSet a, PSet b, PSet cof)
    {
        // Check binary variables
        {
            int last = InWord;
            if (last != -1)
            {
                uint x = a[last] | cof[last];
                uint y = b[last] | cof[last];
                if ((~(x & (x >> 1)) & ~(y & (y >> 1)) & InMask) != 0) return true;

                for (int w = 1; w < last; w++)
                {
                    x = a[w] | cof[w];
                    y = b[w] | cof[w];
                    if ((~(x & (x >> 1)) & ~(y & (y >> 1)) & Disjoint) != 0) return true;
                }
            }
        }

        // Check multiple-valued variables
        {
            for (int var = NumBinaryVars; var < NumVars; var++)
            {
                PSet mask = VarMask![var];
                int last  = LastWord![var];

                // Check for some part missing from a (wrt cof)
                for (int w = FirstWord![var]; w <= last; w++)
                {
                    if ((mask[w] & ~a[w] & ~cof[w]) != 0)
                    {
                        // a has a missing part; check if b also does
                        for (int w2 = FirstWord![var]; w2 <= last; w2++)
                            if ((mask[w2] & ~b[w2] & ~cof[w2]) != 0)
                                return true;
                        break;
                    }
                }
            }
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // d1_order — comparison for distance-1 merge (requires cube.temp[0] mask)
    // -----------------------------------------------------------------------

    public static int D1Order(PSet a, PSet b)
    {
        PSet c1 = Temp![0];
        int i = Loop(a);
        do
        {
            uint x1 = a[i] | c1[i];
            uint x2 = b[i] | c1[i];
            if (x1 > x2) return -1;
            else if (x1 < x2) return 1;
        } while (--i > 0);
        return 0;
    }
}
