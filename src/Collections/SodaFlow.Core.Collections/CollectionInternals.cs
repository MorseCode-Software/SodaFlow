using System;
using SodaFlow.Functional;

namespace SodaFlow.Collections;

/// <summary>
///     The two shorthands this assembly needs more than once, kept together rather than repeated.
/// </summary>
internal static class CollectionInternals
{
    /// <summary>
    ///     The core's <c>FilterSomeImpl</c> applied to <see cref="SodaFlow.Functional.Maybe{T}" />,
    ///     which is what <c>SodaFlow.FilterSome</c> is in the C# wrapper. Restated here because
    ///     this assembly does not reference that wrapper — it sits underneath it.
    /// </summary>
    internal static Stream<T> FilterSome<T>(this Stream<Maybe<T>> s) =>
        s.FilterSomeImpl<T, Maybe<T>>(static (m, a) => m.MatchSome(a));

    /// <summary>
    ///     A weak reference read as an optional value rather than a <see langword="bool" /> plus an
    ///     <see langword="out" /> parameter.
    /// </summary>
    internal static Maybe<T> Target<T>(this WeakReference<T> reference)
        where T : class =>
        reference.TryGetTarget(out T? target) ? Maybe.Some(target) : Maybe<T>.None;
}
