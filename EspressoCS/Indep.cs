namespace EspressoCS;

/// <summary>
/// Indep — independent cube finding for mincov subsystem.
/// This module wraps functionality that is already implemented in MinCov.cs.
/// The main algorithm (SmMaximalIndependentSet) is public in MinCov.
/// This file serves as a logical grouping for any indep.c-specific utilities.
/// Translated from indep.c
/// </summary>
public static class Indep
{
    // The core functionality (SmMaximalIndependentSet and BuildIntersectionMatrix)
    // is implemented in MinCov.cs as private methods used during the mincov algorithm.
    // If these need to be exposed publicly, use MinCov.SmMaximalIndependentSet(A, weight).
}

