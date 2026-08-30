using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class MaybeExtensionMethodsTests
    {
        [Test]
        public void TestFlatten1()
        {
            Maybe<Maybe<int>> m = Maybe.None;

            Maybe<int> result = m.Flatten();

            Assert.AreEqual(Maybe<int>.None, result);
        }

        [Test]
        public void TestFlatten2()
        {
            Maybe<Maybe<int>> m = Maybe.Some(Maybe<int>.None);

            Maybe<int> result = m.Flatten();

            Assert.AreEqual(Maybe<int>.None, result);
        }

        [Test]
        public void TestFlatten3()
        {
            Maybe<Maybe<int>> m = Maybe.Some(Maybe.Some(5));

            Maybe<int> result = m.Flatten();

            Assert.AreEqual(Maybe.Some(5), result);
        }

        [Test]
        public void TestWhereSomeNone()
        {
            Maybe<int>[] m = { Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None };

            IEnumerable<int> result = m.WhereSome();

            CollectionAssert.AreEqual(new int[0], result);
        }

        [Test]
        public void TestWhereSomeSome()
        {
            Maybe<int>[] m = { Maybe<int>.None, Maybe.Some(2), Maybe.Some(5), Maybe<int>.None, Maybe.Some(7) };

            IEnumerable<int> result = m.WhereSome();

            CollectionAssert.AreEqual(new[] { 2, 5, 7 }, result);
        }

        [Test]
        public void TestWhereSomeAll()
        {
            Maybe<int>[] m = { Maybe.Some(3), Maybe.Some(2), Maybe.Some(5), Maybe.Some(4), Maybe.Some(7) };

            IEnumerable<int> result = m.WhereSome();

            CollectionAssert.AreEqual(new[] { 3, 2, 5, 4, 7 }, result);
        }

        [Test]
        public void TestAllSomeOrNoneNone()
        {
            Maybe<int>[] m = { Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None };

            Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

            Assert.AreEqual(Maybe<IEnumerable<int>>.None, result);
        }

        [Test]
        public void TestAllSomeOrNoneSome()
        {
            Maybe<int>[] m = { Maybe<int>.None, Maybe.Some(2), Maybe.Some(5), Maybe<int>.None, Maybe.Some(7) };

            Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

            Assert.AreEqual(Maybe<IEnumerable<int>>.None, result);
        }

        [Test]
        public void TestAllSomeOrNoneAll()
        {
            Maybe<int>[] m = { Maybe.Some(3), Maybe.Some(2), Maybe.Some(5), Maybe.Some(4), Maybe.Some(7) };

            Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

            IEnumerable<int> r = result.Match(v => v, () => null);
            Assert.IsNotNull(r);
            CollectionAssert.AreEqual(new[] { 3, 2, 5, 4, 7 }, r);
        }

        [Test]
        public void TestWhereSomeEmptyAndNullSource()
        {
            CollectionAssert.AreEqual(new int[0], new Maybe<int>[0].WhereSome());
            CollectionAssert.AreEqual(new int[0], ((IEnumerable<Maybe<int>>)null).WhereSome());
        }

        [Test]
        public void TestWhereSomeKeepsDefaultValues()
        {
            Maybe<int>[] m = { Maybe.Some(0), Maybe<int>.None, Maybe.Some(0) };

            CollectionAssert.AreEqual(new[] { 0, 0 }, m.WhereSome());
        }

        [Test]
        public void TestAllSomeOrNoneWithSelectorAll()
        {
            string[] source = { "1", "2", "3" };

            Maybe<IEnumerable<int>> result = source.AllSomeOrNone(s => s.TryParseInt32());

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Match(v => v, () => null));
        }

        [Test]
        public void TestAllSomeOrNoneWithSelectorSome()
        {
            string[] source = { "1", "x", "3" };

            Maybe<IEnumerable<int>> result = source.AllSomeOrNone(s => s.TryParseInt32());

            Assert.AreEqual(Maybe<IEnumerable<int>>.None, result);
        }

        [Test]
        public void TestAllSomeOrNoneWithSelectorEmpty()
        {
            Maybe<IEnumerable<int>> result = new string[0].AllSomeOrNone(s => s.TryParseInt32());

            CollectionAssert.AreEqual(new int[0], result.Match(v => v, () => null));
        }

        [Test]
        public void TestToEnumerable()
        {
            CollectionAssert.AreEqual(new[] { 2 }, Maybe.Some(2).ToEnumerable());
            CollectionAssert.AreEqual(new int[0], Maybe<int>.None.ToEnumerable());
        }

        [Test]
        public void TestToEnumerableFlattensInSelectMany()
        {
            string[] source = { "1", "x", "3" };

            IEnumerable<int> result = source.SelectMany(s => s.TryParseInt32().ToEnumerable());

            CollectionAssert.AreEqual(new[] { 1, 3 }, result);
        }

        [Test]
        public void TestToNullable()
        {
            Assert.AreEqual((int?)2, Maybe.Some(2).ToNullable());
            Assert.AreEqual((int?)null, Maybe<int>.None.ToNullable());
        }

        [Test]
        public void TestToMaybeReference()
        {
            Assert.AreEqual(Maybe.Some("a"), "a".ToMaybe());
            Assert.AreEqual(Maybe<string>.None, ((string)null).ToMaybe());
        }

        [Test]
        public void TestToMaybeNullable()
        {
            Assert.AreEqual(Maybe.Some(2), ((int?)2).ToMaybe());
            Assert.AreEqual(Maybe<int>.None, ((int?)null).ToMaybe());
        }

        [Test]
        public void TestToMaybeAndToNullableRoundTrip()
        {
            Assert.AreEqual((int?)2, ((int?)2).ToMaybe().ToNullable());
            Assert.AreEqual((int?)null, ((int?)null).ToMaybe().ToNullable());
        }

        [Test]
        public void TestValueOr()
        {
            Assert.AreEqual(2, Maybe.Some(2).ValueOr(9));
            Assert.AreEqual(9, Maybe<int>.None.ValueOr(9));
        }

        [Test]
        public void TestValueOrLazy()
        {
            int calls = 0;

            Assert.AreEqual(2, Maybe.Some(2).ValueOr(() => { calls++; return 9; }));
            Assert.AreEqual(0, calls);

            Assert.AreEqual(9, Maybe<int>.None.ValueOr(() => { calls++; return 9; }));
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void TestValueOrDefault()
        {
            Assert.AreEqual(2, Maybe.Some(2).ValueOrDefault());
            Assert.AreEqual(0, Maybe<int>.None.ValueOrDefault());
            Assert.AreEqual(null, Maybe<string>.None.ValueOrDefault());
        }

        [Test]
        public void TestValueOrThrow()
        {
            Assert.AreEqual(2, Maybe.Some(2).ValueOrThrow(() => new InvalidOperationException("no value")));

            InvalidOperationException e = Assert.Throws<InvalidOperationException>(
                () => Maybe<int>.None.ValueOrThrow(() => new InvalidOperationException("no value")));

            Assert.AreEqual("no value", e.Message);
        }

        [Test]
        public void TestOrElse()
        {
            Assert.AreEqual(Maybe.Some(2), Maybe.Some(2).OrElse(Maybe.Some(9)));
            Assert.AreEqual(Maybe.Some(9), Maybe<int>.None.OrElse(Maybe.Some(9)));
            Assert.AreEqual(Maybe<int>.None, Maybe<int>.None.OrElse(Maybe<int>.None));
        }

        [Test]
        public void TestOrElseLazy()
        {
            int calls = 0;

            Assert.AreEqual(Maybe.Some(2), Maybe.Some(2).OrElse(() => { calls++; return Maybe.Some(9); }));
            Assert.AreEqual(0, calls);

            Assert.AreEqual(Maybe.Some(9), Maybe<int>.None.OrElse(() => { calls++; return Maybe.Some(9); }));
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void TestOrElseChains()
        {
            Maybe<int> result = Maybe<int>.None.OrElse(Maybe<int>.None).OrElse(Maybe.Some(3));

            Assert.AreEqual(Maybe.Some(3), result);
        }

        [Test]
        public void TestLift2()
        {
            Assert.AreEqual(Maybe.Some(5), Maybe.Some(2).Lift(Maybe.Some(3), (a, b) => a + b));
            Assert.AreEqual(Maybe<int>.None, Maybe<int>.None.Lift(Maybe.Some(3), (a, b) => a + b));
            Assert.AreEqual(Maybe<int>.None, Maybe.Some(2).Lift(Maybe<int>.None, (a, b) => a + b));
        }

        [Test]
        public void TestLift2DoesNotRunFunctionWithoutBothValues()
        {
            int calls = 0;

            Maybe<int> result = Maybe.Some(2).Lift(
                Maybe<int>.None,
                (a, b) =>
                {
                    calls++;
                    return a + b;
                });

            Assert.AreEqual(Maybe<int>.None, result);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void TestLift3()
        {
            Assert.AreEqual(
                Maybe.Some(9),
                Maybe.Some(2).Lift(Maybe.Some(3), Maybe.Some(4), (a, b, c) => a + b + c));
            Assert.AreEqual(
                Maybe<int>.None,
                Maybe.Some(2).Lift(Maybe<int>.None, Maybe.Some(4), (a, b, c) => a + b + c));
        }

        [Test]
        public void TestLift4()
        {
            Assert.AreEqual(
                Maybe.Some(14),
                Maybe.Some(2).Lift(Maybe.Some(3), Maybe.Some(4), Maybe.Some(5), (a, b, c, d) => a + b + c + d));
            Assert.AreEqual(
                Maybe<int>.None,
                Maybe.Some(2).Lift(Maybe.Some(3), Maybe.Some(4), Maybe<int>.None, (a, b, c, d) => a + b + c + d));
        }
    }
}
