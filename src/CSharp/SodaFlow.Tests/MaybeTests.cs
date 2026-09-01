using System;
using System.Collections.Generic;
using System.Globalization;
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
            Maybe<int> m = default;

            Assert.IsFalse(m.HasValue());
        }

        [Test]
        public void TestSome()
        {
            Maybe<int> m = Maybe.Some(2);

            int n = m.Match(onSome: v => v, onNone: () => 0);

            Assert.AreEqual(expected: 2, actual: n);
        }

        [Test]
        public void TestNone()
        {
            Maybe<int> m = Maybe.None;

            int n = m.Match(onSome: v => 0, onNone: () => 1);

            Assert.AreEqual(expected: 1, actual: n);
        }

        [Test]
        public void EqualityTest()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.Some(2);

            Assert.AreEqual(expected: m1, actual: m2);
        }

        [Test]
        public void EqualityTestNone()
        {
            Maybe<int> m1 = Maybe.None;
            Maybe<int> m2 = Maybe.None;

            Assert.AreEqual(expected: m1, actual: m2);
        }

        [Test]
        public void NonEqualityTest1()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.None;

            Assert.AreNotEqual(expected: m1, actual: m2);
        }

        [Test]
        public void NonEqualityTest2()
        {
            Maybe<int> m1 = Maybe.Some(2);
            Maybe<int> m2 = Maybe.Some(3);

            Assert.AreNotEqual(expected: m1, actual: m2);
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
            Assert.AreEqual(expected: Maybe.Some(2), actual: Maybe.SomeIf(condition: true, value: 2));
            Assert.AreEqual(expected: Maybe<int>.None, actual: Maybe.SomeIf(condition: false, value: 2));
        }

        [Test]
        public void TestSomeIfLazy()
        {
            int calls = 0;

            Assert.AreEqual(
                expected: Maybe.Some(2),
                actual: Maybe.SomeIf(
                    condition: true,
                    valueFactory: () =>
                    {
                        calls++;
                        return 2;
                    }));

            Assert.AreEqual(expected: 1, actual: calls);

            Assert.AreEqual(
                expected: Maybe<int>.None,
                actual: Maybe.SomeIf(
                    condition: false,
                    valueFactory: () =>
                    {
                        calls++;
                        return 2;
                    }));

            Assert.AreEqual(expected: 1, actual: calls);
        }

        [Test]
        public void TestSomeNotNullReference()
        {
            Assert.AreEqual(expected: Maybe.Some("a"), actual: Maybe.SomeNotNull("a"));
            Assert.AreEqual(expected: Maybe<string>.None, actual: Maybe.SomeNotNull((string)null));
        }

        [Test]
        public void TestSomeNotNullDiffersFromSome()
        {
            Assert.AreEqual(expected: Maybe.Some((string)null), actual: Maybe<string>.Some(null));
            Assert.AreNotEqual(expected: Maybe.Some((string)null), actual: Maybe.SomeNotNull((string)null));
        }

        [Test]
        public void TestSomeNotNullNullable()
        {
            Assert.AreEqual(expected: Maybe.Some(2), actual: Maybe.SomeNotNull((int?)2));
            Assert.AreEqual(expected: Maybe<int>.None, actual: Maybe.SomeNotNull((int?)null));
        }

        [Test]
        public void TestFromTryGet()
        {
            Assert.AreEqual(
                expected: Maybe.Some(42),
                actual: Maybe.FromTryGet<string, int>(value: "42", tryGet: int.TryParse));

            Assert.AreEqual(
                expected: Maybe<int>.None,
                actual: Maybe.FromTryGet<string, int>(value: "x", tryGet: int.TryParse));
        }

        [Test]
        public void TestFromTryGetThreeInputs() =>
            Assert.AreEqual(
                expected: Maybe.Some(1234),
                actual: Maybe.FromTryGet<string, NumberStyles, IFormatProvider, int>(
                    value1: "1,234",
                    value2: NumberStyles.Integer | NumberStyles.AllowThousands,
                    value3: CultureInfo.InvariantCulture,
                    tryGet: int.TryParse));

        [Test]
        public void TestFromTryGetNoInput()
        {
            int captured = 7;

            Maybe<int> result =
                Maybe.FromTryGet((out int r) =>
                {
                    r = captured;
                    return true;
                });

            Assert.AreEqual(expected: Maybe.Some(7), actual: result);
        }

        [Test]
        public void ImplementsIEquatable() =>
            Assert.IsTrue(typeof(IEquatable<Maybe<int>>).IsAssignableFrom(typeof(Maybe<int>)));

        [Test]
        public void DefaultEqualityComparerDoesNotBox() =>
            // A struct which does not implement IEquatable<T> gets ObjectEqualityComparer, which
            // compares through Equals(object) and boxes both operands on every comparison.
            Assert.AreNotEqual(
                expected: "ObjectEqualityComparer`1",
                actual: EqualityComparer<Maybe<int>>.Default.GetType().Name);

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

            CollectionAssert.AreEqual(expected: new[] { Maybe.Some(1), Maybe<int>.None }, actual: source.Distinct());
            Assert.IsTrue(source.Contains(Maybe<int>.None));

            Dictionary<Maybe<int>, string> d =
                new Dictionary<Maybe<int>, string> { { Maybe.Some(1), "one" }, { Maybe<int>.None, "none" } };

            Assert.AreEqual(expected: "one", actual: d[Maybe.Some(1)]);
            Assert.AreEqual(expected: "none", actual: d[Maybe<int>.None]);
        }

        [Test]
        public void ImplementsIComparable() =>
            Assert.IsTrue(typeof(IComparable<Maybe<int>>).IsAssignableFrom(typeof(Maybe<int>)));

        [Test]
        public void DefaultOrderingComparerDoesNotBox() =>
            // Without IComparable<T> a struct gets ObjectComparer, which compares through the
            // non-generic IComparable and boxes both operands.
            Assert.AreNotEqual(expected: "ObjectComparer`1", actual: Comparer<Maybe<int>>.Default.GetType().Name);

        [Test]
        public void CompareToOrdersNoneFirst()
        {
            Assert.AreEqual(expected: 0, actual: Maybe<int>.None.CompareTo(Maybe<int>.None));
            Assert.Less(arg1: Maybe<int>.None.CompareTo(Maybe.Some(0)), arg2: 0);
            Assert.Greater(arg1: Maybe.Some(0).CompareTo(Maybe<int>.None), arg2: 0);
        }

        [Test]
        public void CompareToOrdersValuesByTheirOwnComparer()
        {
            Assert.AreEqual(expected: 0, actual: Maybe.Some(2).CompareTo(Maybe.Some(2)));
            Assert.Less(arg1: Maybe.Some(2).CompareTo(Maybe.Some(3)), arg2: 0);
            Assert.Greater(arg1: Maybe.Some(3).CompareTo(Maybe.Some(2)), arg2: 0);
        }

        [Test]
        public void CompareToMatchesNullableOrdering()
        {
            // None sorts before every value, exactly as null does for Nullable<T>.
            Assert.AreEqual(
                expected: Math.Sign(Comparer<int?>.Default.Compare(x: null, y: 0)),
                actual: Math.Sign(Maybe<int>.None.CompareTo(Maybe.Some(0))));

            Assert.AreEqual(
                expected: Math.Sign(Comparer<int?>.Default.Compare(x: 0, y: null)),
                actual: Math.Sign(Maybe.Some(0).CompareTo(Maybe<int>.None)));

            Assert.AreEqual(
                expected: Math.Sign(Comparer<int?>.Default.Compare(x: null, y: null)),
                actual: Math.Sign(Maybe<int>.None.CompareTo(Maybe<int>.None)));
        }

        [Test]
        public void CompareToOrdersAContainedNullBeforeAContainedValue()
        {
            // A contained null is still a value, so it sorts after None and before "a".
            Assert.Less(arg1: Maybe<string>.None.CompareTo(Maybe.Some((string)null)), arg2: 0);
            Assert.Less(arg1: Maybe.Some((string)null).CompareTo(Maybe.Some("a")), arg2: 0);
        }

        [Test]
        public void SortingUsesTheOrdering()
        {
            Maybe<int>[] source = { Maybe.Some(3), Maybe<int>.None, Maybe.Some(1), Maybe<int>.None, Maybe.Some(2) };

            CollectionAssert.AreEqual(
                expected: new[] { Maybe<int>.None, Maybe<int>.None, Maybe.Some(1), Maybe.Some(2), Maybe.Some(3) },
                actual: source.OrderBy(v => v));

            Maybe<int>[] sorted = (Maybe<int>[])source.Clone();
            Array.Sort(sorted);

            CollectionAssert.AreEqual(
                expected: new[] { Maybe<int>.None, Maybe<int>.None, Maybe.Some(1), Maybe.Some(2), Maybe.Some(3) },
                actual: sorted);
        }

        [Test]
        public void CompareToIsConsistentWithEquality()
        {
            Maybe<int>[] values = { Maybe<int>.None, Maybe.Some(1), Maybe.Some(2) };

            foreach (Maybe<int> x in values)
            {
                foreach (Maybe<int> y in values)
                {
                    Assert.AreEqual(expected: x == y, actual: x.CompareTo(y) == 0, message: $"{x} against {y}");

                    Assert.AreEqual(
                        expected: Math.Sign(x.CompareTo(y)),
                        actual: -Math.Sign(y.CompareTo(x)),
                        message: $"{x} against {y}");
                }
            }
        }
    }
}