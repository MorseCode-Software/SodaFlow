using System;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class MaybeMonadTests
{
    [Test]
    public async Task TestSelect()
    {
        await Assert.That(Maybe.Some(2).Select(static v => v * 2)).IsEqualTo(Maybe.Some(4));
        await Assert.That(Maybe<int>.None.Select(static v => v * 2)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestWhere()
    {
        await Assert.That(Maybe.Some(2).Where(static v => v % 2 == 0)).IsEqualTo(Maybe.Some(2));
        await Assert.That(Maybe.Some(3).Where(static v => v % 2 == 0)).IsEqualTo(Maybe<int>.None);
        await Assert.That(Maybe<int>.None.Where(static v => v % 2 == 0)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestWhereDoesNotRunPredicateWithoutValue()
    {
        int calls = 0;

        Maybe<int> result =
            Maybe<int>.None.Where(_ =>
            {
                calls++;
                return true;
            });

        await Assert.That(result).IsEqualTo(Maybe<int>.None);
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task TestQuerySyntax()
    {
        Maybe<int> a = Maybe.Some(2);
        Maybe<int> b = Maybe.Some(3);

        Maybe<int> result =
            from x in a
            from y in b
            where x < y
            select x * y;

        await Assert.That(result).IsEqualTo(Maybe.Some(6));
    }

    [Test]
    public async Task TestQuerySyntaxNone()
    {
        Maybe<int> a = Maybe.Some(2);
        Maybe<int> b = Maybe<int>.None;

        Maybe<int> result =
            from x in a
            from y in b
            select x * y;

        await Assert.That(result).IsEqualTo(Maybe<int>.None);
    }

    /// <summary>
    ///     The functions of a query run only for the values that are there. With no
    ///     first value, the transformation does not run. With no second value, the
    ///     collation does not run.
    /// </summary>
    [Test]
    public async Task TestQueryDoesNotRunFunctionsWithoutValues()
    {
        Maybe<int> fromNone =
            Maybe<int>.None.SelectMany<int, int, int>(
                transformation: static _ => throw new InvalidOperationException("The transformation ran."),
                collation: static (_, _) => throw new InvalidOperationException("The collation ran."));

        await Assert.That(fromNone).IsEqualTo(Maybe<int>.None);

        Maybe<int> toNone =
            Maybe.Some(2).SelectMany<int, int, int>(
                transformation: static _ => Maybe<int>.None,
                collation: static (_, _) => throw new InvalidOperationException("The collation ran."));

        await Assert.That(toNone).IsEqualTo(Maybe<int>.None);
    }
}
