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
}
