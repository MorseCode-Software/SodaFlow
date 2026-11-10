using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers the ids that the transactions get. Zero is the id of a transaction
///     that has no id, thus no transaction can get it.
/// </summary>
public sealed class TransactionIdSequenceTests
{
    [Test]
    public async Task TheFirstIdsAreOneAndTwo()
    {
        long last = 0;

        long first = TransactionIdSequence.Next(ref last);
        long second = TransactionIdSequence.Next(ref last);

        await Assert.That(first).IsEqualTo(1);
        await Assert.That(second).IsEqualTo(2);
        await Assert.That(last).IsEqualTo(2);
    }

    // The count gets to zero from below, after it goes from the largest value to the smallest value.
    [Test]
    public async Task TheIdAfterMinusOneIsOne()
    {
        long last = -1;

        long id = TransactionIdSequence.Next(ref last);

        await Assert.That(id).IsEqualTo(1);
        await Assert.That(last).IsEqualTo(1);
    }

    [Test]
    public async Task TheIdsOnEachSideOfZeroAreInSequence()
    {
        long last = -3;

        long[] actual =
        [
            TransactionIdSequence.Next(ref last),
            TransactionIdSequence.Next(ref last),
            TransactionIdSequence.Next(ref last),
            TransactionIdSequence.Next(ref last)
        ];

        await Assert.That(actual).IsEquivalentTo(expected: [-2L, -1L, 1L, 2L], ordering: CollectionOrdering.Matching);
    }

    // No exception. The code only compares two ids for equality, thus a negative id is correct.
    [Test]
    public async Task TheIdAfterTheLargestValueIsTheSmallestValue()
    {
        long last = long.MaxValue;

        long id = TransactionIdSequence.Next(ref last);

        await Assert.That(id).IsEqualTo(long.MinValue);
    }
}
