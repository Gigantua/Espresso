namespace EspressoCS;

/// <summary>
/// Global cube and cdata structures, plus cube_setup / setdown_cube /
/// save_cube_struct / restore_cube_struct from cubestr.c.
/// Mirrors  struct cube_struct cube  and  struct cdata_struct cdata  in espresso.h.
/// </summary>
public static class CubeContext
{
    // -----------------------------------------------------------------------
    // cube_struct fields
    // -----------------------------------------------------------------------

    public static int    Size;
    public static int    NumVars;
    public static int    NumBinaryVars;
    public static int[]? FirstPart;
    public static int[]? LastPart;
    public static int[]? PartSize;
    public static int[]? FirstWord;
    public static int[]? LastWord;
    public static PSet   BinaryMask;
    public static PSet   MvMask;
    public static PSet[]? VarMask;
    public static PSet[]? Temp;
    public static PSet   FullSet;
    public static PSet   EmptySet;
    public static uint   InMask;
    public static int    InWord;
    public static int[]? SparseVar;   // cube.sparse
    public static int    NumMvVars;
    public static int    Output;      // -1 if none

    // -----------------------------------------------------------------------
    // cdata_struct fields
    // -----------------------------------------------------------------------

    public static int[]?  PartZeros;
    public static int[]?  VarZeros;
    public static int[]?  PartsActive;
    public static bool[]? IsUnate;
    public static int     VarsActive;
    public static int     VarsUnate;
    public static int     Best;

    // -----------------------------------------------------------------------
    // Save / restore snapshots
    // -----------------------------------------------------------------------

    private struct CubeSnapshot
    {
        public int    Size, NumVars, NumBinaryVars, NumMvVars, Output, InWord;
        public uint   InMask;
        public int[]? FirstPart, LastPart, PartSize, FirstWord, LastWord, SparseVar;
        public PSet   BinaryMask, MvMask, FullSet, EmptySet;
        public PSet[]? VarMask, Temp;
    }

    private struct CDataSnapshot
    {
        public int[]?  PartZeros, VarZeros, PartsActive;
        public bool[]? IsUnate;
        public int     VarsActive, VarsUnate, Best;
    }

    private static CubeSnapshot  _tempSave;
    private static CDataSnapshot _cdataTempSave;

    // -----------------------------------------------------------------------
    // cube_setup  (cubestr.c)
    // -----------------------------------------------------------------------

    public static void CubeSetup()
    {
        if (NumBinaryVars < 0 || NumVars < NumBinaryVars)
            throw new InvalidOperationException("cube size is silly, error in .i/.o or .mv");

        NumMvVars = NumVars - NumBinaryVars;
        Output    = NumMvVars > 0 ? NumVars - 1 : -1;

        Size      = 0;
        FirstPart = new int[NumVars];
        LastPart  = new int[NumVars];
        FirstWord = new int[NumVars];
        LastWord  = new int[NumVars];

        for (int var = 0; var < NumVars; var++)
        {
            if (var < NumBinaryVars)
                PartSize![var] = 2;
            FirstPart[var] = Size;
            FirstWord[var] = SetOps.WhichWord(Size);
            Size += Math.Abs(PartSize![var]);
            LastPart[var] = Size - 1;
            LastWord[var] = SetOps.WhichWord(Size - 1);
        }

        VarMask    = new PSet[NumVars];
        SparseVar  = new int[NumVars];
        BinaryMask = SetOps.SetNew(Size);
        MvMask     = SetOps.SetNew(Size);

        for (int var = 0; var < NumVars; var++)
        {
            PSet p = VarMask[var] = SetOps.SetNew(Size);
            for (int i = FirstPart[var]; i <= LastPart[var]; i++)
                SetOps.SetInsert(p, i);
            if (var < NumBinaryVars)
            {
                SetOps.InlineOr(BinaryMask, BinaryMask, p);
                SparseVar[var] = 0;
            }
            else
            {
                SetOps.InlineOr(MvMask, MvMask, p);
                SparseVar[var] = 1;
            }
        }

        if (NumBinaryVars == 0)
        {
            InWord = -1;
        }
        else
        {
            InWord = LastWord[NumBinaryVars - 1];
            InMask = BinaryMask[InWord] & SetOps.Disjoint;
        }

        Temp = new PSet[EspressoConstants.CubeTemp];
        for (int i = 0; i < EspressoConstants.CubeTemp; i++)
            Temp[i] = SetOps.SetNew(Size);

        FullSet  = SetOps.SetFill(SetOps.SetNew(Size), Size);
        EmptySet = SetOps.SetNew(Size);

        PartZeros  = new int[Size];
        VarZeros   = new int[NumVars];
        PartsActive = new int[NumVars];
        IsUnate    = new bool[NumVars];
    }

    // -----------------------------------------------------------------------
    // setdown_cube  (cubestr.c)
    // -----------------------------------------------------------------------

    public static void SetdownCube()
    {
        FirstPart  = null;
        LastPart   = null;
        FirstWord  = null;
        LastWord   = null;
        SparseVar  = null;
        BinaryMask = PSet.Null;
        MvMask     = PSet.Null;
        FullSet    = PSet.Null;
        EmptySet   = PSet.Null;
        VarMask    = null;
        Temp       = null;
        PartZeros  = null;
        VarZeros   = null;
        PartsActive = null;
        IsUnate    = null;
    }

    // -----------------------------------------------------------------------
    // save_cube_struct  (cubestr.c)
    // -----------------------------------------------------------------------

    public static void SaveCubeStruct()
    {
        _tempSave = new CubeSnapshot
        {
            Size           = Size,
            NumVars        = NumVars,
            NumBinaryVars  = NumBinaryVars,
            NumMvVars      = NumMvVars,
            Output         = Output,
            InWord         = InWord,
            InMask         = InMask,
            FirstPart      = FirstPart,
            LastPart       = LastPart,
            PartSize       = PartSize,
            FirstWord      = FirstWord,
            LastWord       = LastWord,
            SparseVar      = SparseVar,
            BinaryMask     = BinaryMask,
            MvMask         = MvMask,
            FullSet        = FullSet,
            EmptySet       = EmptySet,
            VarMask        = VarMask,
            Temp           = Temp,
        };

        _cdataTempSave = new CDataSnapshot
        {
            PartZeros   = PartZeros,
            VarZeros    = VarZeros,
            PartsActive = PartsActive,
            IsUnate     = IsUnate,
            VarsActive  = VarsActive,
            VarsUnate   = VarsUnate,
            Best        = Best,
        };

        // Null out the active cube fields (mirrors the C code)
        FirstPart  = null;
        LastPart   = null;
        FirstWord  = null;
        LastWord   = null;
        PartSize   = null;
        BinaryMask = PSet.Null;
        MvMask     = PSet.Null;
        FullSet    = PSet.Null;
        EmptySet   = PSet.Null;
        VarMask    = null;
        Temp       = null;
        PartZeros  = null;
        VarZeros   = null;
        PartsActive = null;
        IsUnate    = null;
    }

    // -----------------------------------------------------------------------
    // restore_cube_struct  (cubestr.c)
    // -----------------------------------------------------------------------

    public static void RestoreCubeStruct()
    {
        Size          = _tempSave.Size;
        NumVars       = _tempSave.NumVars;
        NumBinaryVars = _tempSave.NumBinaryVars;
        NumMvVars     = _tempSave.NumMvVars;
        Output        = _tempSave.Output;
        InWord        = _tempSave.InWord;
        InMask        = _tempSave.InMask;
        FirstPart     = _tempSave.FirstPart;
        LastPart      = _tempSave.LastPart;
        PartSize      = _tempSave.PartSize;
        FirstWord     = _tempSave.FirstWord;
        LastWord      = _tempSave.LastWord;
        SparseVar     = _tempSave.SparseVar;
        BinaryMask    = _tempSave.BinaryMask;
        MvMask        = _tempSave.MvMask;
        FullSet       = _tempSave.FullSet;
        EmptySet      = _tempSave.EmptySet;
        VarMask       = _tempSave.VarMask;
        Temp          = _tempSave.Temp;

        PartZeros   = _cdataTempSave.PartZeros;
        VarZeros    = _cdataTempSave.VarZeros;
        PartsActive = _cdataTempSave.PartsActive;
        IsUnate     = _cdataTempSave.IsUnate;
        VarsActive  = _cdataTempSave.VarsActive;
        VarsUnate   = _cdataTempSave.VarsUnate;
        Best        = _cdataTempSave.Best;
    }
}
