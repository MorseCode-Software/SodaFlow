using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class MaybeTests
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

        await Assert.That(
                Maybe.SomeIf(
                    condition: true,
                    valueFactory: () =>
                    {
                        calls++;
                        return 2;
                    }))
            .IsEqualTo(Maybe.Some(2));

        await Assert.That(calls).IsEqualTo(1);

        await Assert.That(
                Maybe.SomeIf(
                    condition: false,
                    valueFactory: () =>
                    {
                        calls++;
                        return 2;
                    }))
            .IsEqualTo(Maybe<int>.None);

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
        // ReSharper disable once NullableWarningSuppressionIsUsed - Maybe<string> and Maybe<string?> are the
        // same type once the annotations are erased, and the assertion needs both sides annotated alike.
        await Assert.That(Maybe.SomeNotNull<string>(null)).IsNotEqualTo(Maybe.Some<string>(null!));
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
        await Assert.That(
                Maybe.FromTryGet<string, NumberStyles, IFormatProvider, int>(
                    value1: "1,234",
                    value2: NumberStyles.Integer | NumberStyles.AllowThousands,
                    value3: CultureInfo.InvariantCulture,
                    tryGet: int.TryParse))
            .IsEqualTo(Maybe.Some(1234));

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
        // A struct with no IEquatable<T> gets ObjectEqualityComparer, which
        // compares through Equals(object) and boxes the two operands on each compare.
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

        await Assert.That(source.Distinct())
            .IsEquivalentTo(expected: [Maybe.Some(1), Maybe<int>.None], ordering: CollectionOrdering.Matching);

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
        // non-generic IComparable and boxes the two operands.
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
        // None sorts before each value, as null does for Nullable<T>.
        await Assert.That(Math.Sign(Maybe<int>.None.CompareTo(Maybe.Some(0))))
            .IsEqualTo(Math.Sign(Comparer<int?>.Default.Compare(x: null, y: 0)));

        await Assert.That(Math.Sign(Maybe.Some(0).CompareTo(Maybe<int>.None)))
            .IsEqualTo(Math.Sign(Comparer<int?>.Default.Compare(x: 0, y: null)));

        await Assert.That(Math.Sign(Maybe<int>.None.CompareTo(Maybe<int>.None)))
            .IsEqualTo(Math.Sign(Comparer<int?>.Default.Compare(x: null, y: null)));
    }

    [Test]
    public async Task CompareToOrdersAContainedNullBeforeAContainedValue()
    {
        // A contained null is a value, so it sorts after None and before "a".
        await Assert.That(Maybe<string?>.None.CompareTo(Maybe.Some<string?>(null))).IsLessThan(0);
        await Assert.That(Maybe.Some<string?>(null).CompareTo(Maybe.Some<string?>("a"))).IsLessThan(0);
    }

    [Test]
    public async Task SortingUsesTheOrdering()
    {
        Maybe<int>[] source = [Maybe.Some(3), Maybe<int>.None, Maybe.Some(1), Maybe<int>.None, Maybe.Some(2)];

        await Assert.That(source.OrderBy(static v => v))
            .IsEquivalentTo(
                expected: [Maybe<int>.None, Maybe<int>.None, Maybe.Some(1), Maybe.Some(2), Maybe.Some(3)],
                ordering: CollectionOrdering.Matching);

        Maybe<int>[] sorted = (Maybe<int>[])source.Clone();
        Array.Sort(sorted);

        await Assert.That(sorted)
            .IsEquivalentTo(
                expected: [Maybe<int>.None, Maybe<int>.None, Maybe.Some(1), Maybe.Some(2), Maybe.Some(3)],
                ordering: CollectionOrdering.Matching);
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

                await Assert.That(-Math.Sign(y.CompareTo(x)))
                    .IsEqualTo(Math.Sign(x.CompareTo(y)))
                    .Because($"{x} against {y}");
            }
        }
    }

    // The members below read the fields, where each was a call to Match with a lambda. These tests
    // pin what each one does with a value, with no value, and with a contained null. Thus, they
    // test the rewrite against the code it replaced.
    [Test]
    public async Task MapTransformsAValueAndSkipsNone()
    {
        int calls = 0;

        Maybe<int> mapped = Maybe.Some(2).Map(v => v * 2 + calls++);
        Maybe<int> none = Maybe<int>.None.Map(v => v * 2 + calls++);

        await Assert.That(mapped).IsEqualTo(Maybe.Some(4));
        await Assert.That(none).IsEqualTo(Maybe<int>.None);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task MapGivesAContainedNullToTheFunction()
    {
        Maybe<bool> isNull = Maybe.Some<string?>(null).Map(static v => v is null);

        await Assert.That(isNull).IsEqualTo(Maybe.Some(true));
    }

    [Test]
    public async Task BindGivesTheFunctionsResultAndSkipsNone()
    {
        int calls = 0;

        Maybe<int> some = Maybe.Some(2).Bind(v => Maybe.Some(v + 1 + calls++));
        Maybe<int> toNone = Maybe.Some(2).Bind(static _ => Maybe<int>.None);
        Maybe<int> none = Maybe<int>.None.Bind(v => Maybe.Some(v + calls++));

        await Assert.That(some).IsEqualTo(Maybe.Some(3));
        await Assert.That(toNone).IsEqualTo(Maybe<int>.None);
        await Assert.That(none).IsEqualTo(Maybe<int>.None);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task MatchVoidRunsExactlyOneAction()
    {
        List<string> ran = [];

        Maybe.Some(2).MatchVoid(onSome: v => ran.Add($"some {v}"), onNone: () => ran.Add("none"));
        Maybe<int>.None.MatchVoid(onSome: v => ran.Add($"some {v}"), onNone: () => ran.Add("none"));
        Maybe.Some<string?>(null).MatchVoid(onSome: v => ran.Add($"some {v ?? "null"}"), onNone: () => ran.Add("none"));

        await Assert.That(ran)
            .IsEquivalentTo(expected: ["some 2", "none", "some null"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task MatchSomeRunsOnlyWithAValue()
    {
        List<int> ran = [];

        Maybe.Some(2).MatchSome(ran.Add);
        Maybe<int>.None.MatchSome(ran.Add);

        await Assert.That(ran).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task MatchNoneRunsOnlyWithoutAValue()
    {
        int ran = 0;

        Maybe.Some(2).MatchNone(() => ran++);
        Maybe<int>.None.MatchNone(() => ran++);

        await Assert.That(ran).IsEqualTo(1);
    }

    // An overload with a state gives that state to the function that runs. Thus, a caller gives a
    // local as the state and makes each lambda static, and the lambda captures nothing.
    [Test]
    public async Task MatchWithAStateGivesTheStateAndTheValueToOneFunction()
    {
        int some =
            Maybe.Some(2).Match(state: 10, onSome: static (state, v) => state + v, onNone: static state => -state);

        int none =
            Maybe<int>.None.Match(state: 10, onSome: static (state, v) => state + v, onNone: static state => -state);

        bool isNull =
            Maybe.Some<string?>(null)
                .Match(state: 10, onSome: static (state, v) => state == 10 && v is null, onNone: static _ => false);

        await Assert.That(some).IsEqualTo(12);
        await Assert.That(none).IsEqualTo(-10);
        await Assert.That(isNull).IsTrue();
    }

    [Test]
    public async Task MatchVoidWithAStateRunsExactlyOneAction()
    {
        List<string> ran = [];

        Maybe.Some(2)
            .MatchVoid(
                state: ran,
                onSome: static (list, v) => list.Add($"some {v}"),
                onNone: static list => list.Add("none"));

        Maybe<int>.None.MatchVoid(
            state: ran,
            onSome: static (list, v) => list.Add($"some {v}"),
            onNone: static list => list.Add("none"));

        await Assert.That(ran).IsEquivalentTo(expected: ["some 2", "none"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task MatchSomeWithAStateRunsOnlyWithAValue()
    {
        List<int> ran = [];

        Maybe.Some(2).MatchSome(state: ran, onSome: static (list, v) => list.Add(v));
        Maybe<int>.None.MatchSome(state: ran, onSome: static (list, v) => list.Add(v));

        await Assert.That(ran).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task MatchNoneWithAStateRunsOnlyWithoutAValue()
    {
        List<string> ran = [];

        Maybe.Some(2).MatchNone(state: ran, onNone: static list => list.Add("none"));
        Maybe<int>.None.MatchNone(state: ran, onNone: static list => list.Add("none"));

        await Assert.That(ran).IsEquivalentTo(expected: ["none"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task MapWithAStateTransformsAValueAndSkipsNone()
    {
        List<int> calls = [];

        Maybe<int> mapped =
            Maybe.Some(2)
                .Map(
                    state: (Factor: 3, Calls: calls),
                    f: static (state, v) =>
                    {
                        state.Calls.Add(v);

                        return v * state.Factor;
                    });

        Maybe<int> none =
            Maybe<int>.None.Map(
                state: (Factor: 3, Calls: calls),
                f: static (state, v) =>
                {
                    state.Calls.Add(v);

                    return v * state.Factor;
                });

        await Assert.That(mapped).IsEqualTo(Maybe.Some(6));
        await Assert.That(none).IsEqualTo(Maybe<int>.None);
        await Assert.That(calls).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
    }

#if NET
    // The reference assemblies of net472 do not have the read of the bytes that a thread allocated.
    // Thus, this test is for the newer target only. The overloads are the same code on each target.
    //
    // The purpose of an overload with a state is a call that allocates nothing. The same match
    // with a lambda that captures the local allocates for each call. The test reads that also, to
    // show that the measurement can see an allocation.
    //
    // The offset is an argument of the test. A constant needs no capture, and a lambda with no
    // capture allocates nothing.
    [Test]
    [Arguments(10)]
    public async Task TheOverloadsWithAStateAllocateNothing(int offset)
    {
        const int calls = 1000;
        Maybe<int> some = Maybe.Some(2);
        Maybe<int> none = Maybe<int>.None;
        int[] total = [0];
        long sum = 0;

        for (int i = 0; i < calls; i++)
        {
            sum += WithAState(some: some, none: none, offset: offset, total: total);
            sum += some.Match(onSome: v => v + offset, onNone: () => offset);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < calls; i++)
        {
            sum += WithAState(some: some, none: none, offset: offset, total: total);
        }

        long withAState = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < calls; i++)
        {
            sum += some.Match(onSome: v => v + offset, onNone: () => offset);
        }

        long withACapture = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(withAState).IsEqualTo(0);
        await Assert.That(withACapture).IsGreaterThan(0);
        await Assert.That(sum).IsNotEqualTo(0);

        return;

        static int WithAState(Maybe<int> some, Maybe<int> none, int offset, int[] total)
        {
            int result =
                some.Match(state: offset, onSome: static (state, v) => state + v, onNone: static state => state)
                + none.Match(state: offset, onSome: static (state, v) => state + v, onNone: static state => state);

            some.MatchVoid(
                state: total,
                onSome: static (state, v) => state[0] += v,
                onNone: static state => state[0]--);

            some.MatchSome(state: total, onSome: static (state, v) => state[0] += v);
            none.MatchNone(state: total, onNone: static state => state[0]++);

            return result + some.Map(state: offset, f: static (state, v) => state * v).ValueOr(0);
        }
    }
#endif

    [Test]
    public async Task ValueOrGivesTheValueOrTheDefault()
    {
        int factoryCalls = 0;

        await Assert.That(Maybe.Some(2).ValueOr(7)).IsEqualTo(2);
        await Assert.That(Maybe<int>.None.ValueOr(7)).IsEqualTo(7);
        await Assert.That(Maybe.Some(2).ValueOr(() => 7 + factoryCalls++)).IsEqualTo(2);
        await Assert.That(Maybe<int>.None.ValueOr(() => 7 + factoryCalls++)).IsEqualTo(7);
        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(Maybe.Some<string?>(null).ValueOr("default")).IsNull();
    }

    [Test]
    public async Task ValueOrDefaultGivesTheDefaultForNone()
    {
        await Assert.That(Maybe.Some(2).ValueOrDefault()).IsEqualTo(2);
        await Assert.That(Maybe<int>.None.ValueOrDefault()).IsEqualTo(0);
        await Assert.That(Maybe<string>.None.ValueOrDefault()).IsNull();
    }

    [Test]
    public async Task ValueOrThrowThrowsOnlyForNone()
    {
        int calls = 0;

        await Assert.That(Maybe.Some(2).ValueOrThrow(() => new InvalidOperationException($"{calls++}"))).IsEqualTo(2);
        await Assert.That(calls).IsEqualTo(0);

        await Assert.That(static () => Maybe<int>.None.ValueOrThrow(static () => new InvalidOperationException("none")))
            .Throws<InvalidOperationException>()
            .WithMessage("none");
    }

    [Test]
    public async Task OrElseKeepsAValueAndTakesTheOtherForNone()
    {
        int calls = 0;

        await Assert.That(Maybe.Some(2).OrElse(Maybe.Some(7))).IsEqualTo(Maybe.Some(2));
        await Assert.That(Maybe<int>.None.OrElse(Maybe.Some(7))).IsEqualTo(Maybe.Some(7));
        await Assert.That(Maybe<int>.None.OrElse(Maybe<int>.None)).IsEqualTo(Maybe<int>.None);
        await Assert.That(Maybe.Some(2).OrElse(() => Maybe.Some(7 + calls++))).IsEqualTo(Maybe.Some(2));
        await Assert.That(Maybe<int>.None.OrElse(() => Maybe.Some(7 + calls++))).IsEqualTo(Maybe.Some(7));
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task ToNullableFollowsTheValue()
    {
        await Assert.That(Maybe.Some(2).ToNullable()).IsEqualTo(2);
        await Assert.That(Maybe<int>.None.ToNullable()).IsNull();
    }

    [Test]
    public async Task LiftCombinesValuesAndSkipsWhenAnyIsNone()
    {
        int calls = 0;
        Maybe<int> none = Maybe<int>.None;

        await Assert.That(Maybe.Some(1).Lift(b: Maybe.Some(2), f: Add2)).IsEqualTo(Maybe.Some(3));
        await Assert.That(Maybe.Some(1).Lift(b: none, f: Add2)).IsEqualTo(none);
        await Assert.That(none.Lift(b: Maybe.Some(2), f: Add2)).IsEqualTo(none);
        await Assert.That(Maybe.Some(1).Lift(b: Maybe.Some(2), c: Maybe.Some(3), f: Add3)).IsEqualTo(Maybe.Some(6));
        await Assert.That(Maybe.Some(1).Lift(b: Maybe.Some(2), c: none, f: Add3)).IsEqualTo(none);

        await Assert.That(Maybe.Some(1).Lift(b: Maybe.Some(2), c: Maybe.Some(3), d: Maybe.Some(4), f: Add4))
            .IsEqualTo(Maybe.Some(10));

        await Assert.That(Maybe.Some(1).Lift(b: Maybe.Some(2), c: Maybe.Some(3), d: none, f: Add4)).IsEqualTo(none);
        await Assert.That(calls).IsEqualTo(3);
        return;

        int Add2(int a, int b)
        {
            calls++;
            return a + b;
        }

        int Add3(int a, int b, int c)
        {
            calls++;
            return a + b + c;
        }

        int Add4(int a, int b, int c, int d)
        {
            calls++;
            return a + b + c + d;
        }
    }

    [Test]
    public async Task UntypedMembersReadTheValueAsAnObject()
    {
        List<string> ran = [];
        IMaybe some = Maybe.Some(2);
        IMaybe none = Maybe<int>.None;

        await Assert.That(some.Match(onSome: static v => $"some {v}", onNone: static () => "none")).IsEqualTo("some 2");
        await Assert.That(none.Match(onSome: static v => $"some {v}", onNone: static () => "none")).IsEqualTo("none");

        some.MatchVoid(onSome: v => ran.Add($"void some {v}"), onNone: () => ran.Add("void none"));
        none.MatchVoid(onSome: v => ran.Add($"void some {v}"), onNone: () => ran.Add("void none"));
        some.MatchSome(v => ran.Add($"some {v}"));
        none.MatchSome(v => ran.Add($"some {v}"));
        some.MatchNone(() => ran.Add("none"));
        none.MatchNone(() => ran.Add("none"));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["void some 2", "void none", "some 2", "none"],
                ordering: CollectionOrdering.Matching);
    }
}
