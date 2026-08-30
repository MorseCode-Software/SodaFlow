using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class MaybeMonadTests
    {
        [Test]
        public void TestSelect()
        {
            Assert.AreEqual(Maybe.Some(4), Maybe.Some(2).Select(v => v * 2));
            Assert.AreEqual(Maybe<int>.None, Maybe<int>.None.Select(v => v * 2));
        }

        [Test]
        public void TestWhere()
        {
            Assert.AreEqual(Maybe.Some(2), Maybe.Some(2).Where(v => v % 2 == 0));
            Assert.AreEqual(Maybe<int>.None, Maybe.Some(3).Where(v => v % 2 == 0));
            Assert.AreEqual(Maybe<int>.None, Maybe<int>.None.Where(v => v % 2 == 0));
        }

        [Test]
        public void TestWhereDoesNotRunPredicateWithoutValue()
        {
            int calls = 0;

            Maybe<int> result = Maybe<int>.None.Where(
                v =>
                {
                    calls++;
                    return true;
                });

            Assert.AreEqual(Maybe<int>.None, result);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void TestQuerySyntax()
        {
            Maybe<int> a = Maybe.Some(2);
            Maybe<int> b = Maybe.Some(3);

            Maybe<int> result = from x in a
                                from y in b
                                where x < y
                                select x * y;

            Assert.AreEqual(Maybe.Some(6), result);
        }

        [Test]
        public void TestQuerySyntaxNone()
        {
            Maybe<int> a = Maybe.Some(2);
            Maybe<int> b = Maybe<int>.None;

            Maybe<int> result = from x in a
                                from y in b
                                select x * y;

            Assert.AreEqual(Maybe<int>.None, result);
        }
    }
}
