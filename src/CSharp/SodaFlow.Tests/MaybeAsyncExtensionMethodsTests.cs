using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class MaybeAsyncExtensionMethodsTests
    {
        [Test]
        public async Task TestMapAsync()
        {
            Assert.AreEqual(Maybe.Some(4), await Maybe.Some(2).MapAsync(v => Task.FromResult(v * 2)));
            Assert.AreEqual(Maybe<int>.None, await Maybe<int>.None.MapAsync(v => Task.FromResult(v * 2)));
        }

        [Test]
        public async Task TestMapAsyncDoesNotRunFunctionWithoutValue()
        {
            int calls = 0;

            Maybe<int> result = await Maybe<int>.None.MapAsync(
                v =>
                {
                    calls++;
                    return Task.FromResult(v);
                });

            Assert.AreEqual(Maybe<int>.None, result);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public async Task TestBindAsync()
        {
            Assert.AreEqual(
                Maybe.Some(4),
                await Maybe.Some(2).BindAsync(v => Task.FromResult(Maybe.Some(v * 2))));
            Assert.AreEqual(
                Maybe<int>.None,
                await Maybe.Some(2).BindAsync(v => Task.FromResult(Maybe<int>.None)));
            Assert.AreEqual(
                Maybe<int>.None,
                await Maybe<int>.None.BindAsync(v => Task.FromResult(Maybe.Some(v * 2))));
        }

        [Test]
        public async Task TestWhereAsync()
        {
            Assert.AreEqual(Maybe.Some(2), await Maybe.Some(2).WhereAsync(v => Task.FromResult(v % 2 == 0)));
            Assert.AreEqual(Maybe<int>.None, await Maybe.Some(3).WhereAsync(v => Task.FromResult(v % 2 == 0)));
            Assert.AreEqual(Maybe<int>.None, await Maybe<int>.None.WhereAsync(v => Task.FromResult(true)));
        }

        [Test]
        public async Task TestMapOnTask()
        {
            Assert.AreEqual(Maybe.Some(4), await SomeAsync(2).Map(v => v * 2));
            Assert.AreEqual(Maybe<int>.None, await NoneAsync<int>().Map(v => v * 2));
        }

        [Test]
        public async Task TestMapAsyncOnTask()
        {
            Assert.AreEqual(Maybe.Some(4), await SomeAsync(2).MapAsync(v => Task.FromResult(v * 2)));
            Assert.AreEqual(Maybe<int>.None, await NoneAsync<int>().MapAsync(v => Task.FromResult(v * 2)));
        }

        [Test]
        public async Task TestBindOnTask()
        {
            Assert.AreEqual(Maybe.Some(4), await SomeAsync(2).Bind(v => Maybe.Some(v * 2)));
            Assert.AreEqual(Maybe<int>.None, await SomeAsync(2).Bind(v => Maybe<int>.None));
            Assert.AreEqual(Maybe<int>.None, await NoneAsync<int>().Bind(v => Maybe.Some(v * 2)));
        }

        [Test]
        public async Task TestBindAsyncOnTask()
        {
            Assert.AreEqual(
                Maybe.Some(4),
                await SomeAsync(2).BindAsync(v => Task.FromResult(Maybe.Some(v * 2))));
            Assert.AreEqual(
                Maybe<int>.None,
                await NoneAsync<int>().BindAsync(v => Task.FromResult(Maybe.Some(v * 2))));
        }

        [Test]
        public async Task TestWhereOnTask()
        {
            Assert.AreEqual(Maybe.Some(2), await SomeAsync(2).Where(v => v % 2 == 0));
            Assert.AreEqual(Maybe<int>.None, await SomeAsync(3).Where(v => v % 2 == 0));
        }

        [Test]
        public async Task TestWhereAsyncOnTask()
        {
            Assert.AreEqual(Maybe.Some(2), await SomeAsync(2).WhereAsync(v => Task.FromResult(v % 2 == 0)));
            Assert.AreEqual(Maybe<int>.None, await SomeAsync(3).WhereAsync(v => Task.FromResult(v % 2 == 0)));
        }

        [Test]
        public async Task TestOrElseOnTask()
        {
            Assert.AreEqual(Maybe.Some(2), await SomeAsync(2).OrElse(Maybe.Some(9)));
            Assert.AreEqual(Maybe.Some(9), await NoneAsync<int>().OrElse(Maybe.Some(9)));
        }

        [Test]
        public async Task TestMatchOnTask()
        {
            Assert.AreEqual("2", await SomeAsync(2).Match(v => v.ToString(), () => "none"));
            Assert.AreEqual("none", await NoneAsync<int>().Match(v => v.ToString(), () => "none"));
        }

        [Test]
        public async Task TestValueOrOnTask()
        {
            Assert.AreEqual(2, await SomeAsync(2).ValueOr(9));
            Assert.AreEqual(9, await NoneAsync<int>().ValueOr(9));
            Assert.AreEqual(2, await SomeAsync(2).ValueOrDefault());
            Assert.AreEqual(0, await NoneAsync<int>().ValueOrDefault());
        }

        [Test]
        public async Task TestValueOrThrowOnTask()
        {
            Assert.AreEqual(2, await SomeAsync(2).ValueOrThrow(() => new InvalidOperationException("no value")));

            InvalidOperationException e = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await NoneAsync<int>().ValueOrThrow(() => new InvalidOperationException("no value")));

            Assert.AreEqual("no value", e.Message);
        }

        [Test]
        public async Task TestChainWithoutAwaitingInTheMiddle()
        {
            Maybe<string> found = await "7".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(v => v.ToUpperInvariant())
                .Where(v => v.Length > 0);

            Assert.AreEqual(Maybe.Some("SEVEN"), found);

            Maybe<string> missing = await "8".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(v => v.ToUpperInvariant())
                .Where(v => v.Length > 0);

            Assert.AreEqual(Maybe<string>.None, missing);

            Maybe<string> unparseable = await "x".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(v => v.ToUpperInvariant())
                .Where(v => v.Length > 0);

            Assert.AreEqual(Maybe<string>.None, unparseable);
        }

        [Test]
        public void TestEmptyPathDoesNotAllocateANewTask()
        {
            // The completed task giving no value is the same one every time, so a lookup which
            // misses costs nothing beyond the miss itself.
            Assert.AreSame(
                Maybe<int>.None.MapAsync(v => Task.FromResult(v)),
                Maybe<int>.None.MapAsync(v => Task.FromResult(v)));
        }

        [Test]
        public async Task TestBindAsyncReturnsTheFunctionsOwnTask()
        {
            Task<Maybe<int>> inner = Task.FromResult(Maybe.Some(3));

            Assert.AreSame(inner, Maybe.Some(2).BindAsync(v => inner));
            Assert.AreEqual(Maybe.Some(3), await inner);
        }

        private static Task<Maybe<T>> SomeAsync<T>(T value) => Task.FromResult(Maybe.Some(value));

        private static Task<Maybe<T>> NoneAsync<T>() => Task.FromResult(Maybe<T>.None);

        private static Task<Maybe<string>> LookUpAsync(int key) =>
            Task.FromResult(key == 7 ? Maybe.Some("seven") : Maybe<string>.None);
    }
}
