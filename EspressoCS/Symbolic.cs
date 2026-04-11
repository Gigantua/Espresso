namespace EspressoCS;

/// <summary>Mirrors symbolic_list_t from espresso.h.</summary>
public class SymbolicList
{
    public int Variable;
    public int Pos;
    public SymbolicList? Next;
}

/// <summary>Mirrors symbolic_label_t from espresso.h.</summary>
public class SymbolicLabel
{
    public string? Label;
    public SymbolicLabel? Next;
}

/// <summary>Mirrors symbolic_t from espresso.h.</summary>
public class Symbolic
{
    public SymbolicList? SymbolicListHead;   // symbolic_list
    public int SymbolicListLength;
    public SymbolicLabel? SymbolicLabelHead; // symbolic_label
    public int SymbolicLabelLength;
    public Symbolic? Next;
}
