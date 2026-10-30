using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

/// <summary>
///     Covers the public surface of <see cref="LazySample{T}" />. Only SodaFlow
///     reads a sample, thus each test reads one through a constant cell.
/// </summary>
public sealed class LazySampleTests
{
    private static T Read<T>(LazySample<T> sample) => Cell.ConstantLazy(sample).Sample();

    [Test]
    public async Task DeferGivesTheValueOfItsFunction() =>
        await Assert.That(Read(LazySample.Defer(static () => 3))).IsEqualTo(3);

    [Test]
    public async Task MapTransformsTheValue()
    {
        CellSink<int> cell = Cell.CreateSink(2);

        await Assert.That(Read(cell.SampleLazy().Map(static v => v * 10))).IsEqualTo(20);
    }

    [Test]
    public async Task LiftCombinesTwoThreeAndFourSamples()
    {
        LazySample<int> one = LazySample.Defer(static () => 1);
        LazySample<int> two = Cell.Constant(2).SampleLazy();
        LazySample<int> three = LazySample.Defer(static () => 3);
        LazySample<int> four = Cell.Constant(4).SampleLazy();

        await Assert.That(Read(one.Lift(b: two, f: static (a, b) => a + b))).IsEqualTo(3);
        await Assert.That(Read(one.Lift(b: two, c: three, f: static (a, b, c) => a + b + c))).IsEqualTo(6);

        await Assert.That(Read(one.Lift(b: two, c: three, d: four, f: static (a, b, c, d) => a + b + c + d)))
            .IsEqualTo(10);
    }

    [Test]
    public async Task EachMemberRejectsANullFunction()
    {
        LazySample<int> sample = LazySample.Defer(static () => 1);

        // ReSharper disable NullableWarningSuppressionIsUsed - Testing for exception on null.
        await Assert.That(static () => LazySample.Defer<int>(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => sample.Map<int, int>(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => sample.Lift<int, int, int>(b: sample, f: null!)).ThrowsExactly<ArgumentNullException>();
        // ReSharper restore NullableWarningSuppressionIsUsed
    }
}
