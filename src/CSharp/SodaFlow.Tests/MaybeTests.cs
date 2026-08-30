using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class MaybeTests
    {
        [Test]
        public void DefaultConstructorTest()
        {
            Maybe<int> m = new Maybe<int>();

            Assert.IsFalse(m.HasValue());
        }

        [Test]
        public void DefaultValueTest()
        {
            Maybe<int> m = default(Maybe<int>);

            Assert.IsFalse(m.HasValue());
        }

        [Test]
        public void TestSome()
        {
            Maybe<int> m = Maybe.Some(2);

            int n = m.Match(v => v, () => 0);

            Assert.AreEqual(2, n);
        }

        [Test]
        public void TestNone()
        {
            Maybe<int> m = Maybe.None;

            int n = m.Match(v => 0, () => 1);

            Assert.AreEqual(1, n);
        }

        [Test]
        public void EqualityTest()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.Some(2);

            Assert.AreEqual(m1, m2);
        }

        [Test]
        public void EqualityTestNone()
        {
            Maybe<int> m1 = Maybe.None;
            Maybe<int> m2 = Maybe.None;

            Assert.AreEqual(m1, m2);
        }

        [Test]
        public void NonEqualityTest1()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.None;

            Assert.AreNotEqual(m1, m2);
        }

        [Test]
        public void NonEqualityTest2()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.Some(3);

            Assert.AreNotEqual(m1, m2);
        }

        [Test]
        public void EqualityOperatorTest()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.Some(2);

            Assert.IsTrue(m1 == m2);
        }

        [Test]
        public void EqualityOperatorTestNone()
        {
            Maybe<int> m1 = Maybe.None;
            Maybe<int> m2 = Maybe.None;

            Assert.IsTrue(m1 == m2);
        }

        [Test]
        public void NonEqualityOperatorTest1()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.None;

            Assert.IsTrue(m1 != m2);
        }

        [Test]
        public void NonEqualityOperatorTest2()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.Some(3);

            Assert.IsTrue(m1 != m2);
        }

        [Test]
        public void TestSomeIf()
        {
            Assert.AreEqual(Maybe.Some(2), Maybe.SomeIf(true, 2));
            Assert.AreEqual(Maybe<int>.None, Maybe.SomeIf(false, 2));
        }

        [Test]
        public void TestSomeIfLazy()
        {
            int calls = 0;

            Assert.AreEqual(Maybe.Some(2), Maybe.SomeIf(true, () => { calls++; return 2; }));
            Assert.AreEqual(1, calls);

            Assert.AreEqual(Maybe<int>.None, Maybe.SomeIf(false, () => { calls++; return 2; }));
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void TestSomeNotNullReference()
        {
            Assert.AreEqual(Maybe.Some("a"), Maybe.SomeNotNull("a"));
            Assert.AreEqual(Maybe<string>.None, Maybe.SomeNotNull((string)null));
        }

        [Test]
        public void TestSomeNotNullDiffersFromSome()
        {
            Assert.AreEqual(Maybe.Some((string)null), Maybe<string>.Some(null));
            Assert.AreNotEqual(Maybe.Some((string)null), Maybe.SomeNotNull((string)null));
        }

        [Test]
        public void TestSomeNotNullNullable()
        {
            Assert.AreEqual(Maybe.Some(2), Maybe.SomeNotNull((int?)2));
            Assert.AreEqual(Maybe<int>.None, Maybe.SomeNotNull((int?)null));
        }

        [Test]
        public void TestFromTryGet()
        {
            Assert.AreEqual(Maybe.Some(42), Maybe.FromTryGet<string, int>("42", int.TryParse));
            Assert.AreEqual(Maybe<int>.None, Maybe.FromTryGet<string, int>("x", int.TryParse));
        }

        [Test]
        public void TestFromTryGetThreeInputs()
        {
            Assert.AreEqual(
                Maybe.Some(1234),
                Maybe.FromTryGet<string, NumberStyles, IFormatProvider, int>(
                    "1,234",
                    NumberStyles.Integer | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    int.TryParse));
        }

        [Test]
        public void TestFromTryGetNoInput()
        {
            int captured = 7;

            Maybe<int> result = Maybe.FromTryGet<int>(
                (out int r) =>
                {
                    r = captured;
                    return true;
                });

            Assert.AreEqual(Maybe.Some(7), result);
        }

        [Test]
        public void ImplementsIEquatable()
        {
            Assert.IsTrue(typeof(IEquatable<Maybe<int>>).IsAssignableFrom(typeof(Maybe<int>)));
        }

        [Test]
        public void DefaultComparerDoesNotBox()
        {
            // A struct which does not implement IEquatable<T> gets ObjectEqualityComparer, which
            // compares through Equals(object) and boxes both operands on every comparison.
            Assert.AreNotEqual(
                "ObjectEqualityComparer`1",
                EqualityComparer<Maybe<int>>.Default.GetType().Name);
        }

        [Test]
        public void TypedEqualsAgreesWithOperator()
        {
            Assert.IsTrue(Maybe.Some(2).Equals(Maybe.Some(2)));
            Assert.IsFalse(Maybe.Some(2).Equals(Maybe.Some(3)));
            Assert.IsFalse(Maybe.Some(2).Equals(Maybe<int>.None));
            Assert.IsTrue(Maybe<int>.None.Equals(Maybe<int>.None));
            Assert.IsTrue(Maybe.Some((string)null).Equals(Maybe<string>.Some(null)));
            Assert.IsFalse(Maybe.Some((string)null).Equals(Maybe<string>.None));
        }

        [Test]
        public void TypedEqualsWorksInCollections()
        {
            Maybe<int>[] source = { Maybe.Some(1), Maybe<int>.None, Maybe.Some(1), Maybe<int>.None };

            CollectionAssert.AreEqual(new[] { Maybe.Some(1), Maybe<int>.None }, source.Distinct());
            Assert.IsTrue(source.Contains(Maybe<int>.None));

            Dictionary<Maybe<int>, string> d = new Dictionary<Maybe<int>, string>
            {
                { Maybe.Some(1), "one" },
                { Maybe<int>.None, "none" }
            };

            Assert.AreEqual("one", d[Maybe.Some(1)]);
            Assert.AreEqual("none", d[Maybe<int>.None]);
        }
    }
}
