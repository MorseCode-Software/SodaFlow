using System;
using System.Threading.Tasks;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

public class MaybeAsyncExtensionMethodsTests
{
    [Test]
    public async Task TestMapAsync()
    {
        await Assert.That(await Maybe.Some(2).MapAsync(static v => Task.FromResult(v * 2))).IsEqualTo(Maybe.Some(4));

        await Assert.That(await Maybe<int>.None.MapAsync(static v => Task.FromResult(v * 2))).IsEqualTo(Maybe<int>.None);
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

        await Assert.That(result).IsEqualTo(Maybe<int>.None);
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task TestBindAsync()
    {
        await Assert.That(await Maybe.Some(2).BindAsync(static v => Task.FromResult(Maybe.Some(v * 2)))).IsEqualTo(Maybe.Some(4));

        await Assert.That(await Maybe.Some(2).BindAsync(static _ => Task.FromResult(Maybe<int>.None))).IsEqualTo(Maybe<int>.None);

        await Assert.That(await Maybe<int>.None.BindAsync(static v => Task.FromResult(Maybe.Some(v * 2)))).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestWhereAsync()
    {
        await Assert.That(await Maybe.Some(2).WhereAsync(static v => Task.FromResult(v % 2 == 0))).IsEqualTo(Maybe.Some(2));

        await Assert.That(await Maybe.Some(3).WhereAsync(static v => Task.FromResult(v % 2 == 0))).IsEqualTo(Maybe<int>.None);

        await Assert.That(await Maybe<int>.None.WhereAsync(static _ => Task.FromResult(true))).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestMapOnTask()
    {
        await Assert.That(await SomeAsync(2).Map(static v => v * 2)).IsEqualTo(Maybe.Some(4));
        await Assert.That(await NoneAsync<int>().Map(static v => v * 2)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestMapAsyncOnTask()
    {
        await Assert.That(await SomeAsync(2).MapAsync(static v => Task.FromResult(v * 2))).IsEqualTo(Maybe.Some(4));

        await Assert.That(await NoneAsync<int>().MapAsync(static v => Task.FromResult(v * 2))).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestBindOnTask()
    {
        await Assert.That(await SomeAsync(2).Bind(static v => Maybe.Some(v * 2))).IsEqualTo(Maybe.Some(4));
        await Assert.That(await SomeAsync(2).Bind(static _ => Maybe<int>.None)).IsEqualTo(Maybe<int>.None);
        await Assert.That(await NoneAsync<int>().Bind(static v => Maybe.Some(v * 2))).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestBindAsyncOnTask()
    {
        await Assert.That(await SomeAsync(2).BindAsync(static v => Task.FromResult(Maybe.Some(v * 2)))).IsEqualTo(Maybe.Some(4));

        await Assert.That(await NoneAsync<int>().BindAsync(static v => Task.FromResult(Maybe.Some(v * 2)))).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestWhereOnTask()
    {
        await Assert.That(await SomeAsync(2).Where(static v => v % 2 == 0)).IsEqualTo(Maybe.Some(2));
        await Assert.That(await SomeAsync(3).Where(static v => v % 2 == 0)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestWhereAsyncOnTask()
    {
        await Assert.That(await SomeAsync(2).WhereAsync(static v => Task.FromResult(v % 2 == 0))).IsEqualTo(Maybe.Some(2));

        await Assert.That(await SomeAsync(3).WhereAsync(static v => Task.FromResult(v % 2 == 0))).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestOrElseOnTask()
    {
        await Assert.That(await SomeAsync(2).OrElse(Maybe.Some(9))).IsEqualTo(Maybe.Some(2));
        await Assert.That(await NoneAsync<int>().OrElse(Maybe.Some(9))).IsEqualTo(Maybe.Some(9));
    }

    [Test]
    public async Task TestMatchOnTask()
    {
        await Assert.That(await SomeAsync(2).Match(onSome: static v => v.ToString(), onNone: static () => "none")).IsEqualTo("2");

        await Assert.That(await NoneAsync<int>().Match(onSome: static v => v.ToString(), onNone: static () => "none")).IsEqualTo("none");
    }

    [Test]
    public async Task TestValueOrOnTask()
    {
        await Assert.That(await SomeAsync(2).ValueOr(9)).IsEqualTo(2);
        await Assert.That(await NoneAsync<int>().ValueOr(9)).IsEqualTo(9);
        await Assert.That(await SomeAsync(2).ValueOrDefault()).IsEqualTo(2);
        await Assert.That(await NoneAsync<int>().ValueOrDefault()).IsEqualTo(0);
    }

    [Test]
    public async Task TestValueOrThrowOnTask()
    {
        await Assert.That(await SomeAsync(2).ValueOrThrow(static () => new InvalidOperationException("no value"))).IsEqualTo(2);

        InvalidOperationException e =
            await Assert.That(static async () =>
                await NoneAsync<int>().ValueOrThrow(static () => new InvalidOperationException("no value"))).ThrowsExactly<InvalidOperationException>();

        await Assert.That(e.Message).IsEqualTo("no value");
    }

    [Test]
    public async Task TestChainWithoutAwaitingInTheMiddle()
    {
        Maybe<string> found =
            await "7".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(static v => v.ToUpperInvariant())
                .Where(static v => v.Length > 0);

        await Assert.That(found).IsEqualTo(Maybe.Some("SEVEN"));

        Maybe<string> missing =
            await "8".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(static v => v.ToUpperInvariant())
                .Where(static v => v.Length > 0);

        await Assert.That(missing).IsEqualTo(Maybe<string>.None);

        Maybe<string> unparseable =
            await "x".TryParseInt32()
                .BindAsync(LookUpAsync)
                .Map(static v => v.ToUpperInvariant())
                .Where(static v => v.Length > 0);

        await Assert.That(unparseable).IsEqualTo(Maybe<string>.None);
    }

    [Test]
    public async Task TestEmptyPathDoesNotAllocateANewTask() =>
        // The completed task giving no value is the same one every time, so a lookup which
        // misses costs nothing beyond the miss itself.
        await Assert.That(Maybe<int>.None.MapAsync(Task.FromResult)).IsSameReferenceAs(Maybe<int>.None.MapAsync(Task.FromResult));

    [Test]
    public async Task TestBindAsyncReturnsTheFunctionsOwnTask()
    {
        Task<Maybe<int>> inner = Task.FromResult(Maybe.Some(3));

        await Assert.That(Maybe.Some(2).BindAsync(_ => inner)).IsSameReferenceAs(inner);
        await Assert.That(await inner).IsEqualTo(Maybe.Some(3));
    }

    private static Task<Maybe<T>> SomeAsync<T>(T value) => Task.FromResult(Maybe.Some(value));

    private static Task<Maybe<T>> NoneAsync<T>() => Task.FromResult(Maybe<T>.None);

    private static Task<Maybe<string>> LookUpAsync(int key) =>
        Task.FromResult(key == 7 ? Maybe.Some("seven") : Maybe<string>.None);
}
