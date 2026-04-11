namespace EspressoCS;

using static SetOps;
using static CubeContext;
using static SetFamily;

/// <summary>
/// Black/white list management for the signature-based minimizer.
/// Translated 1:1 from black_white.c.
/// </summary>
public static class BlackWhite
{
    // -----------------------------------------------------------------------
    // Doubly-linked white/black lists (indices into BB rows)
    // -----------------------------------------------------------------------

    private static int _whiteHead, _whiteTail;
    private static int _blackHead, _blackTail;
    private static int _forwardLink, _backwardLink;
    private static int[] _forward = Array.Empty<int>();
    private static int[] _backward = Array.Empty<int>();

    // -----------------------------------------------------------------------
    // Stack for saving/restoring the black list
    // -----------------------------------------------------------------------

    private static int[] _stackHead = Array.Empty<int>();
    private static int[] _stackTail = Array.Empty<int>();
    private static int _stackP;

    // -----------------------------------------------------------------------
    // Blocking matrix BB (complement sense from sigma.c)
    // -----------------------------------------------------------------------

    private static SetFamily _bb = null!;

    // -----------------------------------------------------------------------
    // alloc_list / free_list / init_list
    // -----------------------------------------------------------------------

    private static void AllocList(int size)
    {
        _forward  = new int[size];
        _backward = new int[size];
    }

    private static void FreeList() { /* GC handles it */ }

    private static void InitList(int size)
    {
        for (int i = 0; i < size; i++)
        {
            _forward[i]  = i + 1;
            _backward[i] = i - 1;
        }
        _forward[size - 1] = -1;
        _backward[0]       = -1;
        _whiteHead = 0;
        _whiteTail = size - 1;
        _blackHead = -1;
        _blackTail = -1;
    }

    // -----------------------------------------------------------------------
    // delete / insert — move element between white list and black list
    // -----------------------------------------------------------------------

    private static void Delete(int element)
    {
        _forwardLink  = _forward[element];
        _backwardLink = _backward[element];

        if (_forwardLink != -1)
        {
            if (_backwardLink != -1)
            {
                _forward[_backwardLink]  = _forwardLink;
                _backward[_forwardLink] = _backwardLink;
            }
            else
            {
                _whiteHead = _forwardLink;
                _backward[_forwardLink] = -1;
            }
        }
        else
        {
            if (_backwardLink != -1)
            {
                _whiteTail = _backwardLink;
                _forward[_backwardLink] = -1;
            }
            else
            {
                _whiteHead = _whiteTail = -1;
            }
        }
    }

    private static void Insert(int element)
    {
        if (_blackHead != -1)
        {
            _forward[element]  = _blackHead;
            _backward[element] = -1;
            _backward[_blackHead] = element;
            _blackHead = element;
        }
        else
        {
            _blackHead = _blackTail = element;
            _forward[element]  = -1;
            _backward[element] = -1;
        }
    }

    // -----------------------------------------------------------------------
    // merge_list — append black list to white list
    // -----------------------------------------------------------------------

    public static void MergeList()
    {
        if (_whiteHead != -1)
        {
            if (_blackHead != -1)
            {
                _forward[_whiteTail]  = _blackHead;
                _backward[_blackHead] = _whiteTail;
                _whiteTail = _blackTail;
                _blackHead = _blackTail = -1;
            }
        }
        else
        {
            _whiteHead = _blackHead;
            _whiteTail = _blackTail;
            _blackHead = _blackTail = -1;
        }
    }

    // -----------------------------------------------------------------------
    // Stack operations
    // -----------------------------------------------------------------------

    private static void AllocStack(int size)
    {
        _stackHead = new int[size];
        _stackTail = new int[size];
    }

    private static void FreeStack() { /* GC handles it */ }

    public static void PushBlackList()
    {
        _stackHead[_stackP]   = _blackHead;
        _stackTail[_stackP++] = _blackTail;
    }

    public static void PopBlackList()
    {
        _blackHead = _stackHead[--_stackP];
        _blackTail = _stackTail[_stackP];
    }

    public static void ResetBlackList()
    {
        _blackHead = _blackTail = -1;
    }

    private static void Clear() => _stackP = 0;

    // -----------------------------------------------------------------------
    // setup_bw — initialise black/white lists and blocking matrix
    // -----------------------------------------------------------------------

    public static void SetupBw(SetFamily R, PSet c)
    {
        int size = R.Count;

        AllocList(size);
        AllocStack(NumBinaryVars);
        _bb       = SfNew(size, Size);
        _bb.Count = size;

        var outPartR = SetNew(Size);

        InitList(size);
        Clear();

        for (int i = 0; i < R.Count; i++)
        {
            var r    = R.GetSet(i);
            var b    = _bb.GetSet(i);
            int last = InWord;

            if (last != -1)
            {
                uint x = r[last] & c[last];
                x = ~(x | x >> 1) & InMask;
                b[last] = r[last] & (x | x << 1);

                for (int w = 1; w < last; w++)
                {
                    x    = r[w] & c[w];
                    x    = ~(x | x >> 1) & Disjoint;
                    b[w] = r[w] & (x | x << 1);
                }
            }

            PutLoop(b, Loop(r));
            InlineAnd(b, b, BinaryMask);
            InlineAnd(outPartR, MvMask, r);

            if (!SetpImplies(outPartR, c))
                InlineOr(b, b, outPartR);

            Sigma.SetNot(b);
        }

        SetFree(outPartR);
    }

    // -----------------------------------------------------------------------
    // free_bw — release resources
    // -----------------------------------------------------------------------

    public static void FreeBw()
    {
        FreeList();
        FreeStack();
        SfFree(_bb);
    }

    // -----------------------------------------------------------------------
    // black_white — test containment: every black row covered by some white row
    // -----------------------------------------------------------------------

    public static int BlackWhiteCheck()
    {
        for (int bIndex = _blackHead; bIndex != -1; bIndex = _forward[bIndex])
        {
            bool containment = false;
            for (int wIndex = _whiteHead; wIndex != -1; wIndex = _forward[wIndex])
            {
                if (SetpImplies(_bb.GetSet(bIndex), _bb.GetSet(wIndex)))
                {
                    containment = true;
                    break;
                }
            }
            if (!containment)
                return 0; // FALSE
        }
        return 1; // TRUE
    }

    // -----------------------------------------------------------------------
    // split_list — move white elements not containing bit v to the black list
    // -----------------------------------------------------------------------

    public static void SplitList(SetFamily R, int v)
    {
        int index = _whiteHead;
        while (index != -1)
        {
            int nextIndex = _forward[index];
            if (!IsInSet(R.GetSet(index), v))
            {
                Delete(index);
                Insert(index);
            }
            index = nextIndex;
        }
    }

    // -----------------------------------------------------------------------
    // print_bw — debug helper
    // -----------------------------------------------------------------------

    public static void PrintBw(int size)
    {
        Console.WriteLine($"white_head {_whiteHead}\twhite_tail {_whiteTail}\tblack_head {_blackHead}\tblack_tail {_blackTail}");
        PrintLinks(size, _forward);
        PrintLinks(size, _backward);
    }

    private static void PrintLinks(int size, int[] list)
    {
        for (int i = 0; i < size; i++)
            Console.Write($"{list[i]}{((i + 1) % 10 != 0 ? '\t' : '\n')}");
        Console.WriteLine();
    }

    // -----------------------------------------------------------------------
    // Variable-list data structures (ess_test_and_reduction ordering)
    // -----------------------------------------------------------------------

    private static int   _variableCount;
    private static int[] _variableForwardChain  = Array.Empty<int>();
    private static int[] _variableBackwardChain = Array.Empty<int>();
    private static int   _variableHead;
    private static int   _variableTail;

    public static void VariableListAlloc(int size)
    {
        _variableForwardChain  = new int[size];
        _variableBackwardChain = new int[size];
    }

    public static void VariableListInit(int reducedCFreeCount, int[] reducedCFreeList)
    {
        _variableCount = reducedCFreeCount;

        if (_variableCount == 0)
        {
            _variableHead = _variableTail = -1;
            return;
        }

        _variableHead = reducedCFreeList[0];
        _variableTail = reducedCFreeList[_variableCount - 1];
        _variableForwardChain[_variableTail]  = -1;
        _variableBackwardChain[_variableHead] = -1;

        int nextV = _variableHead;
        for (int i = 1; i < _variableCount; i++)
        {
            int v = nextV;
            nextV = reducedCFreeList[i];
            _variableForwardChain[v]     = nextV;
            _variableBackwardChain[nextV] = v;
        }
    }

    public static void VariableListDelete(int element)
    {
        _variableCount--;
        _forwardLink  = _variableForwardChain[element];
        _backwardLink = _variableBackwardChain[element];

        if (_forwardLink != -1)
        {
            if (_backwardLink != -1)
            {
                _variableForwardChain[_backwardLink]  = _forwardLink;
                _variableBackwardChain[_forwardLink] = _backwardLink;
            }
            else
            {
                _variableHead = _forwardLink;
                _variableBackwardChain[_forwardLink] = -1;
            }
        }
        else
        {
            if (_backwardLink != -1)
            {
                _variableTail = _backwardLink;
                _variableForwardChain[_backwardLink] = -1;
            }
            else
            {
                _variableHead = _variableTail = -1;
            }
        }
    }

    public static void VariableListInsert(int element)
    {
        _variableCount++;
        if (_variableHead != -1)
        {
            _variableForwardChain[element]  = _variableHead;
            _variableBackwardChain[element] = -1;
            _variableBackwardChain[_variableHead] = element;
            _variableHead = element;
        }
        else
        {
            _variableHead = _variableTail = element;
            _variableForwardChain[element]  = -1;
            _variableBackwardChain[element] = -1;
        }
    }

    public static bool VariableListEmpty() => _variableCount == 0;

    public static void GetNextVariable(out int pv, out int pphase, SetFamily R)
    {
        int maxBlackCount = -1;
        int maxVariable   = 0;
        int maxPhase      = 0;

        for (int v = _variableHead; v != -1; v = _variableForwardChain[v])
        {
            int e0 = v << 1;
            int e1 = e0 + 1;
            int e0BlackCount = 0;
            int e1BlackCount = 0;

            for (int wIndex = _whiteHead; wIndex != -1; wIndex = _forward[wIndex])
            {
                var r = R.GetSet(wIndex);
                if (IsInSet(r, e0))
                {
                    if (!IsInSet(r, e1))
                        e1BlackCount++;
                }
                else
                {
                    e0BlackCount++;
                }
            }

            if (e0BlackCount > e1BlackCount)
            {
                if (e0BlackCount > maxBlackCount)
                {
                    maxBlackCount = e0BlackCount;
                    maxVariable   = v;
                    maxPhase      = 0;
                }
            }
            else
            {
                if (e1BlackCount > maxBlackCount)
                {
                    maxBlackCount = e1BlackCount;
                    maxVariable   = v;
                    maxPhase      = 1;
                }
            }
        }

        pv     = maxVariable;
        pphase = maxPhase;
    }

    public static void PrintVariableList()
    {
        Console.WriteLine("Variable_Forward_Chain:");
        PrintLinks(NumBinaryVars, _variableForwardChain);
        Console.WriteLine("Variable_Backward_Chain:");
        PrintLinks(NumBinaryVars, _variableBackwardChain);
    }
}
