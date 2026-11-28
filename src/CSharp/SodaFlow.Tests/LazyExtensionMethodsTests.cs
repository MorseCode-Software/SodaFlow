using System;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class LazyExtensionMethodsTests
{
    [Test]
    public async Task MapAndLiftGiveTheValueOfTheFunctionOnTheInputs()
    {
        Lazy<int> one = new(static () => 1);
        Lazy<int> two = new(static () => 2);
        Lazy<int> three = new(static () => 3);
        Lazy<int> four = new(static () => 4);

        await Assert.That(one.Map(static v => v + 10).Value).IsEqualTo(11);
        await Assert.That(one.Lift(b: two, f: static (a, b) => a * 10 + b).Value).IsEqualTo(12);
        await Assert.That(one.Lift(b: two, c: three, f: static (a, b, c) => a * 100 + b * 10 + c).Value).IsEqualTo(123);

        await Assert.That(
                one.Lift(b: two, c: three, d: four, f: static (a, b, c, d) => a * 1000 + b * 100 + c * 10 + d)
                    .Value)
            .IsEqualTo(1234);

        await Assert.That(one.Map(f: static v => v + 10, mode: LazyThreadSafetyMode.None).Value).IsEqualTo(11);

        await Assert.That(one.Lift(b: two, f: static (a, b) => a * 10 + b, mode: LazyThreadSafetyMode.None).Value)
            .IsEqualTo(12);

        await Assert.That(
                one.Lift(
                        b: two,
                        c: three,
                        f: static (a, b, c) => a * 100 + b * 10 + c,
                        mode: LazyThreadSafetyMode.None)
                    .Value)
            .IsEqualTo(123);

        await Assert.That(
                one.Lift(
                        b: two,
                        c: three,
                        d: four,
                        f: static (a, b, c, d) => a * 1000 + b * 100 + c * 10 + d,
                        mode: LazyThreadSafetyMode.None)
                    .Value)
            .IsEqualTo(1234);
    }

    // The form with no mode is ExecutionAndPublication, as new Lazy<T>(valueFactory) is. That mode
    // keeps an exception from the function, thus the second read throws again and does not run the
    // function.
    [Test]
    public async Task TheFormWithNoModeKeepsAnExceptionOfTheFunction()
    {
        Lazy<int> input = new(static () => 1);
        RunCounter counter = new();
        Lazy<int> mapped = input.Map(counter.FailFirst);

        await Assert.That(() => mapped.Value).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => mapped.Value).ThrowsExactly<InvalidOperationException>();
        await Assert.That(counter.Runs).IsEqualTo(1);
    }

    // PublicationOnly keeps no exception, thus a read after a throw runs the function again. Each
    // form gives its mode to the result. A form that drops the mode keeps the exception.
    [Test]
    public async Task EachFormWithAModeGivesThatModeToTheResult()
    {
        Lazy<int> one = new(static () => 1);
        RunCounter mapCounter = new();
        RunCounter lift2Counter = new();
        RunCounter lift3Counter = new();
        RunCounter lift4Counter = new();

        (RunCounter Counter, Lazy<int> Result)[] results =
        [
            (mapCounter, one.Map(f: mapCounter.FailFirst, mode: LazyThreadSafetyMode.PublicationOnly)),
            (lift2Counter,
                one.Lift(
                    b: one,
                    f: (a, _) => lift2Counter.FailFirst(value: a),
                    mode: LazyThreadSafetyMode.PublicationOnly)),
            (lift3Counter,
                one.Lift(
                    b: one,
                    c: one,
                    f: (a, _, _) => lift3Counter.FailFirst(value: a),
                    mode: LazyThreadSafetyMode.PublicationOnly)),
            (lift4Counter,
                one.Lift(
                    b: one,
                    c: one,
                    d: one,
                    f: (a, _, _, _) => lift4Counter.FailFirst(value: a),
                    mode: LazyThreadSafetyMode.PublicationOnly))
        ];

        foreach ((RunCounter counter, Lazy<int> result) in results)
        {
            await Assert.That(() => result.Value).ThrowsExactly<InvalidOperationException>();
            await Assert.That(result.Value).IsEqualTo(1);
            await Assert.That(counter.Runs).IsEqualTo(2);
        }
    }

    // Each test function counts its runs here, thus each result has its own count.
    private sealed class RunCounter
    {
        public int Runs { get; private set; }

        // Throws at the first run, and gives the value at each run after it.
        public int FailFirst(int value)
        {
            this.Runs++;

            return this.Runs == 1 ? throw new InvalidOperationException("The first run fails.") : value;
        }
    }
}
