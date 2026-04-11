namespace EspressoCS;

using static SetOps;
using static CubeContext;

/// <summary>
/// Cofactor — cubelist construction and cofactoring operations.
/// Translated 1:1 from cofactor.c.
/// </summary>
public static class Cofactor
{
    // -----------------------------------------------------------------------
    // CubeListSize — count cubes in a null-terminated cube list
    // T[0] = cofactor, T[1] = placeholder, T[2..N+1] = cubes, T[N+2] = null
    // -----------------------------------------------------------------------

    public static int CubeListSize(PSet[] T)
    {
        int i = 2;
        while (!T[i].IsNull) i++;
        return i - 2;
    }

    // -----------------------------------------------------------------------
    // cofactor — compute the cofactor of a cube list T with respect to cube c
    // -----------------------------------------------------------------------

    public static PSet[] GetCofactor(PSet[] T, PSet c)
    {
        int listLen = CubeListSize(T) + 5;
        var TcSave  = new PSet[listLen];
        int tcIdx   = 0;

        // T[0]: pass on which variables have been cofactored against
        PSet temp = Temp![0];
        SetDiff(temp, FullSet, c);
        PSet newCube = SetNew(Size);
        SetOr(newCube, T[0], temp);
        TcSave[tcIdx++] = newCube;
        tcIdx++;   // T[1] placeholder

        for (int t1 = 2; !T[t1].IsNull; t1++)
        {
            PSet p = T[t1];
            if (p != c)
            {
                if (!SetC.Cdist0(p, c)) continue;   // goto false
                TcSave[tcIdx++] = p;
                // false:
            }
        }

        TcSave[tcIdx++] = PSet.Null;   // sentinel
        // TcSave[1] end-pointer not needed (we scan for size)
        return TcSave;
    }

    // -----------------------------------------------------------------------
    // scofactor — cofactor optimised for a single-variable cube c
    // -----------------------------------------------------------------------

    public static PSet[] Scofactor(PSet[] T, PSet c, int var)
    {
        int listLen = CubeListSize(T) + 5;
        var TcSave  = new PSet[listLen];
        int tcIdx   = 0;

        // T[0]: pass on which variables have been cofactored against
        PSet mask   = Temp![1];
        SetDiff(mask, FullSet, c);
        PSet newCube = SetNew(Size);
        SetOr(newCube, T[0], mask);
        TcSave[tcIdx++] = newCube;
        tcIdx++;   // T[1] placeholder

        int first = FirstWord![var];
        int last  = LastWord![var];

        // mask = var_mask[var] & c (quick distance check)
        SetAnd(mask, VarMask![var], c);

        for (int t1 = 2; !T[t1].IsNull; t1++)
        {
            PSet p = T[t1];
            if (p != c)
            {
                for (int i = first; i <= last; i++)
                {
                    if ((p[i] & mask[i]) != 0)
                    {
                        TcSave[tcIdx++] = p;
                        break;
                    }
                }
            }
        }

        TcSave[tcIdx++] = PSet.Null;   // sentinel
        return TcSave;
    }

    // -----------------------------------------------------------------------
    // massive_count — populate cdata.part_zeros / var_zeros / parts_active / best
    // -----------------------------------------------------------------------

    public static void MassiveCount(PSet[] T)
    {
        int[] count = PartZeros!;

        // Clear column zero-counts
        for (int i = Size - 1; i >= 0; i--)
            count[i] = 0;

        // Count zeros in each column for each cube in the list
        {
            PSet cof  = T[0];
            PSet full = FullSet;

            for (int t1 = 2; !T[t1].IsNull; t1++)
            {
                PSet p = T[t1];
                for (int i = Loop(p); i > 0; i--)
                {
                    uint val = full[i] & ~(p[i] | cof[i]);
                    if (val != 0)
                    {
                        int cb = (i - 1) << LogBpi;   // base index for this word

                        if ((val & 0xFF000000u) != 0)
                        {
                            if ((val & 0x80000000u) != 0) count[cb + 31]++;
                            if ((val & 0x40000000u) != 0) count[cb + 30]++;
                            if ((val & 0x20000000u) != 0) count[cb + 29]++;
                            if ((val & 0x10000000u) != 0) count[cb + 28]++;
                            if ((val & 0x08000000u) != 0) count[cb + 27]++;
                            if ((val & 0x04000000u) != 0) count[cb + 26]++;
                            if ((val & 0x02000000u) != 0) count[cb + 25]++;
                            if ((val & 0x01000000u) != 0) count[cb + 24]++;
                        }
                        if ((val & 0x00FF0000u) != 0)
                        {
                            if ((val & 0x00800000u) != 0) count[cb + 23]++;
                            if ((val & 0x00400000u) != 0) count[cb + 22]++;
                            if ((val & 0x00200000u) != 0) count[cb + 21]++;
                            if ((val & 0x00100000u) != 0) count[cb + 20]++;
                            if ((val & 0x00080000u) != 0) count[cb + 19]++;
                            if ((val & 0x00040000u) != 0) count[cb + 18]++;
                            if ((val & 0x00020000u) != 0) count[cb + 17]++;
                            if ((val & 0x00010000u) != 0) count[cb + 16]++;
                        }
                        if ((val & 0x0000FF00u) != 0)
                        {
                            if ((val & 0x00008000u) != 0) count[cb + 15]++;
                            if ((val & 0x00004000u) != 0) count[cb + 14]++;
                            if ((val & 0x00002000u) != 0) count[cb + 13]++;
                            if ((val & 0x00001000u) != 0) count[cb + 12]++;
                            if ((val & 0x00000800u) != 0) count[cb + 11]++;
                            if ((val & 0x00000400u) != 0) count[cb + 10]++;
                            if ((val & 0x00000200u) != 0) count[cb +  9]++;
                            if ((val & 0x00000100u) != 0) count[cb +  8]++;
                        }
                        if ((val & 0x000000FFu) != 0)
                        {
                            if ((val & 0x00000080u) != 0) count[cb +  7]++;
                            if ((val & 0x00000040u) != 0) count[cb +  6]++;
                            if ((val & 0x00000020u) != 0) count[cb +  5]++;
                            if ((val & 0x00000010u) != 0) count[cb +  4]++;
                            if ((val & 0x00000008u) != 0) count[cb +  3]++;
                            if ((val & 0x00000004u) != 0) count[cb +  2]++;
                            if ((val & 0x00000002u) != 0) count[cb +  1]++;
                            if ((val & 0x00000001u) != 0) count[cb +  0]++;
                        }
                    }
                }
            }
        }

        // Aggregate counts into cdata fields and select the best splitting variable
        {
            int best         = -1;
            int mostActive   = 0;
            int mostZero     = 0;
            int mostBalanced = 32000;

            VarsUnate = VarsActive = 0;

            for (int var = 0; var < NumVars; var++)
            {
                int active, maxActive;

                if (var < NumBinaryVars)
                {
                    // Special hack for binary variables: two parts per variable
                    int ii      = count[var * 2];
                    int lastbit = count[var * 2 + 1];
                    active    = (ii > 0 ? 1 : 0) + (lastbit > 0 ? 1 : 0);
                    VarZeros![var] = ii + lastbit;
                    maxActive = Math.Max(ii, lastbit);
                }
                else
                {
                    maxActive = active = VarZeros![var] = 0;
                    int lastbit = LastPart![var];
                    for (int i = FirstPart![var]; i <= lastbit; i++)
                    {
                        VarZeros![var] += count[i];
                        active += (count[i] > 0 ? 1 : 0);
                        if (active > maxActive) maxActive = active;
                    }
                }

                // Select best variable: first by most active parts,
                // then by most zeros, then by most balanced split
                if (active > mostActive)
                {
                    best         = var;
                    mostActive   = active;
                    mostZero     = VarZeros![best];
                    mostBalanced = maxActive;
                }
                else if (active == mostActive)
                {
                    if (VarZeros![var] > mostZero)
                    {
                        best         = var;
                        mostZero     = VarZeros![best];
                        mostBalanced = maxActive;
                    }
                    else if (VarZeros![var] == mostZero && maxActive < mostBalanced)
                    {
                        best         = var;
                        mostBalanced = maxActive;
                    }
                }

                PartsActive![var] = active;
                IsUnate![var]     = (active == 1);
                VarsActive       += (active > 0 ? 1 : 0);
                VarsUnate        += (active == 1 ? 1 : 0);
            }

            Best = best;
        }
    }

    // -----------------------------------------------------------------------
    // binate_split_select — choose and build the two cofactor cubes for splitting
    // -----------------------------------------------------------------------

    public static int BinateSplitSelect(PSet[] T, PSet cleft, PSet cright, int debugFlag)
    {
        int best    = Best;
        int lastbit = LastPart![best];
        PSet cof    = T[0];

        SetDiff(cleft,  FullSet, VarMask![best]);
        SetDiff(cright, FullSet, VarMask![best]);

        // Count active parts in this variable
        int halfbit = 0;
        for (int i = FirstPart![best]; i <= lastbit; i++)
            if (!IsInSet(cof, i)) halfbit++;

        // Assign the first half of parts to cleft
        halfbit /= 2;
        int j = FirstPart![best];
        for (; halfbit > 0; j++)
            if (!IsInSet(cof, j)) { halfbit--; SetInsert(cleft, j); }

        // Assign the remaining parts to cright
        for (; j <= lastbit; j++)
            if (!IsInSet(cof, j)) SetInsert(cright, j);

        if ((Globals.Debug & (uint)debugFlag) != 0)
        {
            Console.Write($"BINATE_SPLIT_SELECT: split against {best}\n");
            // verbose cube print (pc1/pc2) omitted — not yet ported
        }

        return best;
    }

    // -----------------------------------------------------------------------
    // cube1list — build a cube-list from one set family
    // -----------------------------------------------------------------------

    public static PSet[] Cube1List(SetFamily A)
    {
        var list  = new PSet[A.Count + 3];
        int plist = 0;

        list[plist++] = SetNew(Size);   // T[0] = new cube (cofactor placeholder)
        plist++;                        // T[1] placeholder

        for (int si = 0; si < A.Count; si++)
            list[plist++] = A.GetSet(si);

        list[plist] = PSet.Null;        // sentinel
        return list;
    }

    // -----------------------------------------------------------------------
    // cube2list — build a cube-list from two set families
    // -----------------------------------------------------------------------

    public static PSet[] Cube2List(SetFamily A, SetFamily B)
    {
        var list  = new PSet[A.Count + B.Count + 3];
        int plist = 0;

        list[plist++] = SetNew(Size);
        plist++;

        for (int si = 0; si < A.Count; si++)
            list[plist++] = A.GetSet(si);
        for (int si = 0; si < B.Count; si++)
            list[plist++] = B.GetSet(si);

        list[plist] = PSet.Null;
        return list;
    }

    // -----------------------------------------------------------------------
    // cube3list — build a cube-list from three set families
    // -----------------------------------------------------------------------

    public static PSet[] Cube3List(SetFamily A, SetFamily B, SetFamily C)
    {
        var list  = new PSet[A.Count + B.Count + C.Count + 3];
        int plist = 0;

        list[plist++] = SetNew(Size);
        plist++;

        for (int si = 0; si < A.Count; si++)
            list[plist++] = A.GetSet(si);
        for (int si = 0; si < B.Count; si++)
            list[plist++] = B.GetSet(si);
        for (int si = 0; si < C.Count; si++)
            list[plist++] = C.GetSet(si);

        list[plist] = PSet.Null;
        return list;
    }

    // -----------------------------------------------------------------------
    // cubeunlist — OR each cube with the cofactor and collect into a new cover
    // -----------------------------------------------------------------------

    public static SetFamily CubeUnlist(PSet[] A1)
    {
        PSet cof  = A1[0];
        int  size = CubeListSize(A1);
        SetFamily A = SetFamily.SfNew(size, Size);

        for (int i = 2; !A1[i].IsNull; i++)
        {
            PSet pdest = A.GetSet(i - 2);
            InlineOr(pdest, A1[i], cof);
        }

        A.Count = size;
        return A;
    }

    // -----------------------------------------------------------------------
    // simplify_cubelist — sort and deduplicate a cube list for distance-1 merge
    // -----------------------------------------------------------------------

    public static void SimplifyCubelist(PSet[] T)
    {
        SetCopy(Temp![0], T[0]);   // retrieve cofactor into temp[0] for D1Order

        int ncubes = CubeListSize(T);

        // Sort T[2..ncubes+1] by d1_order
        Array.Sort(T, 2, ncubes, Comparer<PSet>.Create(SetC.D1Order));

        int tdestIdx = 2;
        // Note: T[2] intentionally skipped (matches commented-out C line)
        for (int i = 3; i < ncubes; i++)
        {
            if (SetC.D1Order(T[i - 1], T[i]) != 0)
                T[tdestIdx++] = T[i];
        }

        T[tdestIdx] = PSet.Null;   // sentinel
    }
}
