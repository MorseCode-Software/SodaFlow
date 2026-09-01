using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class EitherTests
    {
        [Test]
        public void DefaultValue2Test()
        {
            Either<Test1, Test2> e = default;

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt2(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultValue3Test()
        {
            Either<Test1, Test2, Test3> e = default;

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt3(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultValue4Test()
        {
            Either<Test1, Test2, Test3, Test4> e = default;

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt4(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultValue5Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5> e = default;

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt5(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultValue6Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6> e = default;

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt6(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultValue7Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e = default;

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt7(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultValue8Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e = default;

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt8(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultConstructor2Test()
        {
            Either<Test1, Test2> e = new Either<Test1, Test2>();

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt2(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultConstructor3Test()
        {
            Either<Test1, Test2, Test3> e = new Either<Test1, Test2, Test3>();

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt3(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultConstructor4Test()
        {
            Either<Test1, Test2, Test3, Test4> e = new Either<Test1, Test2, Test3, Test4>();

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt4(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultConstructor5Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5> e = new Either<Test1, Test2, Test3, Test4, Test5>();

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt5(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultConstructor6Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6> e = new Either<Test1, Test2, Test3, Test4, Test5, Test6>();

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt6(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultConstructor7Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e =
                new Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7>();

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt7(e));
            Assert.IsNull(o);
        }

        [Test]
        public void DefaultConstructor8Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e =
                new Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8>();

            object o = e.Upcast<IEither>().GetValueAsObject();

            Assert.AreEqual(expected: 1, actual: TestIt8(e));
            Assert.IsNull(o);
        }

        [Test]
        public void Either2Test()
        {
            Either<Test1, Test2> v1 = Either.First(new Test1());
            Either<Test1, Test2> v2 = Either.Second(new Test2());

            Assert.AreEqual(expected: 1, actual: TestIt2(v1));
            Assert.AreEqual(expected: 2, actual: TestIt2(v2));

            Assert.AreEqual(expected: 1, actual: TestIt2Action(v1));
            Assert.AreEqual(expected: 2, actual: TestIt2Action(v2));
        }

        [Test]
        public void Either3Test()
        {
            Either<Test1, Test2, Test3> v1 = Either.First(new Test1());
            Either<Test1, Test2, Test3> v2 = Either.Second(new Test2());
            Either<Test1, Test2, Test3> v3 = Either.Third(new Test3());

            Assert.AreEqual(expected: 1, actual: TestIt3(v1));
            Assert.AreEqual(expected: 2, actual: TestIt3(v2));
            Assert.AreEqual(expected: 3, actual: TestIt3(v3));

            Assert.AreEqual(expected: 1, actual: TestIt3Action(v1));
            Assert.AreEqual(expected: 2, actual: TestIt3Action(v2));
            Assert.AreEqual(expected: 3, actual: TestIt3Action(v3));
        }

        [Test]
        public void Either4Test()
        {
            Either<Test1, Test2, Test3, Test4> v1 = Either.First(new Test1());
            Either<Test1, Test2, Test3, Test4> v2 = Either.Second(new Test2());
            Either<Test1, Test2, Test3, Test4> v3 = Either.Third(new Test3());
            Either<Test1, Test2, Test3, Test4> v4 = Either.Fourth(new Test4());

            Assert.AreEqual(expected: 1, actual: TestIt4(v1));
            Assert.AreEqual(expected: 2, actual: TestIt4(v2));
            Assert.AreEqual(expected: 3, actual: TestIt4(v3));
            Assert.AreEqual(expected: 4, actual: TestIt4(v4));

            Assert.AreEqual(expected: 1, actual: TestIt4Action(v1));
            Assert.AreEqual(expected: 2, actual: TestIt4Action(v2));
            Assert.AreEqual(expected: 3, actual: TestIt4Action(v3));
            Assert.AreEqual(expected: 4, actual: TestIt4Action(v4));
        }

        [Test]
        public void Either5Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5> v1 = Either.First(new Test1());
            Either<Test1, Test2, Test3, Test4, Test5> v2 = Either.Second(new Test2());
            Either<Test1, Test2, Test3, Test4, Test5> v3 = Either.Third(new Test3());
            Either<Test1, Test2, Test3, Test4, Test5> v4 = Either.Fourth(new Test4());
            Either<Test1, Test2, Test3, Test4, Test5> v5 = Either.Fifth(new Test5());

            Assert.AreEqual(expected: 1, actual: TestIt5(v1));
            Assert.AreEqual(expected: 2, actual: TestIt5(v2));
            Assert.AreEqual(expected: 3, actual: TestIt5(v3));
            Assert.AreEqual(expected: 4, actual: TestIt5(v4));
            Assert.AreEqual(expected: 5, actual: TestIt5(v5));

            Assert.AreEqual(expected: 1, actual: TestIt5Action(v1));
            Assert.AreEqual(expected: 2, actual: TestIt5Action(v2));
            Assert.AreEqual(expected: 3, actual: TestIt5Action(v3));
            Assert.AreEqual(expected: 4, actual: TestIt5Action(v4));
            Assert.AreEqual(expected: 5, actual: TestIt5Action(v5));
        }

        [Test]
        public void Either6Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6> v1 = Either.First(new Test1());
            Either<Test1, Test2, Test3, Test4, Test5, Test6> v2 = Either.Second(new Test2());
            Either<Test1, Test2, Test3, Test4, Test5, Test6> v3 = Either.Third(new Test3());
            Either<Test1, Test2, Test3, Test4, Test5, Test6> v4 = Either.Fourth(new Test4());
            Either<Test1, Test2, Test3, Test4, Test5, Test6> v5 = Either.Fifth(new Test5());
            Either<Test1, Test2, Test3, Test4, Test5, Test6> v6 = Either.Sixth(new Test6());

            Assert.AreEqual(expected: 1, actual: TestIt6(v1));
            Assert.AreEqual(expected: 2, actual: TestIt6(v2));
            Assert.AreEqual(expected: 3, actual: TestIt6(v3));
            Assert.AreEqual(expected: 4, actual: TestIt6(v4));
            Assert.AreEqual(expected: 5, actual: TestIt6(v5));
            Assert.AreEqual(expected: 6, actual: TestIt6(v6));

            Assert.AreEqual(expected: 1, actual: TestIt6Action(v1));
            Assert.AreEqual(expected: 2, actual: TestIt6Action(v2));
            Assert.AreEqual(expected: 3, actual: TestIt6Action(v3));
            Assert.AreEqual(expected: 4, actual: TestIt6Action(v4));
            Assert.AreEqual(expected: 5, actual: TestIt6Action(v5));
            Assert.AreEqual(expected: 6, actual: TestIt6Action(v6));
        }

        [Test]
        public void Either7Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v1 = Either.First(new Test1());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v2 = Either.Second(new Test2());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v3 = Either.Third(new Test3());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v4 = Either.Fourth(new Test4());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v5 = Either.Fifth(new Test5());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v6 = Either.Sixth(new Test6());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> v7 = Either.Seventh(new Test7());

            Assert.AreEqual(expected: 1, actual: TestIt7(v1));
            Assert.AreEqual(expected: 2, actual: TestIt7(v2));
            Assert.AreEqual(expected: 3, actual: TestIt7(v3));
            Assert.AreEqual(expected: 4, actual: TestIt7(v4));
            Assert.AreEqual(expected: 5, actual: TestIt7(v5));
            Assert.AreEqual(expected: 6, actual: TestIt7(v6));
            Assert.AreEqual(expected: 7, actual: TestIt7(v7));

            Assert.AreEqual(expected: 1, actual: TestIt7Action(v1));
            Assert.AreEqual(expected: 2, actual: TestIt7Action(v2));
            Assert.AreEqual(expected: 3, actual: TestIt7Action(v3));
            Assert.AreEqual(expected: 4, actual: TestIt7Action(v4));
            Assert.AreEqual(expected: 5, actual: TestIt7Action(v5));
            Assert.AreEqual(expected: 6, actual: TestIt7Action(v6));
            Assert.AreEqual(expected: 7, actual: TestIt7Action(v7));
        }

        [Test]
        public void Either8Test()
        {
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v1 = Either.First(new Test1());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v2 = Either.Second(new Test2());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v3 = Either.Third(new Test3());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v4 = Either.Fourth(new Test4());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v5 = Either.Fifth(new Test5());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v6 = Either.Sixth(new Test6());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v7 = Either.Seventh(new Test7());
            Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> v8 = Either.Eighth(new Test8());

            Assert.AreEqual(expected: 1, actual: TestIt8(v1));
            Assert.AreEqual(expected: 2, actual: TestIt8(v2));
            Assert.AreEqual(expected: 3, actual: TestIt8(v3));
            Assert.AreEqual(expected: 4, actual: TestIt8(v4));
            Assert.AreEqual(expected: 5, actual: TestIt8(v5));
            Assert.AreEqual(expected: 6, actual: TestIt8(v6));
            Assert.AreEqual(expected: 7, actual: TestIt8(v7));
            Assert.AreEqual(expected: 8, actual: TestIt8(v8));

            Assert.AreEqual(expected: 1, actual: TestIt8Action(v1));
            Assert.AreEqual(expected: 2, actual: TestIt8Action(v2));
            Assert.AreEqual(expected: 3, actual: TestIt8Action(v3));
            Assert.AreEqual(expected: 4, actual: TestIt8Action(v4));
            Assert.AreEqual(expected: 5, actual: TestIt8Action(v5));
            Assert.AreEqual(expected: 6, actual: TestIt8Action(v6));
            Assert.AreEqual(expected: 7, actual: TestIt8Action(v7));
            Assert.AreEqual(expected: 8, actual: TestIt8Action(v8));
        }

        [Test]
        public void EqualityTest()
        {
            Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
            Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(2L);

            Assert.AreEqual(expected: e1, actual: e2);
        }

        [Test]
        public void NonEqualityTest1()
        {
            Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
            Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.First(2);

            Assert.AreNotEqual(expected: e1, actual: e2);
        }

        [Test]
        public void NonEqualityTest2()
        {
            Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
            Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(3L);

            Assert.AreNotEqual(expected: e1, actual: e2);
        }

        [Test]
        public void EqualityOperatorTest()
        {
            Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
            Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(2L);

            Assert.IsTrue(e1 == e2);
        }

        [Test]
        public void NonEqualityOperatorTest1()
        {
            Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
            Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.First(2);

            Assert.IsTrue(e1 != e2);
        }

        [Test]
        public void NonEqualityOperatorTest2()
        {
            Either<int, double, DateTime, string, char, float, long, byte> e1 = Either.Seventh(2L);
            Either<int, double, DateTime, string, char, float, long, byte> e2 = Either.Seventh(3L);

            Assert.IsTrue(e1 != e2);
        }

        private class Test1
        {
        }

        private class Test2
        {
        }

        private class Test3
        {
        }

        private class Test4
        {
        }

        private class Test5
        {
        }

        private class Test6
        {
        }

        private class Test7
        {
        }

        private class Test8
        {
        }

        [Test]
        public void SwapTest()
        {
            Either<int, string> first = Either.First(2);
            Either<int, string> second = Either.Second("a");

            Assert.AreEqual(expected: Either<string, int>.Second(2), actual: first.Swap());
            Assert.AreEqual(expected: Either<string, int>.First("a"), actual: second.Swap());
        }

        [Test]
        public void SwapTwiceIsIdentityTest()
        {
            Either<int, string> first = Either.First(2);
            Either<int, string> second = Either.Second("a");

            Assert.AreEqual(expected: first, actual: first.Swap().Swap());
            Assert.AreEqual(expected: second, actual: second.Swap().Swap());
        }

        [Test]
        public void SwapReachesTheOtherCaseTest()
        {
            // MapSecond only addresses the second case; swapping either side of it is how the
            // first case is reached, and the second swap puts the result back where it started.
            Either<int, string> e = Either.First(2);

            Assert.AreEqual(
                expected: Either<string, string>.First("2"),
                actual: e.Swap().MapSecond(v => v.ToString()).Swap());
        }

        private static int TestIt2(Either<Test1, Test2> e) => e.Match(onFirst: v1 => 1, onSecond: v2 => 2);

        private static int TestIt2Action(Either<Test1, Test2> e)
        {
            int n = 0;

            e.MatchVoid(
                onFirst: v1 =>
                {
                    n = 1;
                },
                onSecond: v2 =>
                {
                    n = 2;
                });

            return n;
        }

        private static int TestIt3(Either<Test1, Test2, Test3> e) =>
            e.Match(onFirst: v1 => 1, onSecond: v2 => 2, onThird: v3 => 3);

        private static int TestIt3Action(Either<Test1, Test2, Test3> e)
        {
            int n = 0;

            e.MatchVoid(
                onFirst: v1 =>
                {
                    n = 1;
                },
                onSecond: v2 =>
                {
                    n = 2;
                },
                onThird: v3 =>
                {
                    n = 3;
                });

            return n;
        }

        private static int TestIt4(Either<Test1, Test2, Test3, Test4> e) =>
            e.Match(onFirst: v1 => 1, onSecond: v2 => 2, onThird: v3 => 3, onFourth: v4 => 4);

        private static int TestIt4Action(Either<Test1, Test2, Test3, Test4> e)
        {
            int n = 0;

            e.MatchVoid(
                onFirst: v1 =>
                {
                    n = 1;
                },
                onSecond: v2 =>
                {
                    n = 2;
                },
                onThird: v3 =>
                {
                    n = 3;
                },
                onFourth: v4 =>
                {
                    n = 4;
                });

            return n;
        }

        private static int TestIt5(Either<Test1, Test2, Test3, Test4, Test5> e) =>
            e.Match(onFirst: v1 => 1, onSecond: v2 => 2, onThird: v3 => 3, onFourth: v4 => 4, onFifth: v5 => 5);

        private static int TestIt5Action(Either<Test1, Test2, Test3, Test4, Test5> e)
        {
            int n = 0;

            e.MatchVoid(
                onFirst: v1 =>
                {
                    n = 1;
                },
                onSecond: v2 =>
                {
                    n = 2;
                },
                onThird: v3 =>
                {
                    n = 3;
                },
                onFourth: v4 =>
                {
                    n = 4;
                },
                onFifth: v5 =>
                {
                    n = 5;
                });

            return n;
        }

        private static int TestIt6(Either<Test1, Test2, Test3, Test4, Test5, Test6> e) =>
            e.Match(
                onFirst: v1 => 1,
                onSecond: v2 => 2,
                onThird: v3 => 3,
                onFourth: v4 => 4,
                onFifth: v5 => 5,
                onSixth: v6 => 6);

        private static int TestIt6Action(Either<Test1, Test2, Test3, Test4, Test5, Test6> e)
        {
            int n = 0;

            e.MatchVoid(
                onFirst: v1 =>
                {
                    n = 1;
                },
                onSecond: v2 =>
                {
                    n = 2;
                },
                onThird: v3 =>
                {
                    n = 3;
                },
                onFourth: v4 =>
                {
                    n = 4;
                },
                onFifth: v5 =>
                {
                    n = 5;
                },
                onSixth: v6 =>
                {
                    n = 6;
                });

            return n;
        }

        private static int TestIt7(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e) =>
            e.Match(
                onFirst: v1 => 1,
                onSecond: v2 => 2,
                onThird: v3 => 3,
                onFourth: v4 => 4,
                onFifth: v5 => 5,
                onSixth: v6 => 6,
                onSeventh: v7 => 7);

        private static int TestIt7Action(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7> e)
        {
            int n = 0;

            e.MatchVoid(
                onFirst: v1 =>
                {
                    n = 1;
                },
                onSecond: v2 =>
                {
                    n = 2;
                },
                onThird: v3 =>
                {
                    n = 3;
                },
                onFourth: v4 =>
                {
                    n = 4;
                },
                onFifth: v5 =>
                {
                    n = 5;
                },
                onSixth: v6 =>
                {
                    n = 6;
                },
                onSeventh: v7 =>
                {
                    n = 7;
                });

            return n;
        }

        private static int TestIt8(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e) =>
            e.Match(
                onFirst: v1 => 1,
                onSecond: v2 => 2,
                onThird: v3 => 3,
                onFourth: v4 => 4,
                onFifth: v5 => 5,
                onSixth: v6 => 6,
                onSeventh: v7 => 7,
                onEighth: v8 => 8);

        private static int TestIt8Action(Either<Test1, Test2, Test3, Test4, Test5, Test6, Test7, Test8> e)
        {
            int n = 0;

            e.MatchVoid(
                onFirst: v1 =>
                {
                    n = 1;
                },
                onSecond: v2 =>
                {
                    n = 2;
                },
                onThird: v3 =>
                {
                    n = 3;
                },
                onFourth: v4 =>
                {
                    n = 4;
                },
                onFifth: v5 =>
                {
                    n = 5;
                },
                onSixth: v6 =>
                {
                    n = 6;
                },
                onSeventh: v7 =>
                {
                    n = 7;
                },
                onEighth: v8 =>
                {
                    n = 8;
                });

            return n;
        }

        [Test]
        public void ImplementsIEquatable()
        {
            Assert.IsTrue(typeof(IEquatable<Either<int, string>>).IsAssignableFrom(typeof(Either<int, string>)));

            Assert.IsTrue(
                typeof(IEquatable<Either<int, string, bool, char, byte, long, short, uint>>).IsAssignableFrom(
                    typeof(Either<int, string, bool, char, byte, long, short, uint>)));
        }

        [Test]
        public void DefaultComparerDoesNotBox() =>
            Assert.AreNotEqual(
                expected: "ObjectEqualityComparer`1",
                actual: EqualityComparer<Either<int, string>>.Default.GetType().Name);

        [Test]
        public void TypedEqualsAgreesWithOperator()
        {
            Either<int, string> a = Either.First(2);
            Either<int, string> b = Either.First(2);
            Either<int, string> c = Either.Second("2");

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(c));
            Assert.AreEqual(expected: a == b, actual: a.Equals(b));
            Assert.AreEqual(expected: a == c, actual: a.Equals(c));
        }

        [Test]
        public void TypedEqualsWorksInCollections()
        {
            Either<int, string>[] source = { Either.First(1), Either.Second("a"), Either.First(1), Either.Second("a") };

            CollectionAssert.AreEqual(
                expected: new Either<int, string>[] { Either.First(1), Either.Second("a") },
                actual: source.Distinct());
        }
    }
}