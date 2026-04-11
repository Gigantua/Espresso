namespace EspressoCS;

// Translates sminterf.c — the set-family / sparse-matrix covering interface.

public static class SmInterf
{
    /// <summary>
    /// do_sm_minimum_cover — convert a set family A into a sparse matrix, find a
    /// minimum cover, and return the covered columns as a PSet.
    /// </summary>
    public static PSet DoSmMinimumCover(SetFamily A)
    {
        var M      = SparseMatrix.SmAlloc();
        int rownum = 0;

        // foreach_set(A, last, p) — iterate over every set p in family A.
        for (int _pi = 0; _pi < A.Count; _pi++)
        {
            var p = A.GetSet(_pi);

            // foreach_set_element(p, i, val, base) — iterate over every set bit.
            for (int i = SetOps.Loop(p); i > 0; )
            {
                uint val     = p[i];
                int  baseElem = --i << SetOps.LogBpi;
                for (; val != 0; baseElem++, val >>= 1)
                    if ((val & 1) != 0)
                        SparseMatrix.SmInsert(M, rownum, baseElem);
            }

            rownum++;
        }

        var sparseCover = MinCov.SmMinimumCover(M, null, 1, 0);
        SparseMatrix.SmFree(M);

        var cover = SetOps.SetNew(A.SfSize);
        // sm_foreach_row_element(sparseCover, pe)
        for (var pe = sparseCover.FirstCol; pe != null; pe = pe.NextCol)
            SetOps.SetInsert(cover, pe.ColNum);

        SparseMatrix.SmRowFree(sparseCover);
        return cover;
    }
}
