using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class EnumerableExtensionMethodsTests
    {
        [Test]
        public void TestChoose()
        {
            string[] source = { "1", "x", "3", string.Empty, "5" };

            IEnumerable<int> result = source.Choose(s => s.TryParseInt32());

            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, result);
        }

        [Test]
        public void TestChooseNoneKept()
        {
            string[] source = { "x", "y" };

            IEnumerable<int> result = source.Choose(s => s.TryParseInt32());

            CollectionAssert.AreEqual(new int[0], result);
        }

        [Test]
        public void TestChooseNullSource()
        {
            IEnumerable<string> source = null;

            IEnumerable<int> result = source.Choose(s => s.TryParseInt32());

            CollectionAssert.AreEqual(new int[0], result);
        }

        [Test]
        public void TestChooseIsLazy()
        {
            int calls = 0;

            IEnumerable<int> result = new[] { 1, 2, 3 }.Choose(
                v =>
                {
                    calls++;
                    return Maybe.Some(v);
                });

            Assert.AreEqual(0, calls);

            result.ToArray();

            Assert.AreEqual(3, calls);
        }

        [Test]
        public void TestChooseWithIndex()
        {
            string[] source = { "a", "b", "c", "d" };

            IEnumerable<string> result = source.Choose((v, i) => Maybe.SomeIf(i % 2 == 0, v + i));

            CollectionAssert.AreEqual(new[] { "a0", "c2" }, result);
        }

        [Test]
        public void TestFirstOrNone()
        {
            Assert.AreEqual(Maybe.Some(1), new[] { 1, 2, 3 }.FirstOrNone());
            Assert.AreEqual(Maybe<int>.None, new int[0].FirstOrNone());
            Assert.AreEqual(Maybe<int>.None, ((IEnumerable<int>)null).FirstOrNone());
        }

        [Test]
        public void TestFirstOrNoneKeepsDefaultValue()
        {
            Assert.AreEqual(Maybe.Some(0), new[] { 0, 1 }.FirstOrNone());
        }

        [Test]
        public void TestFirstOrNoneReadsOneElement()
        {
            int read = 0;

            Maybe<int> result = Counted(new[] { 1, 2, 3 }, () => read++).FirstOrNone();

            Assert.AreEqual(Maybe.Some(1), result);
            Assert.AreEqual(1, read);
        }

        [Test]
        public void TestFirstOrNoneWithPredicate()
        {
            Assert.AreEqual(Maybe.Some(2), new[] { 1, 2, 3, 4 }.FirstOrNone(v => v % 2 == 0));
            Assert.AreEqual(Maybe<int>.None, new[] { 1, 3 }.FirstOrNone(v => v % 2 == 0));
            Assert.AreEqual(Maybe<int>.None, ((IEnumerable<int>)null).FirstOrNone(v => true));
        }

        [Test]
        public void TestLastOrNoneIndexable()
        {
            Assert.AreEqual(Maybe.Some(3), new[] { 1, 2, 3 }.LastOrNone());
            Assert.AreEqual(Maybe<int>.None, new int[0].LastOrNone());
            Assert.AreEqual(Maybe<int>.None, ((IEnumerable<int>)null).LastOrNone());
        }

        [Test]
        public void TestLastOrNoneNotIndexable()
        {
            Assert.AreEqual(Maybe.Some(3), Yield(1, 2, 3).LastOrNone());
            Assert.AreEqual(Maybe<int>.None, Yield<int>().LastOrNone());
        }

        [Test]
        public void TestLastOrNoneWithPredicate()
        {
            Assert.AreEqual(Maybe.Some(4), new[] { 1, 2, 3, 4, 5 }.LastOrNone(v => v % 2 == 0));
            Assert.AreEqual(Maybe<int>.None, new[] { 1, 3 }.LastOrNone(v => v % 2 == 0));
        }

        [Test]
        public void TestSingleOrNone()
        {
            Assert.AreEqual(Maybe.Some(1), new[] { 1 }.SingleOrNone());
            Assert.AreEqual(Maybe<int>.None, new int[0].SingleOrNone());
            Assert.AreEqual(Maybe<int>.None, ((IEnumerable<int>)null).SingleOrNone());
        }

        [Test]
        public void TestSingleOrNoneThrowsOnMoreThanOne()
        {
            Assert.Throws<InvalidOperationException>(() => new[] { 1, 2 }.SingleOrNone());
        }

        [Test]
        public void TestSingleOrNoneReadsTwoElements()
        {
            int read = 0;

            Maybe<int> result = Counted(new[] { 1 }, () => read++).SingleOrNone();

            Assert.AreEqual(Maybe.Some(1), result);
            Assert.AreEqual(1, read);
        }

        [Test]
        public void TestSingleOrNoneWithPredicate()
        {
            Assert.AreEqual(Maybe.Some(2), new[] { 1, 2, 3 }.SingleOrNone(v => v % 2 == 0));
            Assert.AreEqual(Maybe<int>.None, new[] { 1, 3 }.SingleOrNone(v => v % 2 == 0));
            Assert.Throws<InvalidOperationException>(() => new[] { 2, 4 }.SingleOrNone(v => v % 2 == 0));
        }

        [Test]
        public void TestElementAtOrNoneIndexable()
        {
            int[] source = { 1, 2, 3 };

            Assert.AreEqual(Maybe.Some(1), source.ElementAtOrNone(0));
            Assert.AreEqual(Maybe.Some(3), source.ElementAtOrNone(2));
            Assert.AreEqual(Maybe<int>.None, source.ElementAtOrNone(3));
            Assert.AreEqual(Maybe<int>.None, source.ElementAtOrNone(-1));
            Assert.AreEqual(Maybe<int>.None, ((IEnumerable<int>)null).ElementAtOrNone(0));
        }

        [Test]
        public void TestElementAtOrNoneNotIndexable()
        {
            Assert.AreEqual(Maybe.Some(1), Yield(1, 2, 3).ElementAtOrNone(0));
            Assert.AreEqual(Maybe.Some(3), Yield(1, 2, 3).ElementAtOrNone(2));
            Assert.AreEqual(Maybe<int>.None, Yield(1, 2, 3).ElementAtOrNone(3));
            Assert.AreEqual(Maybe<int>.None, Yield(1, 2, 3).ElementAtOrNone(-1));
        }

        [Test]
        public void TestElementAtOrNoneReadsNoFurtherThanNeeded()
        {
            int read = 0;

            Maybe<int> result = Counted(new[] { 1, 2, 3, 4, 5 }, () => read++).ElementAtOrNone(1);

            Assert.AreEqual(Maybe.Some(2), result);
            Assert.AreEqual(2, read);
        }

        private static IEnumerable<T> Yield<T>(params T[] items)
        {
            foreach (T item in items)
            {
                yield return item;
            }
        }

        private static IEnumerable<T> Counted<T>(IEnumerable<T> source, Action onRead)
        {
            foreach (T item in source)
            {
                onRead();
                yield return item;
            }
        }
    }
}
