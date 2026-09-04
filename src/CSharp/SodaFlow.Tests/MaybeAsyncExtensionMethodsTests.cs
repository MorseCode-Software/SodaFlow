using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

[TestFixture]
public class MaybeAsyncExtensionMethodsTests
{
    [Test]
    public async Task TestMapAsync()
    {
        Assert.AreEqual(
            expected: Maybe.Some(4),
            actual: await Maybe.Some(2).MapAsync(static v => Task.FromResult(v * 2)));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await Maybe<int>.None.MapAsync(static v => Task.FromResult(v * 2)));
    }

    [Test]
    public async Task TestMapAsyncDoesNotRunFunctionWithoutValue()
    {
        int calls = 0;

        Maybe<int> result =
            await Maybe<int>.None.MapAsync(v =>
            {
                calls++;
                return Task.FromResult(v);
            });

        Assert.AreEqual(expected: Maybe<int>.None, actual: result);
        Assert.AreEqual(expected: 0, actual: calls);
    }

    [Test]
    public async Task TestBindAsync()
    {
        Assert.AreEqual(
            expected: Maybe.Some(4),
            actual: await Maybe.Some(2).BindAsync(static v => Task.FromResult(Maybe.Some(v * 2))));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await Maybe.Some(2).BindAsync(static _ => Task.FromResult(Maybe<int>.None)));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await Maybe<int>.None.BindAsync(static v => Task.FromResult(Maybe.Some(v * 2))));
    }

    [Test]
    public async Task TestWhereAsync()
    {
        Assert.AreEqual(
            expected: Maybe.Some(2),
            actual: await Maybe.Some(2).WhereAsync(static v => Task.FromResult(v % 2 == 0)));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await Maybe.Some(3).WhereAsync(static v => Task.FromResult(v % 2 == 0)));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await Maybe<int>.None.WhereAsync(static _ => Task.FromResult(true)));
    }

    [Test]
    public async Task TestMapOnTask()
    {
        Assert.AreEqual(expected: Maybe.Some(4), actual: await SomeAsync(2).Map(static v => v * 2));
        Assert.AreEqual(expected: Maybe<int>.None, actual: await NoneAsync<int>().Map(static v => v * 2));
    }

    [Test]
    public async Task TestMapAsyncOnTask()
    {
        Assert.AreEqual(
            expected: Maybe.Some(4),
            actual: await SomeAsync(2).MapAsync(static v => Task.FromResult(v * 2)));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await NoneAsync<int>().MapAsync(static v => Task.FromResult(v * 2)));
    }

    [Test]
    public async Task TestBindOnTask()
    {
        Assert.AreEqual(expected: Maybe.Some(4), actual: await SomeAsync(2).Bind(static v => Maybe.Some(v * 2)));
        Assert.AreEqual(expected: Maybe<int>.None, actual: await SomeAsync(2).Bind(static _ => Maybe<int>.None));
        Assert.AreEqual(expected: Maybe<int>.None, actual: await NoneAsync<int>().Bind(static v => Maybe.Some(v * 2)));
    }

    [Test]
    public async Task TestBindAsyncOnTask()
    {
        Assert.AreEqual(
            expected: Maybe.Some(4),
            actual: await SomeAsync(2).BindAsync(static v => Task.FromResult(Maybe.Some(v * 2))));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await NoneAsync<int>().BindAsync(static v => Task.FromResult(Maybe.Some(v * 2))));
    }

    [Test]
    public async Task TestWhereOnTask()
    {
        Assert.AreEqual(expected: Maybe.Some(2), actual: await SomeAsync(2).Where(static v => v % 2 == 0));
        Assert.AreEqual(expected: Maybe<int>.None, actual: await SomeAsync(3).Where(static v => v % 2 == 0));
    }

    [Test]
    public async Task TestWhereAsyncOnTask()
    {
        Assert.AreEqual(
            expected: Maybe.Some(2),
            actual: await SomeAsync(2).WhereAsync(static v => Task.FromResult(v % 2 == 0)));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: await SomeAsync(3).WhereAsync(static v => Task.FromResult(v % 2 == 0)));
    }

    [Test]
    public async Task TestOrElseOnTask()
    {
        Assert.AreEqual(expected: Maybe.Some(2), actual: await SomeAsync(2).OrElse(Maybe.Some(9)));
        Assert.AreEqual(expected: Maybe.Some(9), actual: await NoneAsync<int>().OrElse(Maybe.Some(9)));
    }

    [Test]
    public async Task TestMatchOnTask()
    {
        Assert.AreEqual(
            expected: "2",
            actual: await SomeAsync(2).Match(onSome: static v => v.ToString(), onNone: static () => "none"));

        Assert.AreEqual(
            expected: "none",
            actual: await NoneAsync<int>().Match(onSome: static v => v.ToString(), onNone: static () => "none"));
    }

    [Test]
    public async Task TestValueOrOnTask()
    {
        Assert.AreEqual(expected: 2, actual: await SomeAsync(2).ValueOr(9));
        Assert.AreEqual(expected: 9, actual: await NoneAsync<int>().ValueOr(9));
        Assert.AreEqual(expected: 2, actual: await SomeAsync(2).ValueOrDefault());
        Assert.AreEqual(expected: 0, actual: await NoneAsync<int>().ValueOrDefault());
    }

    [Test]
    public async Task TestValueOrThrowOnTask()
    {
        Assert.AreEqual(
            expected: 2,
            actual: await SomeAsync(2).ValueOrThrow(static () => new InvalidOperationException("no value")));

        InvalidOperationException e =
            Assert.ThrowsAsync<InvalidOperationException>(static async () =>
                await NoneAsync<int>().ValueOrThrow(static () => new InvalidOperationException("no value")));

        Assert.AreEqual(expected: "no value", actual: e.Message);
    }

    [Test]
    public async Task TestChainWithoutAwaitingInTheMiddle()
    {
        Maybe<string> found =
            await "7".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(static v => v.ToUpperInvariant())
                .Where(static v => v.Length > 0);

        Assert.AreEqual(expected: Maybe.Some("SEVEN"), actual: found);

        Maybe<string> missing =
            await "8".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(static v => v.ToUpperInvariant())
                .Where(static v => v.Length > 0);

        Assert.AreEqual(expected: Maybe<string>.None, actual: missing);

        Maybe<string> unparseable =
            await "x".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(static v => v.ToUpperInvariant())
                .Where(static v => v.Length > 0);

        Assert.AreEqual(expected: Maybe<string>.None, actual: unparseable);
    }

    [Test]
    public void TestEmptyPathDoesNotAllocateANewTask() =>
        // The completed task giving no value is the same one every time, so a lookup which
        // misses costs nothing beyond the miss itself.
        Assert.AreSame(
            expected: Maybe<int>.None.MapAsync(Task.FromResult),
            actual: Maybe<int>.None.MapAsync(Task.FromResult));

    [Test]
    public async Task TestBindAsyncReturnsTheFunctionsOwnTask()
    {
        Task<Maybe<int>> inner = Task.FromResult(Maybe.Some(3));

        Assert.AreSame(expected: inner, actual: Maybe.Some(2).BindAsync(_ => inner));
        Assert.AreEqual(expected: Maybe.Some(3), actual: await inner);
    }

    private static Task<Maybe<T>> SomeAsync<T>(T value) => Task.FromResult(Maybe.Some(value));

    private static Task<Maybe<T>> NoneAsync<T>() => Task.FromResult(Maybe<T>.None);

    private static Task<Maybe<string>> LookUpAsync(int key) =>
        Task.FromResult(key == 7 ? Maybe.Some("seven") : Maybe<string>.None);
}
