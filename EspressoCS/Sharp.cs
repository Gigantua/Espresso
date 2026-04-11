namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;
using static SetC;
using static Contain;

/// <summary>
/// Sharp — Boolean "A & ~B" operation on covers.
/// Translated from sharp.c.
/// </summary>
public static class Sharp
{
    // -----------------------------------------------------------------------
    // Sharp — sharp product between two cubes
    // -----------------------------------------------------------------------

    public static SetFamily SharpInternal(PSet a, PSet b)
    {
        var r = SfNew(NumVars, Size);

        if (!Cdist0(a, b))
        {
            return SfAddSet(r, a);
        }

        var d     = Temp![0];
        var temp  = Temp![1];
        var temp1 = Temp![2];

        SetDiff(d, a, b);

        for (int var = 0; var < NumVars; var++)
        {
            if (!SetpEmpty(SetAnd(temp, d, VarMask![var])))
            {
                SetDiff(temp1, a, VarMask![var]);
                var dest = r.GetSet(r.Count++);
                SetOr(dest, temp, temp1);
            }
        }

        return r;
    }

    // -----------------------------------------------------------------------
    // CvSharp — sharp product between two covers
    // -----------------------------------------------------------------------

    public static SetFamily CvSharp(SetFamily A, SetFamily B)
    {
        var T = SfNew(0, Size);
        for (int i = 0; i < A.Count; i++)
        {
            var p = A.GetSet(i);
            var temp = CbSharp(p, B);
            T = Contain.SfUnion(T, temp);
        }
        return T;
    }

    // -----------------------------------------------------------------------
    // CbSharp — sharp product between a cube and a cover
    // -----------------------------------------------------------------------

    public static SetFamily CbSharp(PSet c, SetFamily T)
    {
        if (T.Count == 0)
        {
            return SfAddSet(SfNew(1, Size), c);
        }

        return CbRecurSharp(c, T, 0, T.Count - 1, 0);
    }

    // -----------------------------------------------------------------------
    // CbRecurSharp — recursive formulation for balanced merging
    // -----------------------------------------------------------------------

    private static SetFamily CbRecurSharp(PSet c, SetFamily T, int first, int last, int level)
    {
        if (first == last)
        {
            return SharpInternal(c, T.GetSet(first));
        }

        int middle = (first + last) / 2;
        var left   = CbRecurSharp(c, T, first, middle, level + 1);
        var right  = CbRecurSharp(c, T, middle + 1, last, level + 1);
        var temp   = CvIntersect(left, right);

        SfFree(left);
        SfFree(right);

        return temp;
    }

    // -----------------------------------------------------------------------
    // CvIntersect — intersection of two covers
    // -----------------------------------------------------------------------

    private const int MAGIC = 500;  // save 500 cubes before containment

    public static SetFamily CvIntersect(SetFamily A, SetFamily B)
    {
        var T      = SfNew(MAGIC, Size);
        SetFamily? Tsave = null;

        for (int i = 0; i < A.Count; i++)
        {
            var pi = A.GetSet(i);
            for (int j = 0; j < B.Count; j++)
            {
                var pj = B.GetSet(j);
                if (Cdist0(pi, pj))
                {
                    var pt = T.GetSet(T.Count);
                    SetAnd(pt, pi, pj);
                    T.Count++;

                    if (T.Count >= T.Capacity)
                    {
                        if (Tsave == null)
                            Tsave = Contain.SfContain(T);
                        else
                            Tsave = Contain.SfUnion(Tsave, Contain.SfContain(T));

                        T = SfNew(MAGIC, Size);
                    }
                }
            }
        }

        if (Tsave == null)
            Tsave = Contain.SfContain(T);
        else
            Tsave = Contain.SfUnion(Tsave, Contain.SfContain(T));

        return Tsave;
    }

    // -----------------------------------------------------------------------
    // Dsharp — disjoint-sharp between two cubes
    // -----------------------------------------------------------------------

    public static SetFamily DsharpInternal(PSet a, PSet b)
    {
        var r = SfNew(NumVars, Size);

        if (!Cdist0(a, b))
        {
            return SfAddSet(r, a);
        }

        var diff  = new PSet(Size);
        var and   = new PSet(Size);
        var mask  = new PSet(Size);
        var temp1 = Temp![0];

        SetDiff(diff, a, b);
        SetAnd(and, a, b);
        SetClear(mask, Size);

        for (int var = 0; var < NumVars; var++)
        {
            if (!SetpDisjoint(diff, VarMask![var]))
            {
                var temp = r.GetSet(r.Count++);
                SetAnd(temp, diff, VarMask![var]);

                InlineAnd(temp1, and, mask);
                InlineOr(temp, temp, temp1);

                SetOr(mask, mask, VarMask![var]);
                InlineDiff(temp1, a, mask);
                InlineOr(temp, temp, temp1);
            }
        }

        return r;
    }

    // -----------------------------------------------------------------------
    // CvDsharp — disjoint-sharp product between two covers
    // -----------------------------------------------------------------------

    public static SetFamily CvDsharp(SetFamily A, SetFamily B)
    {
        var T = SfNew(0, Size);
        for (int i = 0; i < A.Count; i++)
        {
            var p = A.GetSet(i);
            var temp = CbDsharp(p, B);
            T = Contain.SfUnion(T, temp);
        }
        return T;
    }

    // -----------------------------------------------------------------------
    // CbDsharp — disjoint-sharp between a cube and a cover
    // -----------------------------------------------------------------------

    public static SetFamily CbDsharp(PSet c, SetFamily T)
    {
        if (T.Count == 0)
        {
            return SfAddSet(SfNew(1, Size), c);
        }

        var Y = SfNew(T.Count, Size);
        SetCopy(Y.GetSet(Y.Count++), c);

        for (int i = 0; i < T.Count; i++)
        {
            var p = T.GetSet(i);
            var Y1 = Cb1Dsharp(Y, p);
            SfFree(Y);
            Y = Y1;
        }

        return Y;
    }

    // -----------------------------------------------------------------------
    // Cb1Dsharp — disjoint-sharp between a cover and a cube
    // -----------------------------------------------------------------------

    public static SetFamily Cb1Dsharp(SetFamily T, PSet c)
    {
        var R = SfNew(T.Count, Size);
        for (int i = 0; i < T.Count; i++)
        {
            var p = T.GetSet(i);
            var temp = DsharpInternal(p, c);
            R = Contain.SfUnion(R, temp);
        }
        return R;
    }

    // -----------------------------------------------------------------------
    // MakeDisjoint — make a cover disjoint-sharp
    // -----------------------------------------------------------------------

    public static SetFamily MakeDisjoint(SetFamily A)
    {
        var R = SfNew(0, Size);
        for (int i = 0; i < A.Count; i++)
        {
            var p = A.GetSet(i);
            var new_cov = CbDsharp(p, R);
            R = SfAppend(R, new_cov);
        }
        return R;
    }
}

