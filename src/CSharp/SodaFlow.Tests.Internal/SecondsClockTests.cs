using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers the subtraction of the seconds clock. The timer thread reads an
///     interval from it at each step, thus it must give one for each deadline.
/// </summary>
public sealed class SecondsClockTests
{
    [Test]
    public async Task AnIntervalInRangeIsTheDifference()
    {
        await Assert.That(SecondsClock.Subtract(first: 2.5, second: 1.0)).IsEqualTo(TimeSpan.FromSeconds(1.5));
        await Assert.That(SecondsClock.Subtract(first: 1.0, second: 2.5)).IsEqualTo(TimeSpan.FromSeconds(-1.5));
    }

    // A deadline at infinity, or more than about 29,000 years away, has no TimeSpan. The interval
    // stops at the end of the range, where FromSeconds threw.
    [Test]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.MaxValue)]
    [Arguments(1e15)]
    public async Task AnIntervalPastTheRangeStopsAtItsEnd(double far)
    {
        await Assert.That(SecondsClock.Subtract(first: far, second: 0.0)).IsEqualTo(TimeSpan.MaxValue);
        await Assert.That(SecondsClock.Subtract(first: -far, second: 0.0)).IsEqualTo(TimeSpan.MinValue);
    }
}
