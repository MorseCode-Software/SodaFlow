using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

[TestFixture]
public class MaybeMonadTests
{
    [Test]
    public void TestSelect()
    {
        Assert.AreEqual(expected: Maybe.Some(4), actual: Maybe.Some(2).Select(static v => v * 2));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Maybe<int>.None.Select(static v => v * 2));
    }

    [Test]
    public void TestWhere()
    {
        Assert.AreEqual(expected: Maybe.Some(2), actual: Maybe.Some(2).Where(static v => v % 2 == 0));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Maybe.Some(3).Where(static v => v % 2 == 0));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Maybe<int>.None.Where(static v => v % 2 == 0));
    }

    [Test]
    public void TestWhereDoesNotRunPredicateWithoutValue()
    {
        int calls = 0;

        Maybe<int> result =
            Maybe<int>.None.Where(_ =>
            {
                calls++;
                return true;
            });

        Assert.AreEqual(expected: Maybe<int>.None, actual: result);
        Assert.AreEqual(expected: 0, actual: calls);
    }

    [Test]
    public void TestQuerySyntax()
    {
        Maybe<int> a = Maybe.Some(2);
        Maybe<int> b = Maybe.Some(3);

        Maybe<int> result =
            from x in a
            from y in b
            where x < y
            select x * y;

        Assert.AreEqual(expected: Maybe.Some(6), actual: result);
    }

    [Test]
    public void TestQuerySyntaxNone()
    {
        Maybe<int> a = Maybe.Some(2);
        Maybe<int> b = Maybe<int>.None;

        Maybe<int> result =
            from x in a
            from y in b
            select x * y;

        Assert.AreEqual(expected: Maybe<int>.None, actual: result);
    }
}
