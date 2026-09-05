using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class EitherTests
{
    [Test]
    public async Task DefaultValue2Test()
    {
        Either<Test1, Test2> e = default;

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt2(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultValue3Test()
    {
        Either<Test1, Test2, Test3> e = default;

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt3(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultValue4Test()
    {
        Either<Test1, Test2, Test3, Test4> e = default;

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt4(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultValue5Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5> e = default;

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt5(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultValue6Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6> e = default;

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt6(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultValue7Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e = default;

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt7(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultValue8Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e = default;

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt8(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultConstructor2Test()
    {
        Either<Test1, Test2> e = new();

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt2(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultConstructor3Test()
    {
        Either<Test1, Test2, Test3> e = new();

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt3(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultConstructor4Test()
    {
        Either<Test1, Test2, Test3, Test4> e = new();

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt4(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultConstructor5Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5> e = new();

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt5(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultConstructor6Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6> e = new();

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt6(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultConstructor7Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e = new();

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt7(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task DefaultConstructor8Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e = new();

        object? o = e.Upcast<IEither>().GetValueAsObject();

        await Assert.That(TestIt8(e)).IsEqualTo(1);
        await Assert.That(o).IsNull();
    }

    [Test]
    public async Task Either2Test()
    {
        Either<Test1, Test2> v1 = Either.First(new Test1());
        Either<Test1, Test2> v2 = Either.Second(new Test2());

        await Assert.That(TestIt2(v1)).IsEqualTo(1);
        await Assert.That(TestIt2(v2)).IsEqualTo(2);

        await Assert.That(TestIt2Action(v1)).IsEqualTo(1);
        await Assert.That(TestIt2Action(v2)).IsEqualTo(2);
    }

    [Test]
    public async Task Either3Test()
    {
        Either<Test1, Test2, Test3> v1 = Either.First(new Test1());
        Either<Test1, Test2, Test3> v2 = Either.Second(new Test2());
        Either<Test1, Test2, Test3> v3 = Either.Third(new Test3());

        await Assert.That(TestIt3(v1)).IsEqualTo(1);
        await Assert.That(TestIt3(v2)).IsEqualTo(2);
        await Assert.That(TestIt3(v3)).IsEqualTo(3);

        await Assert.That(TestIt3Action(v1)).IsEqualTo(1);
        await Assert.That(TestIt3Action(v2)).IsEqualTo(2);
        await Assert.That(TestIt3Action(v3)).IsEqualTo(3);
    }

    [Test]
    public async Task Either4Test()
    {
        Either<Test1, Test2, Test3, Test4> v1 = Either.First(new Test1());
        Either<Test1, Test2, Test3, Test4> v2 = Either.Second(new Test2());
        Either<Test1, Test2, Test3, Test4> v3 = Either.Third(new Test3());
        Either<Test1, Test2, Test3, Test4> v4 = Either.Fourth(new Test4());

        await Assert.That(TestIt4(v1)).IsEqualTo(1);
        await Assert.That(TestIt4(v2)).IsEqualTo(2);
        await Assert.That(TestIt4(v3)).IsEqualTo(3);
        await Assert.That(TestIt4(v4)).IsEqualTo(4);

        await Assert.That(TestIt4Action(v1)).IsEqualTo(1);
        await Assert.That(TestIt4Action(v2)).IsEqualTo(2);
        await Assert.That(TestIt4Action(v3)).IsEqualTo(3);
        await Assert.That(TestIt4Action(v4)).IsEqualTo(4);
    }

    [Test]
    public async Task Either5Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5> v1 = Either.First(new Test1());
        Either<Test1, Test2, Test3, Test4, Test5> v2 = Either.Second(new Test2());
        Either<Test1, Test2, Test3, Test4, Test5> v3 = Either.Third(new Test3());
        Either<Test1, Test2, Test3, Test4, Test5> v4 = Either.Fourth(new Test4());
        Either<Test1, Test2, Test3, Test4, Test5> v5 = Either.Fifth(new Test5());

        await Assert.That(TestIt5(v1)).IsEqualTo(1);
        await Assert.That(TestIt5(v2)).IsEqualTo(2);
        await Assert.That(TestIt5(v3)).IsEqualTo(3);
        await Assert.That(TestIt5(v4)).IsEqualTo(4);
        await Assert.That(TestIt5(v5)).IsEqualTo(5);

        await Assert.That(TestIt5Action(v1)).IsEqualTo(1);
        await Assert.That(TestIt5Action(v2)).IsEqualTo(2);
        await Assert.That(TestIt5Action(v3)).IsEqualTo(3);
        await Assert.That(TestIt5Action(v4)).IsEqualTo(4);
        await Assert.That(TestIt5Action(v5)).IsEqualTo(5);
    }

    [Test]
    public async Task Either6Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6> v1 = Either.First(new Test1());
        Either<Test1, Test2, Test3, Test4, Test5, Test6> v2 = Either.Second(new Test2());
        Either<Test1, Test2, Test3, Test4, Test5, Test6> v3 = Either.Third(new Test3());
        Either<Test1, Test2, Test3, Test4, Test5, Test6> v4 = Either.Fourth(new Test4());
        Either<Test1, Test2, Test3, Test4, Test5, Test6> v5 = Either.Fifth(new Test5());
        Either<Test1, Test2, Test3, Test4, Test5, Test6> v6 = Either.Sixth(new Test6());

        await Assert.That(TestIt6(v1)).IsEqualTo(1);
        await Assert.That(TestIt6(v2)).IsEqualTo(2);
        await Assert.That(TestIt6(v3)).IsEqualTo(3);
        await Assert.That(TestIt6(v4)).IsEqualTo(4);
        await Assert.That(TestIt6(v5)).IsEqualTo(5);
        await Assert.That(TestIt6(v6)).IsEqualTo(6);

        await Assert.That(TestIt6Action(v1)).IsEqualTo(1);
        await Assert.That(TestIt6Action(v2)).IsEqualTo(2);
        await Assert.That(TestIt6Action(v3)).IsEqualTo(3);
        await Assert.That(TestIt6Action(v4)).IsEqualTo(4);
        await Assert.That(TestIt6Action(v5)).IsEqualTo(5);
        await Assert.That(TestIt6Action(v6)).IsEqualTo(6);
    }

    [Test]
    public async Task Either7Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v1 = Either.First(new Test1());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v2 = Either.Second(new Test2());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v3 = Either.Third(new Test3());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v4 = Either.Fourth(new Test4());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v5 = Either.Fifth(new Test5());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v6 = Either.Sixth(new Test6());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v7 = Either.Seventh(new Test7());

        await Assert.That(TestIt7(v1)).IsEqualTo(1);
        await Assert.That(TestIt7(v2)).IsEqualTo(2);
        await Assert.That(TestIt7(v3)).IsEqualTo(3);
        await Assert.That(TestIt7(v4)).IsEqualTo(4);
        await Assert.That(TestIt7(v5)).IsEqualTo(5);
        await Assert.That(TestIt7(v6)).IsEqualTo(6);
        await Assert.That(TestIt7(v7)).IsEqualTo(7);

        await Assert.That(TestIt7Action(v1)).IsEqualTo(1);
        await Assert.That(TestIt7Action(v2)).IsEqualTo(2);
        await Assert.That(TestIt7Action(v3)).IsEqualTo(3);
        await Assert.That(TestIt7Action(v4)).IsEqualTo(4);
        await Assert.That(TestIt7Action(v5)).IsEqualTo(5);
        await Assert.That(TestIt7Action(v6)).IsEqualTo(6);
        await Assert.That(TestIt7Action(v7)).IsEqualTo(7);
    }

    [Test]
    public async Task Either8Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v1 = Either.First(new Test1());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v2 = Either.Second(new Test2());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v3 = Either.Third(new Test3());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v4 = Either.Fourth(new Test4());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v5 = Either.Fifth(new Test5());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v6 = Either.Sixth(new Test6());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v7 = Either.Seventh(new Test7());
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v8 = Either.Eighth(new Test8());

        await Assert.That(TestIt8(v1)).IsEqualTo(1);
        await Assert.That(TestIt8(v2)).IsEqualTo(2);
        await Assert.That(TestIt8(v3)).IsEqualTo(3);
        await Assert.That(TestIt8(v4)).IsEqualTo(4);
        await Assert.That(TestIt8(v5)).IsEqualTo(5);
        await Assert.That(TestIt8(v6)).IsEqualTo(6);
        await Assert.That(TestIt8(v7)).IsEqualTo(7);
        await Assert.That(TestIt8(v8)).IsEqualTo(8);

        await Assert.That(TestIt8Action(v1)).IsEqualTo(1);
        await Assert.That(TestIt8Action(v2)).IsEqualTo(2);
        await Assert.That(TestIt8Action(v3)).IsEqualTo(3);
        await Assert.That(TestIt8Action(v4)).IsEqualTo(4);
        await Assert.That(TestIt8Action(v5)).IsEqualTo(5);
        await Assert.That(TestIt8Action(v6)).IsEqualTo(6);
        await Assert.That(TestIt8Action(v7)).IsEqualTo(7);
        await Assert.That(TestIt8Action(v8)).IsEqualTo(8);
    }

    [Test]
    public async Task EqualityTest()
    {
        Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
        Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(2L);

        await Assert.That(e2).IsEqualTo(e1);
    }

    [Test]
    public async Task NonEqualityTest1()
    {
        Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
        Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.First(2);

        await Assert.That(e2).IsNotEqualTo(e1);
    }

    [Test]
    public async Task NonEqualityTest2()
    {
        Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
        Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(3L);

        await Assert.That(e2).IsNotEqualTo(e1);
    }

    [Test]
    public async Task EqualityOperatorTest()
    {
        Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
        Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(2L);

        await Assert.That(e1 == e2).IsTrue();
    }

    [Test]
    public async Task NonEqualityOperatorTest1()
    {
        Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
        Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.First(2);

        await Assert.That(e1 != e2).IsTrue();
    }

    [Test]
    public async Task NonEqualityOperatorTest2()
    {
        Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
        Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(3L);

        await Assert.That(e1 != e2).IsTrue();
    }

    private sealed class Test1;

    private sealed class Test2;

    private sealed class Test3;

    private sealed class Test4;

    private sealed class Test5;

    private sealed class Test6;

    private sealed class Test7;

    private sealed class Test8;

    [Test]
    public async Task SwapTest()
    {
        Either<int, string> first = Either.First(2);
        Either<int, string> second = Either.Second("a");

        await Assert.That(first.Swap()).IsEqualTo(Either<string, int>.Second(2));
        await Assert.That(second.Swap()).IsEqualTo(Either<string, int>.First("a"));
    }

    [Test]
    public async Task SwapTwiceIsIdentityTest()
    {
        Either<int, string> first = Either.First(2);
        Either<int, string> second = Either.Second("a");

        await Assert.That(first.Swap().Swap()).IsEqualTo(first);
        await Assert.That(second.Swap().Swap()).IsEqualTo(second);
    }

    [Test]
    public async Task SwapReachesTheOtherCaseTest()
    {
        // MapSecond only addresses the second case; swapping either side of it is how the
        // first case is reached, and the second swap puts the result back where it started.
        Either<int, string> e = Either.First(2);

        await Assert.That(e.Swap().MapSecond(static v => v.ToString()).Swap()).IsEqualTo(Either<string, string>.First("2"));
    }

    private static int TestIt2(Either<Test1, Test2> e) => e.Match(onFirst: static _ => 1, onSecond: static _ => 2);

    private static int TestIt2Action(Either<Test1, Test2> e)
    {
        int n = 0;

        e.MatchVoid(
            onFirst: _ =>
            {
                n = 1;
            },
            onSecond: _ =>
            {
                n = 2;
            });

        return n;
    }

    private static int TestIt3(Either<Test1, Test2, Test3> e) =>
        e.Match(onFirst: static _ => 1, onSecond: static _ => 2, onThird: static _ => 3);

    private static int TestIt3Action(Either<Test1, Test2, Test3> e)
    {
        int n = 0;

        e.MatchVoid(
            onFirst: _ =>
            {
                n = 1;
            },
            onSecond: _ =>
            {
                n = 2;
            },
            onThird: _ =>
            {
                n = 3;
            });

        return n;
    }

    private static int TestIt4(Either<Test1, Test2, Test3, Test4> e) =>
        e.Match(onFirst: static _ => 1, onSecond: static _ => 2, onThird: static _ => 3, onFourth: static _ => 4);

    private static int TestIt4Action(Either<Test1, Test2, Test3, Test4> e)
    {
        int n = 0;

        e.MatchVoid(
            onFirst: _ =>
            {
                n = 1;
            },
            onSecond: _ =>
            {
                n = 2;
            },
            onThird: _ =>
            {
                n = 3;
            },
            onFourth: _ =>
            {
                n = 4;
            });

        return n;
    }

    private static int TestIt5(Either<Test1, Test2, Test3, Test4, Test5> e) =>
        e.Match(
            onFirst: static _ => 1,
            onSecond: static _ => 2,
            onThird: static _ => 3,
            onFourth: static _ => 4,
            onFifth: static _ => 5);

    private static int TestIt5Action(Either<Test1, Test2, Test3, Test4, Test5> e)
    {
        int n = 0;

        e.MatchVoid(
            onFirst: _ =>
            {
                n = 1;
            },
            onSecond: _ =>
            {
                n = 2;
            },
            onThird: _ =>
            {
                n = 3;
            },
            onFourth: _ =>
            {
                n = 4;
            },
            onFifth: _ =>
            {
                n = 5;
            });

        return n;
    }

    private static int TestIt6(Either<Test1, Test2, Test3, Test4, Test5, Test6> e) =>
        e.Match(
            onFirst: static _ => 1,
            onSecond: static _ => 2,
            onThird: static _ => 3,
            onFourth: static _ => 4,
            onFifth: static _ => 5,
            onSixth: static _ => 6);

    private static int TestIt6Action(Either<Test1, Test2, Test3, Test4, Test5, Test6> e)
    {
        int n = 0;

        e.MatchVoid(
            onFirst: _ =>
            {
                n = 1;
            },
            onSecond: _ =>
            {
                n = 2;
            },
            onThird: _ =>
            {
                n = 3;
            },
            onFourth: _ =>
            {
                n = 4;
            },
            onFifth: _ =>
            {
                n = 5;
            },
            onSixth: _ =>
            {
                n = 6;
            });

        return n;
    }

    private static int TestIt7(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e) =>
        e.Match(
            onFirst: static _ => 1,
            onSecond: static _ => 2,
            onThird: static _ => 3,
            onFourth: static _ => 4,
            onFifth: static _ => 5,
            onSixth: static _ => 6,
            onSeventh: static _ => 7);

    private static int TestIt7Action(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e)
    {
        int n = 0;

        e.MatchVoid(
            onFirst: _ =>
            {
                n = 1;
            },
            onSecond: _ =>
            {
                n = 2;
            },
            onThird: _ =>
            {
                n = 3;
            },
            onFourth: _ =>
            {
                n = 4;
            },
            onFifth: _ =>
            {
                n = 5;
            },
            onSixth: _ =>
            {
                n = 6;
            },
            onSeventh: _ =>
            {
                n = 7;
            });

        return n;
    }

    private static int TestIt8(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e) =>
        e.Match(
            onFirst: static _ => 1,
            onSecond: static _ => 2,
            onThird: static _ => 3,
            onFourth: static _ => 4,
            onFifth: static _ => 5,
            onSixth: static _ => 6,
            onSeventh: static _ => 7,
            onEighth: static _ => 8);

    private static int TestIt8Action(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e)
    {
        int n = 0;

        e.MatchVoid(
            onFirst: _ =>
            {
                n = 1;
            },
            onSecond: _ =>
            {
                n = 2;
            },
            onThird: _ =>
            {
                n = 3;
            },
            onFourth: _ =>
            {
                n = 4;
            },
            onFifth: _ =>
            {
                n = 5;
            },
            onSixth: _ =>
            {
                n = 6;
            },
            onSeventh: _ =>
            {
                n = 7;
            },
            onEighth: _ =>
            {
                n = 8;
            });

        return n;
    }

    [Test]
    public async Task ImplementsIEquatable()
    {
        await Assert.That(typeof(IEquatable<Either<int, string>>).IsAssignableFrom(typeof(Either<int, string>))).IsTrue();

        await Assert.That(typeof(IEquatable<Either<int, string, bool, char, byte, long, short, uint>>).IsAssignableFrom(
                typeof(Either<int, string, bool, char, byte, long, short, uint>))).IsTrue();
    }

    [Test]
    public async Task DefaultComparerDoesNotBox() =>
        await Assert.That(EqualityComparer<Either<int, string>>.Default.GetType().Name).IsNotEqualTo("ObjectEqualityComparer`1");

    [Test]
    public async Task TypedEqualsAgreesWithOperator()
    {
        Either<int, string> a = Either.First(2);
        Either<int, string> b = Either.First(2);
        Either<int, string> c = Either.Second("2");

        await Assert.That(a.Equals(b)).IsTrue();
        await Assert.That(a.Equals(c)).IsFalse();
        await Assert.That(a.Equals(b)).IsEqualTo(a == b);
        await Assert.That(a.Equals(c)).IsEqualTo(a == c);
    }

    [Test]
    public async Task TypedEqualsWorksInCollections()
    {
        Either<int, string>[] source = [Either.First(1), Either.Second("a"), Either.First(1), Either.Second("a")];

        await Assert.That(source.Distinct()).IsEquivalentTo(new Either<int, string>[] { Either.First(1), Either.Second("a") }, CollectionOrdering.Matching);
    }
}
