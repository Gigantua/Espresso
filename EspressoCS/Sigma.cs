namespace EspressoCS;

// Translates sigma.c.
// Computes the "signature cube" for a given ONSET cube with respect to an OFFSET cover.

public static class Sigma
{
    /// <summary>
    /// get_sigma — compute the signature cube for onset cube <paramref name="c"/>
    /// given offset cover <paramref name="R"/>.
    /// </summary>
    public static PSet GetSigma(SetFamily R, PSet c)
    {
        var outPartR = SetOps.SetNew(CubeContext.Size);
        var s        = SetOps.SetNew(CubeContext.Size);

        var BB = SetFamily.SfNew(R.Count, CubeContext.Size);
        BB.Count = R.Count;

        // Build blocking matrix BB from R and c.
        for (int i = 0; i < R.Count; i++)
        {
            var r    = R.GetSet(i);
            var b    = BB.GetSet(i);
            int last = CubeContext.InWord;

            if (last != -1)
            {
                // Check the partial word of binary variables.
                uint x = r[last] & c[last];
                x = ~(x | x >> 1) & CubeContext.InMask;
                b[last] = r[last] & (x | x << 1);

                // Check the full words of binary variables.
                for (int w = 1; w < last; w++)
                {
                    x    = r[w] & c[w];
                    x    = ~(x | x >> 1) & SetOps.Disjoint;
                    b[w] = r[w] & (x | x << 1);
                }
            }

            SetOps.PutLoop(b, SetOps.Loop(r));
            SetOps.InlineAnd(b, b, CubeContext.BinaryMask);
            SetOps.InlineAnd(outPartR, CubeContext.MvMask, r);
            if (!SetOps.SetpImplies(outPartR, c))
                SetOps.InlineOr(b, b, outPartR);
        }

        SetOps.SetFree(outPartR);

        BB = Unate.UnateCompl(BB);

        SetOps.InlineCopy(s, CubeContext.EmptySet);
        for (int i = 0; i < BB.Count; i++)
        {
            var b = BB.GetSet(i);
            SetOps.InlineOr(s, s, b);
        }

        SetFamily.SfFree(BB);
        SetNot(s);
        return s;
    }

    /// <summary>set_not — flip 0s to 1s and 1s to 0s (complement within the full set).</summary>
    public static void SetNot(PSet c)
    {
        SetOps.InlineDiff(c, CubeContext.FullSet, c);
    }
}
