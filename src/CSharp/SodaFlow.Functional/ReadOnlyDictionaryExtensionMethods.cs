using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     The lookup operations on a read-only dictionary which answer with a
///     <see cref="Maybe{T}" />.
/// </summary>
/// <remarks>
///     These are on <see cref="IReadOnlyDictionary{TKey,TValue}" /> and not also on
///     <see cref="IDictionary{TKey,TValue}" /> on purpose. Almost each dictionary implements
///     the two interfaces, and an overload for each makes each call on a concrete
///     <see cref="Dictionary{TKey,TValue}" /> ambiguous. Where only an
///     <see cref="IDictionary{TKey,TValue}" /> is in hand, use
///     <see cref="Maybe.FromTryGet{T,TResult}" /> as an alternative.
/// </remarks>
[PublicAPI]
public static class ReadOnlyDictionaryExtensionMethods
{
    /// <summary>
    ///     Gives the value stored for a key, if there is one.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
    /// <param name="dictionary">
    ///     The dictionary to look in. A <see langword="null" /> dictionary is treated as empty.
    /// </param>
    /// <param name="key">The key to look up.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the value stored for <paramref name="key" />, and
    ///     one containing no value if the dictionary has no entry for it.
    /// </returns>
    /// <remarks>
    ///     The same lookup as <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue" />, without
    ///     the output parameter. It also removes the ambiguity where a missing key and a stored
    ///     default each give the default for the value type.
    ///     A <see langword="null" /> key is passed straight through to the dictionary, so this
    ///     throws for the implementations which reject one. That is a mistake in the calling code
    ///     rather than a missing entry, and is not something to answer with no value.
    /// </remarks>
    [Pure]
    public static Maybe<TValue> TryGetValue<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue>? dictionary,
        TKey key)
    {
        if (dictionary == null)
        {
            return Maybe.None;
        }

        // The output is declared nullable and read as not nullable, and not the opposite,
        // because TryGetValue is annotated to keep it null on false only on net6.0. the
        // net472 and netstandard2.0 reference assemblies have no nullable mark.
        return dictionary.TryGetValue(key: key, value: out TValue? value) ? Maybe.Some(value) : Maybe.None;
    }
}
