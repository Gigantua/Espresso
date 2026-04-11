namespace EspressoCS;

using static CubeContext;
using static SetOps;
using static SetFamily;

/// <summary>
/// Signature-based minimization path.
/// Translated from signature.c and signature_exact.c.
/// </summary>
public static class Signature
{
    public static SetFamily Run(SetFamily F1, SetFamily D1, SetFamily R1)
    {
        SetFamily F = SfSave(F1);
        SetFamily D = SfSave(D1);
        SetFamily R = SfSave(R1);

        R = CvrM.Unravel(R, NumBinaryVars);
        R = Contain.SfContain(R);

        for (int i = 0; i < F.Count; i++)
        {
            ResetFlag(F.GetSet(i), Prime);
        }

        F = Expand.ExpandCover(F, R, 0);
        F = Irred.Irredundant(F, D);
        SetFamily essential = Essen.Essentials(ref F, ref D);
        SetFamily esc = Canonical.FindCanonicalCover(F, D, R);
        SetFamily esSet = GeneratePrimes(esc, R);
        F = SignatureMinimizeExact(esc, esSet);
        F = SfAppend(F, essential);

        if (!Globals.SkipMakeSparse && R != null)
        {
            F = Sparse.MakeSparse(F, D1, R);
        }

        SfFree(D);
        SfFree(R);
        SfFree(esc);
        SfFree(esSet);
        return F;
    }

    public static SetFamily GeneratePrimes(SetFamily F, SetFamily R)
    {
        PSet outPartR = SetNew(Size);
        PSet odd = SetNew(Size);
        PSet even = SetNew(Size);

        int count = 0;
        SetFamily primes = SfNew(F.Count, Size);
        for (int ci = 0; ci < F.Count; ci++)
        {
            PSet c = F.GetSet(ci);
            SetFamily bb = SfNew(R.Count, Size);
            bb.Count = R.Count;

            for (int i = 0; i < R.Count; i++)
            {
                PSet r = R.GetSet(i);
                PSet b = bb.GetSet(i);
                int last = InWord;
                if (last != -1)
                {
                    uint x = r[last] & c[last];
                    x = ~(x | x >> 1) & InMask;
                    b[last] = r[last] & (x | x << 1);
                    for (int w = 1; w < last; w++)
                    {
                        x = r[w] & c[w];
                        x = ~(x | x >> 1) & Disjoint;
                        b[w] = r[w] & (x | x << 1);
                    }
                }

                PutLoop(b, Loop(r));
                InlineAnd(b, b, BinaryMask);
                InlineAnd(outPartR, MvMask, r);
                if (!SetpImplies(outPartR, c))
                {
                    InlineOr(b, b, outPartR);
                }
            }

            bb = Unate.UnateCompl(bb);
            if (bb != null)
            {
                for (int bi = 0; bi < bb.Count; bi++)
                {
                    Sigma.SetNot(bb.GetSet(bi));
                }
                primes = SfAppend(primes, bb);
            }

            count++;
            if (count % 100 == 0)
            {
                primes = Contain.SfContain(primes);
            }
        }

        primes = Contain.SfContain(primes);
        SetFree(outPartR);
        SetFree(odd);
        SetFree(even);
        return primes;
    }

    public static SetFamily SignatureMinimizeExact(SetFamily esCubes, SetFamily esSet)
    {
        for (int i = 0; i < esCubes.Count; i++)
        {
            PutSize(esCubes.GetSet(i), i);
        }
        for (int i = 0; i < esSet.Count; i++)
        {
            PutSize(esSet.GetSet(i), i);
        }

        SmMatrix table = SignatureFormTable(esCubes, esSet);
        SmRow cover = MinCov.SmMinimumCover(table, null, 0, 0);

        SetFamily result = SfNew(100, Size);
        for (SmElement? pe = cover.FirstCol; pe != null; pe = pe.NextCol)
        {
            result = SfAddSet(result, esSet.GetSet(pe.ColNum));
        }

        SparseMatrix.SmFree(table);
        SparseMatrix.SmRowFree(cover);
        return result;
    }

    public static SmMatrix SignatureFormTable(SetFamily esCubes, SetFamily esSet)
    {
        SmMatrix table = SparseMatrix.SmAlloc();
        int colDeleted = 0;

        for (int column = 0; column < esSet.Count; column++)
        {
            PSet p = esSet.GetSet(column);
            if (column % 1000 == 0)
            {
                colDeleted += Dominate.SmColDominance(table, null);
            }
            for (int row = 0; row < esCubes.Count; row++)
            {
                PSet c = esCubes.GetSet(row);
                if (SetpImplies(c, p))
                {
                    SparseMatrix.SmInsert(table, row, column);
                }
            }
        }

        colDeleted += Dominate.SmColDominance(table, null);
        return table;
    }
}
