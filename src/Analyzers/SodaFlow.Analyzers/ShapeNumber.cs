using System.Linq;

namespace SodaFlow.Analyzers;

/// <summary>
///     The numbers from which the walk knows that a read of a loop left no new
///     state. <see cref="LoopState.Shape" /> is such a number for a state.
/// </summary>
/// <remarks>
///     The walk adds one number for each location and its state. It stops the
///     reads of a loop when a sum is a sum from before. Two different sets of
///     states must not have one sum. Thus, the number for a location and its
///     state has no simple relation to those two parts. With a simple relation,
///     the changes to two locations can add to zero. The walk then does not
///     make a read that it must make.
/// </remarks>
internal static class ShapeNumber
{
    /// <summary>
    ///     A number for a name. Each run of the analyzer gives a name the same
    ///     number, thus the analyzer gives the same code the same diagnostics.
    /// </summary>
    /// <param name="name">The name.</param>
    public static int Of(string name) =>
        unchecked((int)name.Aggregate(
            seed: 2166136261,
            func: static (number, letter) => unchecked((number ^ letter) * 16777619)));

    /// <summary>A number for a location and the state in it.</summary>
    /// <param name="location">The number of the location.</param>
    /// <param name="state">The number of the state.</param>
    public static int Of(int location, int state)
    {
        unchecked
        {
            uint number = ((uint)location * 0x9E3779B1) ^ (uint)state;

            number ^= number >> 16;
            number *= 0x85EBCA6B;
            number ^= number >> 13;
            number *= 0xC2B2AE35;
            number ^= number >> 16;

            return (int)number;
        }
    }
}
