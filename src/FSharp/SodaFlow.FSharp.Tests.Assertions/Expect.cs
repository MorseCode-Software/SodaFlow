using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Core;
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
// Every assertion below is awaited, in Run, one call away from where it is built. TUnit's
// analyzer only recognizes an await in the same expression, so it reads each of these as an
// assertion that is never checked. Awaiting in place instead would mean writing each method
// twice, once with the message and once without.
#pragma warning disable TUnitAssertions0002

public static class Expect
{
    /// <summary>Asserts that <paramref name="actual" /> equals <paramref name="expected" />.</summary>
    public static Task Equal<T>(T expected, T actual, string? because = null) =>
        Run(Assert.That(actual).IsEqualTo(expected), because);

    /// <summary>Asserts that <paramref name="actual" /> is the same object as <paramref name="expected" />.</summary>
    /// <remarks>
    ///     Untyped, as NUnit's AreSame was. Reference identity does not need the two sides to share a
    ///     static type, and some of these call sites compare an exception to one held as its base.
    /// </remarks>
    public static Task Same(object? expected, object? actual, string? because = null) =>
        Run(Assert.That(actual).IsSameReferenceAs(expected), because);

    /// <summary>Asserts that <paramref name="actual" /> holds exactly <paramref name="expected" />, in that order.</summary>
    public static Task Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? because = null) =>
        Run(Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching), because);

    /// <summary>Asserts that <paramref name="actual" /> holds exactly <paramref name="expected" />, in any order.</summary>
    public static Task SameItems<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? because = null) =>
        Run(Assert.That(actual).IsEquivalentTo(expected), because);

    /// <summary>Asserts that <paramref name="actual" /> is <see langword="true" />.</summary>
    public static Task True(bool actual, string? because = null) =>
        Run(Assert.That(actual).IsTrue(), because);

    /// <summary>Asserts that <paramref name="actual" /> is <see langword="false" />.</summary>
    public static Task False(bool actual, string? because = null) =>
        Run(Assert.That(actual).IsFalse(), because);

    /// <summary>Asserts that <paramref name="actual" /> is not <see langword="null" />.</summary>
    public static Task NotNull<T>(T actual, string? because = null)
        where T : class =>
        Run(Assert.That(actual).IsNotNull(), because);

    /// <summary>Asserts that <paramref name="actual" /> is less than <paramref name="limit" />.</summary>
    public static Task LessThan<T>(T limit, T actual, string? because = null)
        where T : IComparable<T> =>
        Run(Assert.That(actual).IsLessThan(limit), because);

    /// <summary>Asserts that <paramref name="action" /> throws exactly <typeparamref name="TException" />.</summary>
    public static Task Throws<TException>(Action action, string? because = null)
        where TException : Exception =>
        Run(Assert.That(action).ThrowsExactly<TException>(), because);

    /// <summary>
    ///     Awaits an assertion, attaching <paramref name="because" /> when the call supplied the
    ///     message NUnit's assertions took as their last argument.
    /// </summary>
    private static async Task Run<T>(Assertion<T> assertion, string? because)
    {
        if (because is not null)
        {
            await assertion.Because(because);
            return;
        }

        await assertion;
    }
}

#pragma warning restore TUnitAssertions0002
