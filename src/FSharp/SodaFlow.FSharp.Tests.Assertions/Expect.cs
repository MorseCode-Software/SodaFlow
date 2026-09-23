using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using TUnit.Assertions.Core;
using TUnit.Assertions.Enums;

namespace SodaFlow.Tests;

/// <summary>
///     The assertions the F# tests use, each returning a plain <see cref="Task" />.
/// </summary>
/// <remarks>
///     <para>
///         This exists because F# cannot call <c>Assert.That</c>. It has twenty-six overloads, and a
///         <see cref="List{T}" /> is a permitted argument to seven of them. C# selects the closest
///         overload and compiles. F# has no such rule and reports the call as ambiguous. This occurs for
///         each collection assertion, and for each one on a value of an unresolved type. No type
///         declaration and no upcast at the call site helps, because the candidates stay applicable for
///         each type of the argument.
///     </para>
///     <para>
///         This code resolves the overload in the language that can, thus the F# tests keep one
///         assertion on each line. The await in each method is the second half. An await of a TUnit
///         assertion gives the value that it checked, and not nothing, and an F# <c>do!</c> cannot bind
///         that value. These methods give a <see cref="Task" />, which it can bind.
///     </para>
///     <para>
///         The expected value comes first, as it did in the NUnit calls these replace.
///     </para>
/// </remarks>
// Run awaits each assertion below, one call away from the location that makes it. The TUnit
// analyzer recognizes only an await in the same expression, thus it reads each of these as an
// assertion with no check. An await in each method needs two copies of each method: one with
// the message and one with no message.
#pragma warning disable TUnitAssertions0002
// Called only from F#, which inspectcode does not include in its analysis, thus each member
// here reads as dead.
[PublicAPI]
public static class Expect
{
    /// <summary>Asserts that <paramref name="actual" /> equals <paramref name="expected" />.</summary>
    public static Task Equal<T>(T expected, T actual, string? because = null) =>
        Run(assertion: Assert.That(actual).IsEqualTo(expected), because: because);

    /// <summary>Asserts that <paramref name="actual" /> is the same object as <paramref name="expected" />.</summary>
    /// <remarks>
    ///     This has no type parameter, as the AreSame of NUnit had none. Reference identity has no
    ///     requirement for the two sides to share a static type. Some of these call sites compare an
    ///     exception to an exception with its base type.
    /// </remarks>
    public static Task Same(object? expected, object? actual, string? because = null) =>
        Run(assertion: Assert.That(actual).IsSameReferenceAs(expected), because: because);

    /// <summary>
    ///     Asserts that <paramref name="actual" /> holds <paramref name="expected" /> and nothing
    ///     more, in that order.
    /// </summary>
    public static Task Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? because = null) =>
        Run(
            assertion: Assert.That(actual).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching),
            because: because);

    /// <summary>
    ///     Asserts that <paramref name="actual" /> holds <paramref name="expected" /> and nothing
    ///     more, in any order.
    /// </summary>
    public static Task SameItems<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? because = null) =>
        Run(assertion: Assert.That(actual).IsEquivalentTo(expected), because: because);

    /// <summary>Asserts that <paramref name="actual" /> is <see langword="true" />.</summary>
    public static Task True(bool actual, string? because = null) =>
        Run(assertion: Assert.That(actual).IsTrue(), because: because);

    /// <summary>Asserts that <paramref name="actual" /> is <see langword="false" />.</summary>
    public static Task False(bool actual, string? because = null) =>
        Run(assertion: Assert.That(actual).IsFalse(), because: because);

    /// <summary>Asserts that <paramref name="actual" /> is not <see langword="null" />.</summary>
    public static Task NotNull<T>(T actual, string? because = null)
        where T : class =>
        Run(assertion: Assert.That(actual).IsNotNull(), because: because);

    /// <summary>Asserts that <paramref name="actual" /> is less than <paramref name="limit" />.</summary>
    public static Task LessThan<T>(T limit, T actual, string? because = null)
        where T : IComparable<T> =>
        Run(assertion: Assert.That(actual).IsLessThan(limit), because: because);

    /// <summary>
    ///     Asserts that <paramref name="action" /> throws <typeparamref name="TException" /> and no
    ///     other type.
    /// </summary>
    public static Task Throws<TException>(Action action, string? because = null)
        where TException : Exception =>
        Run(assertion: Assert.That(action).ThrowsExactly<TException>(), because: because);

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
