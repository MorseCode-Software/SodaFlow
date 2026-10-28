using System;

namespace SodaFlow.Collections;

/// <summary>
///     The key of a per-key cell cache. See
///     <see cref="CellProjection{TSource,TProjected}" />.
/// </summary>
/// <remarks>
///     This base is not generic, thus one dictionary can hold the caches of each
///     projected type. It has no equality of its own: two projections are the same
///     key only when they are one instance.
/// </remarks>
internal abstract class CellProjection
{
    private protected CellProjection()
    {
    }
}

/// <summary>
///     How a language surface makes the value of a per-key cell from one part of
///     an item, and the key of the cache that holds those cells.
/// </summary>
/// <remarks>
///     <para>
///         A cache of cells is correct only when each cell in it was made with the
///         same two functions. Before this type, the cache was keyed by
///         <typeparamref name="TProjected" /> alone, and the functions were
///         parameters of each call. Two callers with one projected type and two
///         different functions then shared a cache. The second caller got cells
///         that give the values of the first. No caller did that, and nothing
///         stopped one. Here the functions and the key are one object.
///         A cache belongs to the projection that filled it, and a different
///         projection is a different key.
///     </para>
///     <para>
///         Thus, a surface holds one instance of this for each projected type and
///         gives that instance at each call. A new instance at each call gives a
///         new cache at each call, and then no two observers of one key share a
///         cell.
///     </para>
/// </remarks>
/// <typeparam name="TSource">The part of the item that the cell reads.</typeparam>
/// <typeparam name="TProjected">The type that the wrapper gives to the cell.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The summary of the base points at this one, thus this one is the
// documentation and not a copy.
internal sealed class CellProjection<TSource, TProjected>(
    in Func<TSource, TProjected> onPresent,
    in Func<TProjected> onAbsent) : CellProjection
{
    /// <summary>
    ///     Makes the value of the cell while the collection has the key.
    /// </summary>
    internal Func<TSource, TProjected> OnPresent { get; } = onPresent;

    /// <summary>
    ///     Makes the value of the cell while the collection does not have the key.
    /// </summary>
    internal Func<TProjected> OnAbsent { get; } = onAbsent;
}
