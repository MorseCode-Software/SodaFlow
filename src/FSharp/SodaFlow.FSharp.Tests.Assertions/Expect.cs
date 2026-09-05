using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;

namespace SodaFlow.Tests;

/// <summary>
///     The assertions the F# tests use, each returning a plain <see cref="Task" />.
/// </summary>
/// <remarks>
///     <para>
///         This exists because F# cannot call <c>Assert.That</c>. It has twenty-six overloads, and a
///         <see cref="List{T}" /> is an acceptable argument to seven of them. C# picks the most
///         specific and compiles; F# has no such tie-break and reports the call as ambiguous, for
///         every collection assertion and for every one on a value of an unresolved type. No
///         annotation or upcast at the call site helps, because the candidates stay applicable
///         whatever the argument is narrowed to.
///     </para>
///     <para>
///         Resolving the overload here, in the language that can, is what lets the F# tests keep
///         reading as one assertion per line. Awaiting inside each method is the other half: a TUnit
///         assertion is awaited, and hands back the value it checked rather than nothing, which an
///         F# <c>do!</c> cannot bind. These hand back a <see cref="Task" />, which it can.
///     </para>
///     <para>
///         The expected value comes first, as it did in the NUnit calls these replace.
///     </para>
/// </remarks>
public static class Expect
{
    /// <summary>Asserts that <paramref name="actual" /> equals <paramref name="expected" />.</summary>
    public static async Task Equal<T>(T expected, T actual) =>
        await Assert.That(actual).IsEqualTo(expected);

    /// <summary>Asserts that <paramref name="actual" /> is the same object as <paramref name="expected" />.</summary>
    public static async Task Same<T>(T expected, T actual)
        where T : class =>
        await Assert.That(actual).IsSameReferenceAs(expected);

    /// <summary>Asserts that <paramref name="actual" /> holds exactly <paramref name="expected" />, in that order.</summary>
    public static async Task Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);

    /// <summary>Asserts that <paramref name="actual" /> holds exactly <paramref name="expected" />, in any order.</summary>
    public static async Task SameItems<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        await Assert.That(actual).IsEquivalentTo(expected);

    /// <summary>Asserts that <paramref name="actual" /> is <see langword="true" />.</summary>
    public static async Task True(bool actual) => await Assert.That(actual).IsTrue();

    /// <summary>Asserts that <paramref name="actual" /> is <see langword="false" />.</summary>
    public static async Task False(bool actual) => await Assert.That(actual).IsFalse();

    /// <summary>Asserts that <paramref name="actual" /> is not <see langword="null" />.</summary>
    public static async Task NotNull<T>(T actual)
        where T : class =>
        await Assert.That(actual).IsNotNull();

    /// <summary>Asserts that <paramref name="actual" /> is less than <paramref name="limit" />.</summary>
    public static async Task LessThan<T>(T limit, T actual)
        where T : IComparable<T> =>
        await Assert.That(actual).IsLessThan(limit);

    /// <summary>Asserts that <paramref name="action" /> throws exactly <typeparamref name="TException" />.</summary>
    public static async Task Throws<TException>(Action action)
        where TException : Exception =>
        await Assert.That(action).ThrowsExactly<TException>();
}
