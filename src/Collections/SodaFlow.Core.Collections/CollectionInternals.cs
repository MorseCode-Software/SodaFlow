using System;
using System.Collections.Generic;

namespace SodaFlow.Collections;

/// <summary>
///     The one shorthand this assembly needs more than once.
/// </summary>
internal static class CollectionInternals
{
    /// <summary>
    ///     A dictionary lookup whose output is a plain <typeparamref name="TValue" /> rather than a
    ///     nullable one, so that a <c>TryGet</c> declared over an unconstrained type parameter can
    ///     forward to it without every call site restating why that is sound.
    /// </summary>
    /// <remarks>
    ///     This is the one place in the assembly that suppresses a nullable warning, and it is here
    ///     rather than at the six call sites which would otherwise each need it.
    ///     <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue" /> leaves its output at the
    ///     default when it answers false, which is the whole of what an <see langword="out" />
    ///     parameter of an unconstrained type can promise. net6.0 says so in an annotation; net472
    ///     and netstandard2.0 carry none, which is what the compiler is complaining about.
    /// </remarks>
    internal static bool TryGet<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dictionary,
        TKey key,
        out TValue value)
    {
        bool found = dictionary.TryGetValue(key, out TValue? stored);

        // ReSharper disable once NullableWarningSuppressionIsUsed - see the remarks above.
        value = stored!;

        return found;
    }

    /// <summary>This collection's internal face, which the language wrappers build on.</summary>
    /// <remarks>
    ///     Every collection this assembly builds implements it, so the cast holds for anything the
    ///     library produced; it can only fail for a type someone else wrote against the public
    ///     interface, which is what the message says.
    /// </remarks>
    internal static IReactiveCollectionInternal<TKey, TIdentity, TState> AsInternal<TKey, TIdentity, TState>(
        this IReactiveCollection<TKey, TIdentity, TState> collection)
        where TKey : notnull
        where TIdentity : notnull =>
        collection as IReactiveCollectionInternal<TKey, TIdentity, TState>
        ?? throw new NotSupportedException(
            $"{collection.GetType()} does not come from this library.");

    /// <summary>The collection that owns the store behind a view.</summary>
    /// <remarks>
    ///     Every collection this assembly builds implements the internal interface, so the cast
    ///     holds for anything the library produced. It can only fail for a type someone else wrote
    ///     against the public interface, which is why the message says so rather than letting an
    ///     <see cref="InvalidCastException" /> surface with nothing to act on.
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> RootOf<TKey, TIdentity, TState>(
        this IReactiveCollection<TKey, TIdentity, TState> collection)
        where TKey : notnull
        where TIdentity : notnull =>
        collection is IReactiveCollectionInternal<TKey, TIdentity, TState> internals
            ? internals.Root
            : throw new NotSupportedException(
                $"{collection.GetType()} does not come from this library, and the per-item cells "
                + "are answered by the collection that owns the store.");
}
