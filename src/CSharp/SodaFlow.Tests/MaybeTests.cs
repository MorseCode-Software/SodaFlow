using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

public class MaybeTests
{
    [Test]
    public async Task DefaultConstructorTest()
    {
        Maybe<int> m = new();

        await Assert.That(m.HasValue()).IsFalse();
    }

    [Test]
    public async Task DefaultValueTest()
    {
        Maybe<int> m = default;

        await Assert.That(m.HasValue()).IsFalse();
    }

    [Test]
    public async Task TestSome()
    {
        Maybe<int> m = Maybe.Some(2);

        int n = m.Match(onSome: static v => v, onNone: static () => 0);

        await Assert.That(n).IsEqualTo(2);
    }

    [Test]
    public async Task TestNone()
    {
        Maybe<int> m = Maybe.None;

        int n = m.Match(onSome: static _ => 0, onNone: static () => 1);

        await Assert.That(n).IsEqualTo(1);
    }

    [Test]
    public async Task EqualityTest()
    {
        Maybe<int> m1 = Maybe.Some(2);
        Maybe<int> m2 = Maybe.Some(2);

        await Assert.That(m2).IsEqualTo(m1);
    }

    [Test]
    public async Task EqualityTestNone()
    {
        Maybe<int> m1 = Maybe.None;
        Maybe<int> m2 = Maybe.None;

        await Assert.That(m2).IsEqualTo(m1);
    }

    [Test]
    public async Task NonEqualityTest1()
    {
        Maybe<int> m1 = Maybe.Some(2);
        Maybe<int> m2 = Maybe.None;

        await Assert.That(m2).IsNotEqualTo(m1);
    }

    [Test]
    public async Task NonEqualityTest2()
    {
        Maybe<int> m1 = Maybe.Some(2);
        Maybe<int> m2 = Maybe.Some(3);

        await Assert.That(m2).IsNotEqualTo(m1);
    }

    [Test]
    public async Task EqualityOperatorTest()
    {
        Maybe<int> m1 = Maybe.Some(2);
        Maybe<int> m2 = Maybe.Some(2);

        await Assert.That(m1 == m2).IsTrue();
    }

    [Test]
    public async Task EqualityOperatorTestNone()
    {
        Maybe<int> m1 = Maybe.None;
        Maybe<int> m2 = Maybe.None;

        await Assert.That(m1 == m2).IsTrue();
    }

    [Test]
    public async Task NonEqualityOperatorTest1()
    {
        Maybe<int> m1 = Maybe.Some(2);
        Maybe<int> m2 = Maybe.None;

        await Assert.That(m1 != m2).IsTrue();
    }

    [Test]
    public async Task NonEqualityOperatorTest2()
    {
        Maybe<int> m1 = Maybe.Some(2);
        Maybe<int> m2 = Maybe.Some(3);

        await Assert.That(m1 != m2).IsTrue();
    }

    [Test]
    public async Task TestSomeIf()
    {
        await Assert.That(Maybe.SomeIf(condition: true, value: 2)).IsEqualTo(Maybe.Some(2));
        await Assert.That(Maybe.SomeIf(condition: false, value: 2)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestSomeIfLazy()
    {
        int calls = 0;

        await Assert.That(Maybe.SomeIf(
                condition: true,
                valueFactory: () =>
                {
                    calls++;
                    return 2;
                })).IsEqualTo(Maybe.Some(2));

        await Assert.That(calls).IsEqualTo(1);

        await Assert.That(Maybe.SomeIf(
                condition: false,
                valueFactory: () =>
                {
                    calls++;
                    return 2;
                })).IsEqualTo(Maybe<int>.None);

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TestSomeNotNullReference()
    {
        await Assert.That(Maybe.SomeNotNull("a")).IsEqualTo(Maybe.Some("a"));
        await Assert.That(Maybe.SomeNotNull<string>(null)).IsEqualTo(Maybe<string>.None);
    }

    [Test]
    public async Task TestSomeNotNullDiffersFromSome()
    {
        await Assert.That(Maybe<string?>.Some(null)).IsEqualTo(Maybe.Some<string?>(null));
        await Assert.That(Maybe.SomeNotNull<string>(null)).IsNotEqualTo(Maybe.Some<string?>(null));
    }

    [Test]
    public async Task TestSomeNotNullNullable()
    {
        await Assert.That(Maybe.SomeNotNull((int?)2)).IsEqualTo(Maybe.Some(2));
        await Assert.That(Maybe.SomeNotNull((int?)null)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestFromTryGet()
    {
        await Assert.That(Maybe.FromTryGet<string, int>(value: "42", tryGet: int.TryParse)).IsEqualTo(Maybe.Some(42));

        await Assert.That(Maybe.FromTryGet<string, int>(value: "x", tryGet: int.TryParse)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestFromTryGetThreeInputs() =>
        await Assert.That(Maybe.FromTryGet<string, NumberStyles, IFormatProvider, int>(
                value1: "1,234",
                value2: NumberStyles.Integer | NumberStyles.AllowThousands,
                value3: CultureInfo.InvariantCulture,
                tryGet: int.TryParse)).IsEqualTo(Maybe.Some(1234));

    [Test]
    public async Task TestFromTryGetNoInput()
    {
        const int captured = 7;

        Maybe<int> result =
            Maybe.FromTryGet(static (out int r) =>
            {
                r = captured;
                return true;
            });

        await Assert.That(result).IsEqualTo(Maybe.Some(7));
    }

    [Test]
    public async Task ImplementsIEquatable() =>
        await Assert.That(typeof(IEquatable<Maybe<int>>).IsAssignableFrom(typeof(Maybe<int>))).IsTrue();

    [Test]
    public async Task DefaultEqualityComparerDoesNotBox() =>
        // A struct which does not implement IEquatable<T> gets ObjectEqualityComparer, which
        // compares through Equals(object) and boxes both operands on every comparison.
        await Assert.That(EqualityComparer<Maybe<int>>.Default.GetType().Name).IsNotEqualTo("ObjectEqualityComparer`1");

    [Test]
    public async Task TypedEqualsAgreesWithOperator()
    {
        await Assert.That(Maybe.Some(2).Equals(Maybe.Some(2))).IsTrue();
        await Assert.That(Maybe.Some(2).Equals(Maybe.Some(3))).IsFalse();
        await Assert.That(Maybe.Some(2).Equals(Maybe<int>.None)).IsFalse();
        await Assert.That(Maybe<int>.None.Equals(Maybe<int>.None)).IsTrue();
        await Assert.That(Maybe.Some<string?>(null).Equals(Maybe<string?>.Some(null))).IsTrue();
        await Assert.That(Maybe.Some<string?>(null).Equals(Maybe<string?>.None)).IsFalse();
    }

    [Test]
    public async Task TypedEqualsWorksInCollections()
    {
        Maybe<int>[] source = [Maybe.Some(1), Maybe<int>.None, Maybe.Some(1), Maybe<int>.None];

        await Assert.That(source.Distinct()).IsEquivalentTo(new[] { Maybe.Some(1), Maybe<int>.None }, CollectionOrdering.Matching);
        await Assert.That(source.Contains(Maybe<int>.None)).IsTrue();

        Dictionary<Maybe<int>, string> d = new() { { Maybe.Some(1), "one" }, { Maybe<int>.None, "none" } };

        await Assert.That(d[Maybe.Some(1)]).IsEqualTo("one");
        await Assert.That(d[Maybe<int>.None]).IsEqualTo("none");
    }

    [Test]
    public async Task ImplementsIComparable() =>
        await Assert.That(typeof(IComparable<Maybe<int>>).IsAssignableFrom(typeof(Maybe<int>))).IsTrue();

    [Test]
    public async Task DefaultOrderingComparerDoesNotBox() =>
        // Without IComparable<T> a struct gets ObjectComparer, which compares through the
        // non-generic IComparable and boxes both operands.
        await Assert.That(Comparer<Maybe<int>>.Default.GetType().Name).IsNotEqualTo("ObjectComparer`1");

    [Test]
    public async Task CompareToOrdersNoneFirst()
    {
        await Assert.That(Maybe<int>.None.CompareTo(Maybe<int>.None)).IsEqualTo(0);
        await Assert.That(Maybe<int>.None.CompareTo(Maybe.Some(0))).IsLessThan(0);
        await Assert.That(Maybe.Some(0).CompareTo(Maybe<int>.None)).IsGreaterThan(0);
    }

    [Test]
    public async Task CompareToOrdersValuesByTheirOwnComparer()
    {
        await Assert.That(Maybe.Some(2).CompareTo(Maybe.Some(2))).IsEqualTo(0);
        await Assert.That(Maybe.Some(2).CompareTo(Maybe.Some(3))).IsLessThan(0);
        await Assert.That(Maybe.Some(3).CompareTo(Maybe.Some(2))).IsGreaterThan(0);
    }

    [Test]
    public async Task CompareToMatchesNullableOrdering()
    {
        // None sorts before every value, exactly as null does for Nullable<T>.
        await Assert.That(Math.Sign(Maybe<int>.None.CompareTo(Maybe.Some(0)))).IsEqualTo(Math.Sign(Comparer<int?>.Default.Compare(x: null, y: 0)));

        await Assert.That(Math.Sign(Maybe.Some(0).CompareTo(Maybe<int>.None))).IsEqualTo(Math.Sign(Comparer<int?>.Default.Compare(x: 0, y: null)));

        await Assert.That(Math.Sign(Maybe<int>.None.CompareTo(Maybe<int>.None))).IsEqualTo(Math.Sign(Comparer<int?>.Default.Compare(x: null, y: null)));
    }

    [Test]
    public async Task CompareToOrdersAContainedNullBeforeAContainedValue()
    {
        // A contained null is still a value, so it sorts after None and before "a".
        await Assert.That(Maybe<string?>.None.CompareTo(Maybe.Some<string?>(null))).IsLessThan(0);
        await Assert.That(Maybe.Some<string?>(null).CompareTo(Maybe.Some<string?>("a"))).IsLessThan(0);
    }

    [Test]
    public async Task SortingUsesTheOrdering()
    {
        Maybe<int>[] source = [Maybe.Some(3), Maybe<int>.None, Maybe.Some(1), Maybe<int>.None, Maybe.Some(2)];

        await Assert.That(source.OrderBy(static v => v)).IsEquivalentTo(new[] { Maybe<int>.None, Maybe<int>.None, Maybe.Some(1), Maybe.Some(2), Maybe.Some(3) }, CollectionOrdering.Matching);

        Maybe<int>[] sorted = (Maybe<int>[])source.Clone();
        Array.Sort(sorted);

        await Assert.That(sorted).IsEquivalentTo(new[] { Maybe<int>.None, Maybe<int>.None, Maybe.Some(1), Maybe.Some(2), Maybe.Some(3) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task CompareToIsConsistentWithEquality()
    {
        Maybe<int>[] values = [Maybe<int>.None, Maybe.Some(1), Maybe.Some(2)];

        foreach (Maybe<int> x in values)
        {
            foreach (Maybe<int> y in values)
            {
                await Assert.That(x.CompareTo(y) == 0).IsEqualTo(x == y).Because($"{x} against {y}");

                await Assert.That(-Math.Sign(y.CompareTo(x))).IsEqualTo(Math.Sign(x.CompareTo(y))).Because($"{x} against {y}");
            }
        }
    }
}
