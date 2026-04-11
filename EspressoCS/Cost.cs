namespace EspressoCS;

/// <summary>Mirrors cost_t / pcost from espresso.h.</summary>
public class Cost
{
    public int Cubes;   // number of cubes in the cover
    public int In;      // transistor count, binary-valued variables
    public int Out;     // transistor count, output part
    public int Mv;      // transistor count, multiple-valued vars
    public int Total;   // total number of transistors
    public int Primes;  // number of prime cubes
}
