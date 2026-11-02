using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using Either3 = SodaFlow.Functional.Either<int, int, int>;
using Either4 = SodaFlow.Functional.Either<int, int, int, int>;
using Either5 = SodaFlow.Functional.Either<int, int, int, int, int>;
using Either6 = SodaFlow.Functional.Either<int, int, int, int, int, int>;
using Either7 = SodaFlow.Functional.Either<int, int, int, int, int, int, int>;
using Either8 = SodaFlow.Functional.Either<int, int, int, int, int, int, int, int>;
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

        await Assert.That(() => TestIt2(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultValue3Test()
    {
        Either<Test1, Test2, Test3> e = default;

        await Assert.That(() => TestIt3(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultValue4Test()
    {
        Either<Test1, Test2, Test3, Test4> e = default;

        await Assert.That(() => TestIt4(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultValue5Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5> e = default;

        await Assert.That(() => TestIt5(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultValue6Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6> e = default;

        await Assert.That(() => TestIt6(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5, Test6>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultValue7Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e = default;

        await Assert.That(() => TestIt7(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultValue8Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e = default;

        await Assert.That(() => TestIt8(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();

        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8>.First(new Test1()))
            .IsFalse();
    }

    [Test]
    public async Task DefaultConstructor2Test()
    {
        Either<Test1, Test2> e = new();

        await Assert.That(() => TestIt2(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor3Test()
    {
        Either<Test1, Test2, Test3> e = new();

        await Assert.That(() => TestIt3(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor4Test()
    {
        Either<Test1, Test2, Test3, Test4> e = new();

        await Assert.That(() => TestIt4(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor5Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5> e = new();

        await Assert.That(() => TestIt5(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor6Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6> e = new();

        await Assert.That(() => TestIt6(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5, Test6>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor7Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e = new();

        await Assert.That(() => TestIt7(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();
        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7>.First(new Test1())).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor8Test()
    {
        Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e = new();

        await Assert.That(() => TestIt8(e)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => e.Upcast<IEither>().GetValueAsObject()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(e == default).IsTrue();

        await Assert.That(e == Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8>.First(new Test1()))
            .IsFalse();
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
        // MapSecond only addresses the second case. A change of one side of it is how the
        // first case, and the second change puts the result back at its initial position.
        Either<int, string> e = Either.First(2);

        await Assert.That(e.Swap().MapSecond(static v => v.ToString()).Swap())
            .IsEqualTo(Either<string, string>.First("2"));
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
        await Assert.That(typeof(IEquatable<Either<int, string>>).IsAssignableFrom(typeof(Either<int, string>)))
            .IsTrue();

        await Assert.That(
                typeof(IEquatable<Either<int, string, bool, char, byte, long, short, uint>>).IsAssignableFrom(
                    typeof(Either<int, string, bool, char, byte, long, short, uint>)))
            .IsTrue();
    }

    [Test]
    public async Task DefaultComparerDoesNotBox() =>
        await Assert.That(EqualityComparer<Either<int, string>>.Default.GetType().Name)
            .IsNotEqualTo("ObjectEqualityComparer`1");

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

        await Assert.That(source.Distinct())
            .IsEquivalentTo(
                expected: new Either<int, string>[] { Either.First(1), Either.Second("a") },
                ordering: CollectionOrdering.Matching);
    }

    // The map members, MatchVoid, and the members of the untyped interfaces read the fields. Each
    // was a call to Match with a lambda for each case. These tests pin what each one does for each
    // held case and for a default instance. Thus, they test the rewrite against the code it
    // replaced.

    [Test]
    public async Task MatchVoidRunsOnlyTheHeldCase2()
    {
        List<string> ran = [];

        Either<int, int>.First(1).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v));

        Either<int, int>.Second(2).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(static () =>
                default(Either<int, int>).MatchVoid(
                    onFirst: static _ => { },
                    onSecond: static _ => { }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MapsTransformOnlyTheHeldCase2()
    {
        int calls = 0;

        Func<int, int> ten = v =>
        {
            calls++;
            return v * 10;
        };

        await Assert.That(Either<int, int>.First(1).MapFirst(ten)).IsEqualTo(Either<int, int>.First(10));
        await Assert.That(Either<int, int>.First(1).MapSecond(ten)).IsEqualTo(Either<int, int>.First(1));

        await Assert.That(Either<int, int>.Second(2).MapFirst(ten)).IsEqualTo(Either<int, int>.Second(2));
        await Assert.That(Either<int, int>.Second(2).MapSecond(ten)).IsEqualTo(Either<int, int>.Second(20));
        await Assert.That(calls).IsEqualTo(2);

        await Assert.That(static () => default(Either<int, int>).MapFirst(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either<int, int>).MapSecond(static v => v))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UntypedMembersReadTheHeldCase2()
    {
        List<string> ran = [];

        IEitherOfTwo e1 = Either<int, int>.First(1);

        await Assert.That(e1.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v)).IsEqualTo("first 1");

        e1.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v));

        IEitherOfTwo e2 = Either<int, int>.Second(2);

        await Assert.That(e2.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v)).IsEqualTo("second 2");

        e2.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2"],
                ordering: CollectionOrdering.Matching);

        IEitherOfTwo d = default(Either<int, int>);

        await Assert.That(() => d.Match(
                onFirst: static _ => string.Empty,
                onSecond: static _ => string.Empty)).ThrowsExactly<InvalidOperationException>();

        await Assert.That(() => d.MatchVoid(
                onFirst: static _ => { },
                onSecond: static _ => { })).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MatchVoidRunsOnlyTheHeldCase3()
    {
        List<string> ran = [];

        Either3.First(1).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v));

        Either3.Second(2).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v));

        Either3.Third(3).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(static () =>
                default(Either3).MatchVoid(
                    onFirst: static _ => { },
                    onSecond: static _ => { },
                    onThird: static _ => { }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MapsTransformOnlyTheHeldCase3()
    {
        int calls = 0;

        Func<int, int> ten = v =>
        {
            calls++;
            return v * 10;
        };

        await Assert.That(Either3.First(1).MapFirst(ten)).IsEqualTo(Either3.First(10));
        await Assert.That(Either3.First(1).MapSecond(ten)).IsEqualTo(Either3.First(1));
        await Assert.That(Either3.First(1).MapThird(ten)).IsEqualTo(Either3.First(1));

        await Assert.That(Either3.Second(2).MapFirst(ten)).IsEqualTo(Either3.Second(2));
        await Assert.That(Either3.Second(2).MapSecond(ten)).IsEqualTo(Either3.Second(20));
        await Assert.That(Either3.Second(2).MapThird(ten)).IsEqualTo(Either3.Second(2));

        await Assert.That(Either3.Third(3).MapFirst(ten)).IsEqualTo(Either3.Third(3));
        await Assert.That(Either3.Third(3).MapSecond(ten)).IsEqualTo(Either3.Third(3));
        await Assert.That(Either3.Third(3).MapThird(ten)).IsEqualTo(Either3.Third(30));
        await Assert.That(calls).IsEqualTo(3);

        await Assert.That(static () => default(Either3).MapFirst(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either3).MapSecond(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either3).MapThird(static v => v))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UntypedMembersReadTheHeldCase3()
    {
        List<string> ran = [];

        IEitherOfThree e1 = Either3.First(1);

        await Assert.That(e1.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v)).IsEqualTo("first 1");

        e1.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v));

        IEitherOfThree e2 = Either3.Second(2);

        await Assert.That(e2.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v)).IsEqualTo("second 2");

        e2.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v));

        IEitherOfThree e3 = Either3.Third(3);

        await Assert.That(e3.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v)).IsEqualTo("third 3");

        e3.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3"],
                ordering: CollectionOrdering.Matching);

        IEitherOfThree d = default(Either3);

        await Assert.That(() => d.Match(
                onFirst: static _ => string.Empty,
                onSecond: static _ => string.Empty,
                onThird: static _ => string.Empty)).ThrowsExactly<InvalidOperationException>();

        await Assert.That(() => d.MatchVoid(
                onFirst: static _ => { },
                onSecond: static _ => { },
                onThird: static _ => { })).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MatchVoidRunsOnlyTheHeldCase4()
    {
        List<string> ran = [];

        Either4.First(1).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v));

        Either4.Second(2).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v));

        Either4.Third(3).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v));

        Either4.Fourth(4).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(static () =>
                default(Either4).MatchVoid(
                    onFirst: static _ => { },
                    onSecond: static _ => { },
                    onThird: static _ => { },
                    onFourth: static _ => { }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MapsTransformOnlyTheHeldCase4()
    {
        int calls = 0;

        Func<int, int> ten = v =>
        {
            calls++;
            return v * 10;
        };

        await Assert.That(Either4.First(1).MapFirst(ten)).IsEqualTo(Either4.First(10));
        await Assert.That(Either4.First(1).MapSecond(ten)).IsEqualTo(Either4.First(1));
        await Assert.That(Either4.First(1).MapThird(ten)).IsEqualTo(Either4.First(1));
        await Assert.That(Either4.First(1).MapFourth(ten)).IsEqualTo(Either4.First(1));

        await Assert.That(Either4.Second(2).MapFirst(ten)).IsEqualTo(Either4.Second(2));
        await Assert.That(Either4.Second(2).MapSecond(ten)).IsEqualTo(Either4.Second(20));
        await Assert.That(Either4.Second(2).MapThird(ten)).IsEqualTo(Either4.Second(2));
        await Assert.That(Either4.Second(2).MapFourth(ten)).IsEqualTo(Either4.Second(2));

        await Assert.That(Either4.Third(3).MapFirst(ten)).IsEqualTo(Either4.Third(3));
        await Assert.That(Either4.Third(3).MapSecond(ten)).IsEqualTo(Either4.Third(3));
        await Assert.That(Either4.Third(3).MapThird(ten)).IsEqualTo(Either4.Third(30));
        await Assert.That(Either4.Third(3).MapFourth(ten)).IsEqualTo(Either4.Third(3));

        await Assert.That(Either4.Fourth(4).MapFirst(ten)).IsEqualTo(Either4.Fourth(4));
        await Assert.That(Either4.Fourth(4).MapSecond(ten)).IsEqualTo(Either4.Fourth(4));
        await Assert.That(Either4.Fourth(4).MapThird(ten)).IsEqualTo(Either4.Fourth(4));
        await Assert.That(Either4.Fourth(4).MapFourth(ten)).IsEqualTo(Either4.Fourth(40));
        await Assert.That(calls).IsEqualTo(4);

        await Assert.That(static () => default(Either4).MapFirst(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either4).MapSecond(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either4).MapThird(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either4).MapFourth(static v => v))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UntypedMembersReadTheHeldCase4()
    {
        List<string> ran = [];

        IEitherOfFour e1 = Either4.First(1);

        await Assert.That(e1.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v)).IsEqualTo("first 1");

        e1.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v));

        IEitherOfFour e2 = Either4.Second(2);

        await Assert.That(e2.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v)).IsEqualTo("second 2");

        e2.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v));

        IEitherOfFour e3 = Either4.Third(3);

        await Assert.That(e3.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v)).IsEqualTo("third 3");

        e3.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v));

        IEitherOfFour e4 = Either4.Fourth(4);

        await Assert.That(e4.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v)).IsEqualTo("fourth 4");

        e4.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4"],
                ordering: CollectionOrdering.Matching);

        IEitherOfFour d = default(Either4);

        await Assert.That(() => d.Match(
                onFirst: static _ => string.Empty,
                onSecond: static _ => string.Empty,
                onThird: static _ => string.Empty,
                onFourth: static _ => string.Empty)).ThrowsExactly<InvalidOperationException>();

        await Assert.That(() => d.MatchVoid(
                onFirst: static _ => { },
                onSecond: static _ => { },
                onThird: static _ => { },
                onFourth: static _ => { })).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MatchVoidRunsOnlyTheHeldCase5()
    {
        List<string> ran = [];

        Either5.First(1).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v));

        Either5.Second(2).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v));

        Either5.Third(3).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v));

        Either5.Fourth(4).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v));

        Either5.Fifth(5).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(static () =>
                default(Either5).MatchVoid(
                    onFirst: static _ => { },
                    onSecond: static _ => { },
                    onThird: static _ => { },
                    onFourth: static _ => { },
                    onFifth: static _ => { }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MapsTransformOnlyTheHeldCase5()
    {
        int calls = 0;

        Func<int, int> ten = v =>
        {
            calls++;
            return v * 10;
        };

        await Assert.That(Either5.First(1).MapFirst(ten)).IsEqualTo(Either5.First(10));
        await Assert.That(Either5.First(1).MapSecond(ten)).IsEqualTo(Either5.First(1));
        await Assert.That(Either5.First(1).MapThird(ten)).IsEqualTo(Either5.First(1));
        await Assert.That(Either5.First(1).MapFourth(ten)).IsEqualTo(Either5.First(1));
        await Assert.That(Either5.First(1).MapFifth(ten)).IsEqualTo(Either5.First(1));

        await Assert.That(Either5.Second(2).MapFirst(ten)).IsEqualTo(Either5.Second(2));
        await Assert.That(Either5.Second(2).MapSecond(ten)).IsEqualTo(Either5.Second(20));
        await Assert.That(Either5.Second(2).MapThird(ten)).IsEqualTo(Either5.Second(2));
        await Assert.That(Either5.Second(2).MapFourth(ten)).IsEqualTo(Either5.Second(2));
        await Assert.That(Either5.Second(2).MapFifth(ten)).IsEqualTo(Either5.Second(2));

        await Assert.That(Either5.Third(3).MapFirst(ten)).IsEqualTo(Either5.Third(3));
        await Assert.That(Either5.Third(3).MapSecond(ten)).IsEqualTo(Either5.Third(3));
        await Assert.That(Either5.Third(3).MapThird(ten)).IsEqualTo(Either5.Third(30));
        await Assert.That(Either5.Third(3).MapFourth(ten)).IsEqualTo(Either5.Third(3));
        await Assert.That(Either5.Third(3).MapFifth(ten)).IsEqualTo(Either5.Third(3));

        await Assert.That(Either5.Fourth(4).MapFirst(ten)).IsEqualTo(Either5.Fourth(4));
        await Assert.That(Either5.Fourth(4).MapSecond(ten)).IsEqualTo(Either5.Fourth(4));
        await Assert.That(Either5.Fourth(4).MapThird(ten)).IsEqualTo(Either5.Fourth(4));
        await Assert.That(Either5.Fourth(4).MapFourth(ten)).IsEqualTo(Either5.Fourth(40));
        await Assert.That(Either5.Fourth(4).MapFifth(ten)).IsEqualTo(Either5.Fourth(4));

        await Assert.That(Either5.Fifth(5).MapFirst(ten)).IsEqualTo(Either5.Fifth(5));
        await Assert.That(Either5.Fifth(5).MapSecond(ten)).IsEqualTo(Either5.Fifth(5));
        await Assert.That(Either5.Fifth(5).MapThird(ten)).IsEqualTo(Either5.Fifth(5));
        await Assert.That(Either5.Fifth(5).MapFourth(ten)).IsEqualTo(Either5.Fifth(5));
        await Assert.That(Either5.Fifth(5).MapFifth(ten)).IsEqualTo(Either5.Fifth(50));
        await Assert.That(calls).IsEqualTo(5);

        await Assert.That(static () => default(Either5).MapFirst(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either5).MapSecond(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either5).MapThird(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either5).MapFourth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either5).MapFifth(static v => v))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UntypedMembersReadTheHeldCase5()
    {
        List<string> ran = [];

        IEitherOfFive e1 = Either5.First(1);

        await Assert.That(e1.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v)).IsEqualTo("first 1");

        e1.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v));

        IEitherOfFive e2 = Either5.Second(2);

        await Assert.That(e2.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v)).IsEqualTo("second 2");

        e2.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v));

        IEitherOfFive e3 = Either5.Third(3);

        await Assert.That(e3.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v)).IsEqualTo("third 3");

        e3.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v));

        IEitherOfFive e4 = Either5.Fourth(4);

        await Assert.That(e4.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v)).IsEqualTo("fourth 4");

        e4.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v));

        IEitherOfFive e5 = Either5.Fifth(5);

        await Assert.That(e5.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v)).IsEqualTo("fifth 5");

        e5.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5"],
                ordering: CollectionOrdering.Matching);

        IEitherOfFive d = default(Either5);

        await Assert.That(() => d.Match(
                onFirst: static _ => string.Empty,
                onSecond: static _ => string.Empty,
                onThird: static _ => string.Empty,
                onFourth: static _ => string.Empty,
                onFifth: static _ => string.Empty)).ThrowsExactly<InvalidOperationException>();

        await Assert.That(() => d.MatchVoid(
                onFirst: static _ => { },
                onSecond: static _ => { },
                onThird: static _ => { },
                onFourth: static _ => { },
                onFifth: static _ => { })).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MatchVoidRunsOnlyTheHeldCase6()
    {
        List<string> ran = [];

        Either6.First(1).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v));

        Either6.Second(2).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v));

        Either6.Third(3).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v));

        Either6.Fourth(4).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v));

        Either6.Fifth(5).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v));

        Either6.Sixth(6).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5", "sixth 6"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(static () =>
                default(Either6).MatchVoid(
                    onFirst: static _ => { },
                    onSecond: static _ => { },
                    onThird: static _ => { },
                    onFourth: static _ => { },
                    onFifth: static _ => { },
                    onSixth: static _ => { }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MapsTransformOnlyTheHeldCase6()
    {
        int calls = 0;

        Func<int, int> ten = v =>
        {
            calls++;
            return v * 10;
        };

        await Assert.That(Either6.First(1).MapFirst(ten)).IsEqualTo(Either6.First(10));
        await Assert.That(Either6.First(1).MapSecond(ten)).IsEqualTo(Either6.First(1));
        await Assert.That(Either6.First(1).MapThird(ten)).IsEqualTo(Either6.First(1));
        await Assert.That(Either6.First(1).MapFourth(ten)).IsEqualTo(Either6.First(1));
        await Assert.That(Either6.First(1).MapFifth(ten)).IsEqualTo(Either6.First(1));
        await Assert.That(Either6.First(1).MapSixth(ten)).IsEqualTo(Either6.First(1));

        await Assert.That(Either6.Second(2).MapFirst(ten)).IsEqualTo(Either6.Second(2));
        await Assert.That(Either6.Second(2).MapSecond(ten)).IsEqualTo(Either6.Second(20));
        await Assert.That(Either6.Second(2).MapThird(ten)).IsEqualTo(Either6.Second(2));
        await Assert.That(Either6.Second(2).MapFourth(ten)).IsEqualTo(Either6.Second(2));
        await Assert.That(Either6.Second(2).MapFifth(ten)).IsEqualTo(Either6.Second(2));
        await Assert.That(Either6.Second(2).MapSixth(ten)).IsEqualTo(Either6.Second(2));

        await Assert.That(Either6.Third(3).MapFirst(ten)).IsEqualTo(Either6.Third(3));
        await Assert.That(Either6.Third(3).MapSecond(ten)).IsEqualTo(Either6.Third(3));
        await Assert.That(Either6.Third(3).MapThird(ten)).IsEqualTo(Either6.Third(30));
        await Assert.That(Either6.Third(3).MapFourth(ten)).IsEqualTo(Either6.Third(3));
        await Assert.That(Either6.Third(3).MapFifth(ten)).IsEqualTo(Either6.Third(3));
        await Assert.That(Either6.Third(3).MapSixth(ten)).IsEqualTo(Either6.Third(3));

        await Assert.That(Either6.Fourth(4).MapFirst(ten)).IsEqualTo(Either6.Fourth(4));
        await Assert.That(Either6.Fourth(4).MapSecond(ten)).IsEqualTo(Either6.Fourth(4));
        await Assert.That(Either6.Fourth(4).MapThird(ten)).IsEqualTo(Either6.Fourth(4));
        await Assert.That(Either6.Fourth(4).MapFourth(ten)).IsEqualTo(Either6.Fourth(40));
        await Assert.That(Either6.Fourth(4).MapFifth(ten)).IsEqualTo(Either6.Fourth(4));
        await Assert.That(Either6.Fourth(4).MapSixth(ten)).IsEqualTo(Either6.Fourth(4));

        await Assert.That(Either6.Fifth(5).MapFirst(ten)).IsEqualTo(Either6.Fifth(5));
        await Assert.That(Either6.Fifth(5).MapSecond(ten)).IsEqualTo(Either6.Fifth(5));
        await Assert.That(Either6.Fifth(5).MapThird(ten)).IsEqualTo(Either6.Fifth(5));
        await Assert.That(Either6.Fifth(5).MapFourth(ten)).IsEqualTo(Either6.Fifth(5));
        await Assert.That(Either6.Fifth(5).MapFifth(ten)).IsEqualTo(Either6.Fifth(50));
        await Assert.That(Either6.Fifth(5).MapSixth(ten)).IsEqualTo(Either6.Fifth(5));

        await Assert.That(Either6.Sixth(6).MapFirst(ten)).IsEqualTo(Either6.Sixth(6));
        await Assert.That(Either6.Sixth(6).MapSecond(ten)).IsEqualTo(Either6.Sixth(6));
        await Assert.That(Either6.Sixth(6).MapThird(ten)).IsEqualTo(Either6.Sixth(6));
        await Assert.That(Either6.Sixth(6).MapFourth(ten)).IsEqualTo(Either6.Sixth(6));
        await Assert.That(Either6.Sixth(6).MapFifth(ten)).IsEqualTo(Either6.Sixth(6));
        await Assert.That(Either6.Sixth(6).MapSixth(ten)).IsEqualTo(Either6.Sixth(60));
        await Assert.That(calls).IsEqualTo(6);

        await Assert.That(static () => default(Either6).MapFirst(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either6).MapSecond(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either6).MapThird(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either6).MapFourth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either6).MapFifth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either6).MapSixth(static v => v))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UntypedMembersReadTheHeldCase6()
    {
        List<string> ran = [];

        IEitherOfSix e1 = Either6.First(1);

        await Assert.That(e1.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v)).IsEqualTo("first 1");

        e1.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v));

        IEitherOfSix e2 = Either6.Second(2);

        await Assert.That(e2.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v)).IsEqualTo("second 2");

        e2.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v));

        IEitherOfSix e3 = Either6.Third(3);

        await Assert.That(e3.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v)).IsEqualTo("third 3");

        e3.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v));

        IEitherOfSix e4 = Either6.Fourth(4);

        await Assert.That(e4.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v)).IsEqualTo("fourth 4");

        e4.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v));

        IEitherOfSix e5 = Either6.Fifth(5);

        await Assert.That(e5.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v)).IsEqualTo("fifth 5");

        e5.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v));

        IEitherOfSix e6 = Either6.Sixth(6);

        await Assert.That(e6.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v)).IsEqualTo("sixth 6");

        e6.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5", "sixth 6"],
                ordering: CollectionOrdering.Matching);

        IEitherOfSix d = default(Either6);

        await Assert.That(() => d.Match(
                onFirst: static _ => string.Empty,
                onSecond: static _ => string.Empty,
                onThird: static _ => string.Empty,
                onFourth: static _ => string.Empty,
                onFifth: static _ => string.Empty,
                onSixth: static _ => string.Empty)).ThrowsExactly<InvalidOperationException>();

        await Assert.That(() => d.MatchVoid(
                onFirst: static _ => { },
                onSecond: static _ => { },
                onThird: static _ => { },
                onFourth: static _ => { },
                onFifth: static _ => { },
                onSixth: static _ => { })).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MatchVoidRunsOnlyTheHeldCase7()
    {
        List<string> ran = [];

        Either7.First(1).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v));

        Either7.Second(2).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v));

        Either7.Third(3).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v));

        Either7.Fourth(4).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v));

        Either7.Fifth(5).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v));

        Either7.Sixth(6).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v));

        Either7.Seventh(7).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5", "sixth 6", "seventh 7"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(static () =>
                default(Either7).MatchVoid(
                    onFirst: static _ => { },
                    onSecond: static _ => { },
                    onThird: static _ => { },
                    onFourth: static _ => { },
                    onFifth: static _ => { },
                    onSixth: static _ => { },
                    onSeventh: static _ => { }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MapsTransformOnlyTheHeldCase7()
    {
        int calls = 0;

        Func<int, int> ten = v =>
        {
            calls++;
            return v * 10;
        };

        await Assert.That(Either7.First(1).MapFirst(ten)).IsEqualTo(Either7.First(10));
        await Assert.That(Either7.First(1).MapSecond(ten)).IsEqualTo(Either7.First(1));
        await Assert.That(Either7.First(1).MapThird(ten)).IsEqualTo(Either7.First(1));
        await Assert.That(Either7.First(1).MapFourth(ten)).IsEqualTo(Either7.First(1));
        await Assert.That(Either7.First(1).MapFifth(ten)).IsEqualTo(Either7.First(1));
        await Assert.That(Either7.First(1).MapSixth(ten)).IsEqualTo(Either7.First(1));
        await Assert.That(Either7.First(1).MapSeventh(ten)).IsEqualTo(Either7.First(1));

        await Assert.That(Either7.Second(2).MapFirst(ten)).IsEqualTo(Either7.Second(2));
        await Assert.That(Either7.Second(2).MapSecond(ten)).IsEqualTo(Either7.Second(20));
        await Assert.That(Either7.Second(2).MapThird(ten)).IsEqualTo(Either7.Second(2));
        await Assert.That(Either7.Second(2).MapFourth(ten)).IsEqualTo(Either7.Second(2));
        await Assert.That(Either7.Second(2).MapFifth(ten)).IsEqualTo(Either7.Second(2));
        await Assert.That(Either7.Second(2).MapSixth(ten)).IsEqualTo(Either7.Second(2));
        await Assert.That(Either7.Second(2).MapSeventh(ten)).IsEqualTo(Either7.Second(2));

        await Assert.That(Either7.Third(3).MapFirst(ten)).IsEqualTo(Either7.Third(3));
        await Assert.That(Either7.Third(3).MapSecond(ten)).IsEqualTo(Either7.Third(3));
        await Assert.That(Either7.Third(3).MapThird(ten)).IsEqualTo(Either7.Third(30));
        await Assert.That(Either7.Third(3).MapFourth(ten)).IsEqualTo(Either7.Third(3));
        await Assert.That(Either7.Third(3).MapFifth(ten)).IsEqualTo(Either7.Third(3));
        await Assert.That(Either7.Third(3).MapSixth(ten)).IsEqualTo(Either7.Third(3));
        await Assert.That(Either7.Third(3).MapSeventh(ten)).IsEqualTo(Either7.Third(3));

        await Assert.That(Either7.Fourth(4).MapFirst(ten)).IsEqualTo(Either7.Fourth(4));
        await Assert.That(Either7.Fourth(4).MapSecond(ten)).IsEqualTo(Either7.Fourth(4));
        await Assert.That(Either7.Fourth(4).MapThird(ten)).IsEqualTo(Either7.Fourth(4));
        await Assert.That(Either7.Fourth(4).MapFourth(ten)).IsEqualTo(Either7.Fourth(40));
        await Assert.That(Either7.Fourth(4).MapFifth(ten)).IsEqualTo(Either7.Fourth(4));
        await Assert.That(Either7.Fourth(4).MapSixth(ten)).IsEqualTo(Either7.Fourth(4));
        await Assert.That(Either7.Fourth(4).MapSeventh(ten)).IsEqualTo(Either7.Fourth(4));

        await Assert.That(Either7.Fifth(5).MapFirst(ten)).IsEqualTo(Either7.Fifth(5));
        await Assert.That(Either7.Fifth(5).MapSecond(ten)).IsEqualTo(Either7.Fifth(5));
        await Assert.That(Either7.Fifth(5).MapThird(ten)).IsEqualTo(Either7.Fifth(5));
        await Assert.That(Either7.Fifth(5).MapFourth(ten)).IsEqualTo(Either7.Fifth(5));
        await Assert.That(Either7.Fifth(5).MapFifth(ten)).IsEqualTo(Either7.Fifth(50));
        await Assert.That(Either7.Fifth(5).MapSixth(ten)).IsEqualTo(Either7.Fifth(5));
        await Assert.That(Either7.Fifth(5).MapSeventh(ten)).IsEqualTo(Either7.Fifth(5));

        await Assert.That(Either7.Sixth(6).MapFirst(ten)).IsEqualTo(Either7.Sixth(6));
        await Assert.That(Either7.Sixth(6).MapSecond(ten)).IsEqualTo(Either7.Sixth(6));
        await Assert.That(Either7.Sixth(6).MapThird(ten)).IsEqualTo(Either7.Sixth(6));
        await Assert.That(Either7.Sixth(6).MapFourth(ten)).IsEqualTo(Either7.Sixth(6));
        await Assert.That(Either7.Sixth(6).MapFifth(ten)).IsEqualTo(Either7.Sixth(6));
        await Assert.That(Either7.Sixth(6).MapSixth(ten)).IsEqualTo(Either7.Sixth(60));
        await Assert.That(Either7.Sixth(6).MapSeventh(ten)).IsEqualTo(Either7.Sixth(6));

        await Assert.That(Either7.Seventh(7).MapFirst(ten)).IsEqualTo(Either7.Seventh(7));
        await Assert.That(Either7.Seventh(7).MapSecond(ten)).IsEqualTo(Either7.Seventh(7));
        await Assert.That(Either7.Seventh(7).MapThird(ten)).IsEqualTo(Either7.Seventh(7));
        await Assert.That(Either7.Seventh(7).MapFourth(ten)).IsEqualTo(Either7.Seventh(7));
        await Assert.That(Either7.Seventh(7).MapFifth(ten)).IsEqualTo(Either7.Seventh(7));
        await Assert.That(Either7.Seventh(7).MapSixth(ten)).IsEqualTo(Either7.Seventh(7));
        await Assert.That(Either7.Seventh(7).MapSeventh(ten)).IsEqualTo(Either7.Seventh(70));
        await Assert.That(calls).IsEqualTo(7);

        await Assert.That(static () => default(Either7).MapFirst(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either7).MapSecond(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either7).MapThird(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either7).MapFourth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either7).MapFifth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either7).MapSixth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either7).MapSeventh(static v => v))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UntypedMembersReadTheHeldCase7()
    {
        List<string> ran = [];

        IEitherOfSeven e1 = Either7.First(1);

        await Assert.That(e1.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v)).IsEqualTo("first 1");

        e1.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v));

        IEitherOfSeven e2 = Either7.Second(2);

        await Assert.That(e2.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v)).IsEqualTo("second 2");

        e2.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v));

        IEitherOfSeven e3 = Either7.Third(3);

        await Assert.That(e3.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v)).IsEqualTo("third 3");

        e3.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v));

        IEitherOfSeven e4 = Either7.Fourth(4);

        await Assert.That(e4.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v)).IsEqualTo("fourth 4");

        e4.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v));

        IEitherOfSeven e5 = Either7.Fifth(5);

        await Assert.That(e5.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v)).IsEqualTo("fifth 5");

        e5.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v));

        IEitherOfSeven e6 = Either7.Sixth(6);

        await Assert.That(e6.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v)).IsEqualTo("sixth 6");

        e6.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v));

        IEitherOfSeven e7 = Either7.Seventh(7);

        await Assert.That(e7.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v)).IsEqualTo("seventh 7");

        e7.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5", "sixth 6", "seventh 7"],
                ordering: CollectionOrdering.Matching);

        IEitherOfSeven d = default(Either7);

        await Assert.That(() => d.Match(
                onFirst: static _ => string.Empty,
                onSecond: static _ => string.Empty,
                onThird: static _ => string.Empty,
                onFourth: static _ => string.Empty,
                onFifth: static _ => string.Empty,
                onSixth: static _ => string.Empty,
                onSeventh: static _ => string.Empty)).ThrowsExactly<InvalidOperationException>();

        await Assert.That(() => d.MatchVoid(
                onFirst: static _ => { },
                onSecond: static _ => { },
                onThird: static _ => { },
                onFourth: static _ => { },
                onFifth: static _ => { },
                onSixth: static _ => { },
                onSeventh: static _ => { })).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MatchVoidRunsOnlyTheHeldCase8()
    {
        List<string> ran = [];

        Either8.First(1).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        Either8.Second(2).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        Either8.Third(3).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        Either8.Fourth(4).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        Either8.Fifth(5).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        Either8.Sixth(6).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        Either8.Seventh(7).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        Either8.Eighth(8).MatchVoid(
            onFirst: v => ran.Add("first " + v),
            onSecond: v => ran.Add("second " + v),
            onThird: v => ran.Add("third " + v),
            onFourth: v => ran.Add("fourth " + v),
            onFifth: v => ran.Add("fifth " + v),
            onSixth: v => ran.Add("sixth " + v),
            onSeventh: v => ran.Add("seventh " + v),
            onEighth: v => ran.Add("eighth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5", "sixth 6", "seventh 7", "eighth 8"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(static () =>
                default(Either8).MatchVoid(
                    onFirst: static _ => { },
                    onSecond: static _ => { },
                    onThird: static _ => { },
                    onFourth: static _ => { },
                    onFifth: static _ => { },
                    onSixth: static _ => { },
                    onSeventh: static _ => { },
                    onEighth: static _ => { }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MapsTransformOnlyTheHeldCase8()
    {
        int calls = 0;

        Func<int, int> ten = v =>
        {
            calls++;
            return v * 10;
        };

        await Assert.That(Either8.First(1).MapFirst(ten)).IsEqualTo(Either8.First(10));
        await Assert.That(Either8.First(1).MapSecond(ten)).IsEqualTo(Either8.First(1));
        await Assert.That(Either8.First(1).MapThird(ten)).IsEqualTo(Either8.First(1));
        await Assert.That(Either8.First(1).MapFourth(ten)).IsEqualTo(Either8.First(1));
        await Assert.That(Either8.First(1).MapFifth(ten)).IsEqualTo(Either8.First(1));
        await Assert.That(Either8.First(1).MapSixth(ten)).IsEqualTo(Either8.First(1));
        await Assert.That(Either8.First(1).MapSeventh(ten)).IsEqualTo(Either8.First(1));
        await Assert.That(Either8.First(1).MapEighth(ten)).IsEqualTo(Either8.First(1));

        await Assert.That(Either8.Second(2).MapFirst(ten)).IsEqualTo(Either8.Second(2));
        await Assert.That(Either8.Second(2).MapSecond(ten)).IsEqualTo(Either8.Second(20));
        await Assert.That(Either8.Second(2).MapThird(ten)).IsEqualTo(Either8.Second(2));
        await Assert.That(Either8.Second(2).MapFourth(ten)).IsEqualTo(Either8.Second(2));
        await Assert.That(Either8.Second(2).MapFifth(ten)).IsEqualTo(Either8.Second(2));
        await Assert.That(Either8.Second(2).MapSixth(ten)).IsEqualTo(Either8.Second(2));
        await Assert.That(Either8.Second(2).MapSeventh(ten)).IsEqualTo(Either8.Second(2));
        await Assert.That(Either8.Second(2).MapEighth(ten)).IsEqualTo(Either8.Second(2));

        await Assert.That(Either8.Third(3).MapFirst(ten)).IsEqualTo(Either8.Third(3));
        await Assert.That(Either8.Third(3).MapSecond(ten)).IsEqualTo(Either8.Third(3));
        await Assert.That(Either8.Third(3).MapThird(ten)).IsEqualTo(Either8.Third(30));
        await Assert.That(Either8.Third(3).MapFourth(ten)).IsEqualTo(Either8.Third(3));
        await Assert.That(Either8.Third(3).MapFifth(ten)).IsEqualTo(Either8.Third(3));
        await Assert.That(Either8.Third(3).MapSixth(ten)).IsEqualTo(Either8.Third(3));
        await Assert.That(Either8.Third(3).MapSeventh(ten)).IsEqualTo(Either8.Third(3));
        await Assert.That(Either8.Third(3).MapEighth(ten)).IsEqualTo(Either8.Third(3));

        await Assert.That(Either8.Fourth(4).MapFirst(ten)).IsEqualTo(Either8.Fourth(4));
        await Assert.That(Either8.Fourth(4).MapSecond(ten)).IsEqualTo(Either8.Fourth(4));
        await Assert.That(Either8.Fourth(4).MapThird(ten)).IsEqualTo(Either8.Fourth(4));
        await Assert.That(Either8.Fourth(4).MapFourth(ten)).IsEqualTo(Either8.Fourth(40));
        await Assert.That(Either8.Fourth(4).MapFifth(ten)).IsEqualTo(Either8.Fourth(4));
        await Assert.That(Either8.Fourth(4).MapSixth(ten)).IsEqualTo(Either8.Fourth(4));
        await Assert.That(Either8.Fourth(4).MapSeventh(ten)).IsEqualTo(Either8.Fourth(4));
        await Assert.That(Either8.Fourth(4).MapEighth(ten)).IsEqualTo(Either8.Fourth(4));

        await Assert.That(Either8.Fifth(5).MapFirst(ten)).IsEqualTo(Either8.Fifth(5));
        await Assert.That(Either8.Fifth(5).MapSecond(ten)).IsEqualTo(Either8.Fifth(5));
        await Assert.That(Either8.Fifth(5).MapThird(ten)).IsEqualTo(Either8.Fifth(5));
        await Assert.That(Either8.Fifth(5).MapFourth(ten)).IsEqualTo(Either8.Fifth(5));
        await Assert.That(Either8.Fifth(5).MapFifth(ten)).IsEqualTo(Either8.Fifth(50));
        await Assert.That(Either8.Fifth(5).MapSixth(ten)).IsEqualTo(Either8.Fifth(5));
        await Assert.That(Either8.Fifth(5).MapSeventh(ten)).IsEqualTo(Either8.Fifth(5));
        await Assert.That(Either8.Fifth(5).MapEighth(ten)).IsEqualTo(Either8.Fifth(5));

        await Assert.That(Either8.Sixth(6).MapFirst(ten)).IsEqualTo(Either8.Sixth(6));
        await Assert.That(Either8.Sixth(6).MapSecond(ten)).IsEqualTo(Either8.Sixth(6));
        await Assert.That(Either8.Sixth(6).MapThird(ten)).IsEqualTo(Either8.Sixth(6));
        await Assert.That(Either8.Sixth(6).MapFourth(ten)).IsEqualTo(Either8.Sixth(6));
        await Assert.That(Either8.Sixth(6).MapFifth(ten)).IsEqualTo(Either8.Sixth(6));
        await Assert.That(Either8.Sixth(6).MapSixth(ten)).IsEqualTo(Either8.Sixth(60));
        await Assert.That(Either8.Sixth(6).MapSeventh(ten)).IsEqualTo(Either8.Sixth(6));
        await Assert.That(Either8.Sixth(6).MapEighth(ten)).IsEqualTo(Either8.Sixth(6));

        await Assert.That(Either8.Seventh(7).MapFirst(ten)).IsEqualTo(Either8.Seventh(7));
        await Assert.That(Either8.Seventh(7).MapSecond(ten)).IsEqualTo(Either8.Seventh(7));
        await Assert.That(Either8.Seventh(7).MapThird(ten)).IsEqualTo(Either8.Seventh(7));
        await Assert.That(Either8.Seventh(7).MapFourth(ten)).IsEqualTo(Either8.Seventh(7));
        await Assert.That(Either8.Seventh(7).MapFifth(ten)).IsEqualTo(Either8.Seventh(7));
        await Assert.That(Either8.Seventh(7).MapSixth(ten)).IsEqualTo(Either8.Seventh(7));
        await Assert.That(Either8.Seventh(7).MapSeventh(ten)).IsEqualTo(Either8.Seventh(70));
        await Assert.That(Either8.Seventh(7).MapEighth(ten)).IsEqualTo(Either8.Seventh(7));

        await Assert.That(Either8.Eighth(8).MapFirst(ten)).IsEqualTo(Either8.Eighth(8));
        await Assert.That(Either8.Eighth(8).MapSecond(ten)).IsEqualTo(Either8.Eighth(8));
        await Assert.That(Either8.Eighth(8).MapThird(ten)).IsEqualTo(Either8.Eighth(8));
        await Assert.That(Either8.Eighth(8).MapFourth(ten)).IsEqualTo(Either8.Eighth(8));
        await Assert.That(Either8.Eighth(8).MapFifth(ten)).IsEqualTo(Either8.Eighth(8));
        await Assert.That(Either8.Eighth(8).MapSixth(ten)).IsEqualTo(Either8.Eighth(8));
        await Assert.That(Either8.Eighth(8).MapSeventh(ten)).IsEqualTo(Either8.Eighth(8));
        await Assert.That(Either8.Eighth(8).MapEighth(ten)).IsEqualTo(Either8.Eighth(80));
        await Assert.That(calls).IsEqualTo(8);

        await Assert.That(static () => default(Either8).MapFirst(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either8).MapSecond(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either8).MapThird(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either8).MapFourth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either8).MapFifth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either8).MapSixth(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either8).MapSeventh(static v => v))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(static () => default(Either8).MapEighth(static v => v))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UntypedMembersReadTheHeldCase8()
    {
        List<string> ran = [];

        IEitherOfEight e1 = Either8.First(1);

        await Assert.That(e1.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("first 1");

        e1.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        IEitherOfEight e2 = Either8.Second(2);

        await Assert.That(e2.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("second 2");

        e2.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        IEitherOfEight e3 = Either8.Third(3);

        await Assert.That(e3.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("third 3");

        e3.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        IEitherOfEight e4 = Either8.Fourth(4);

        await Assert.That(e4.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("fourth 4");

        e4.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        IEitherOfEight e5 = Either8.Fifth(5);

        await Assert.That(e5.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("fifth 5");

        e5.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        IEitherOfEight e6 = Either8.Sixth(6);

        await Assert.That(e6.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("sixth 6");

        e6.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        IEitherOfEight e7 = Either8.Seventh(7);

        await Assert.That(e7.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("seventh 7");

        e7.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        IEitherOfEight e8 = Either8.Eighth(8);

        await Assert.That(e8.Match(
                onFirst: static v => "first " + v,
                onSecond: static v => "second " + v,
                onThird: static v => "third " + v,
                onFourth: static v => "fourth " + v,
                onFifth: static v => "fifth " + v,
                onSixth: static v => "sixth " + v,
                onSeventh: static v => "seventh " + v,
                onEighth: static v => "eighth " + v)).IsEqualTo("eighth 8");

        e8.MatchVoid(
                onFirst: v => ran.Add("first " + v),
                onSecond: v => ran.Add("second " + v),
                onThird: v => ran.Add("third " + v),
                onFourth: v => ran.Add("fourth " + v),
                onFifth: v => ran.Add("fifth " + v),
                onSixth: v => ran.Add("sixth " + v),
                onSeventh: v => ran.Add("seventh " + v),
                onEighth: v => ran.Add("eighth " + v));

        await Assert.That(ran)
            .IsEquivalentTo(
                expected: ["first 1", "second 2", "third 3", "fourth 4", "fifth 5", "sixth 6", "seventh 7", "eighth 8"],
                ordering: CollectionOrdering.Matching);

        IEitherOfEight d = default(Either8);

        await Assert.That(() => d.Match(
                onFirst: static _ => string.Empty,
                onSecond: static _ => string.Empty,
                onThird: static _ => string.Empty,
                onFourth: static _ => string.Empty,
                onFifth: static _ => string.Empty,
                onSixth: static _ => string.Empty,
                onSeventh: static _ => string.Empty,
                onEighth: static _ => string.Empty)).ThrowsExactly<InvalidOperationException>();

        await Assert.That(() => d.MatchVoid(
                onFirst: static _ => { },
                onSecond: static _ => { },
                onThird: static _ => { },
                onFourth: static _ => { },
                onFifth: static _ => { },
                onSixth: static _ => { },
                onSeventh: static _ => { },
                onEighth: static _ => { })).ThrowsExactly<InvalidOperationException>();
    }

    private sealed class Test1;

    private sealed class Test2;

    private sealed class Test3;

    private sealed class Test4;

    private sealed class Test5;

    private sealed class Test6;

    private sealed class Test7;

    private sealed class Test8;
}
