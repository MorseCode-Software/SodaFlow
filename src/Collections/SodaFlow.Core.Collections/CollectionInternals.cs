using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SodaFlow.Collections;

/// <summary>
///     The one helper that this assembly uses more than one time.
/// </summary>
internal static class CollectionInternals
{
    /// <summary>
    ///     A dictionary lookup whose output is a <typeparamref name="TValue" /> and not a nullable
    ///     <typeparamref name="TValue" />. Thus a <c>TryGet</c> on a type parameter with no
    ///     constraint can call it, and each call does not give the cause again.
    /// </summary>
    /// <remarks>
    ///     This is the one position in the assembly with a suppression of a nullable warning, and
    ///     it is here and not at the six calls that each one requires.
    ///     <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue" /> gives the default value as
    ///     its output when it answers false, and an <see langword="out" /> parameter of a type with
    ///     no constraint can give no more. net6.0 declares that, and net472 and netstandard2.0
    ///     declare nothing, which causes the warning.
    /// </remarks>
    internal static bool TryGet<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dictionary,
        TKey key,
        [NotNullWhen(true)] out TValue? value)
    {
        bool found = dictionary.TryGetValue(key: key, value: out value);

        return found;
    }
}
