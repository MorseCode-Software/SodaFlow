using System;
using System.Diagnostics;

namespace SodaFlow;

internal sealed class EntryPriorityQueue
{
    private readonly HeadAndTail last = new(head: null, tail: null);

    private HeadAndTail?[] entries = new HeadAndTail?[1000];

    private int entriesSize = 1000;
    private int maxRank = -1;

    // The minimum rank in the queue, or int.MaxValue when the queue is empty. Dequeue walks the
    // buckets from this rank to the first entry. Each transaction starts with an empty queue.
    // Thus, with zero as the empty value, a transaction whose first entry had a deep rank walked
    // each bucket below it. With int.MaxValue, the test in Enqueue that lowers this rank sets
    // it at the first entry, and the walk starts there. An entry with NullRank goes to the last
    // list and does not touch this rank.
    private int minRank = int.MaxValue;

    // The number of entries in the buckets of the entries array. Only the checks keep it. It lets
    // CheckQueue walk only the ranks from minRank to maxRank and still find an entry outside them.
    private int rankedCount;

    // The checks below run in a debug build only. A release build removes each call, thus the
    // hot path pays nothing for them. Before, a false constant guarded them, and no build ran
    // them.
    //
    // The checks walk only the ranks from minRank to maxRank. All transactions share the entries
    // array, and it never gets shorter. Thus, a walk of the full array made each operation cost
    // time for the deepest rank that the process ever queued.
    [Conditional("DEBUG")]
    private void CheckQueue()
    {
        int found = 0;

        for (int i = this.minRank; i <= this.maxRank; i++)
        {
            HeadAndTail? e = this.entries[i];

            if (e == null)
            {
                continue;
            }

            if (e.Head?.PqPrev != null)
            {
                throw new InvalidOperationException("Head cannot have a previous entry.");
            }

            if (e.Tail?.PqNext != null)
            {
                throw new InvalidOperationException("Tail cannot have a next entry.");
            }

            TransactionInternal.Entry? current = e.Head;

            while (current != null)
            {
                if (current.PqRank != i)
                {
                    throw new InvalidOperationException("Rank was not the expected value.");
                }

                if (!current.InPq)
                {
                    throw new InvalidOperationException("Entry was not marked as being in a priority queue.");
                }

                found++;
                current = current.PqNext;
            }
        }

        if (found != this.rankedCount)
        {
            throw new InvalidOperationException("An entry was outside the range of ranks of the queue.");
        }
    }

    [Conditional("DEBUG")]
    private void CountRanked(TransactionInternal.Entry e, int change)
    {
        if (e.PqRank != Node.NullRank)
        {
            this.rankedCount += change;
        }
    }

    internal bool IsEmpty()
    {
        if (this.minRank <= this.maxRank)
        {
            return false;
        }

        this.minRank = int.MaxValue;
        this.maxRank = -1;
        return this.last.Head == null;
    }

    internal void Enqueue(TransactionInternal.Entry e)
    {
        if (e.PqRank != e.Node.Rank)
        {
            throw new InvalidOperationException("Enqueue requires ranks to agree.");
        }

        // A rank is an index into the entries array. Thus, the array must be longer than the
        // rank, and this test uses >= and not >. The size must double again and again until the
        // array holds the rank, because one increase is not sufficient for a rank far after the
        // end.
        if (e.PqRank != Node.NullRank && e.PqRank >= this.entriesSize)
        {
            int newSize = this.entriesSize;

            while (newSize <= e.PqRank)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = int.MaxValue;
                    break;
                }

                newSize *= 2;
            }

            Array.Resize(array: ref this.entries, newSize: newSize);
            this.entriesSize = newSize;
        }

        if (e.InPq)
        {
            return;
        }

        e.InPq = true;
        this.CountRanked(e: e, change: 1);

        HeadAndTail? entry;

        if (e.PqRank == Node.NullRank)
        {
            entry = this.last;
        }
        else
        {
            if (e.PqRank < this.minRank)
            {
                this.minRank = e.PqRank;
            }

            if (e.PqRank > this.maxRank)
            {
                this.maxRank = e.PqRank;
            }

            entry = this.entries[e.PqRank];

            if (entry == null)
            {
                this.entries[e.PqRank] = new HeadAndTail(head: e, tail: e);

                this.CheckQueue();

                return;
            }
        }

        if (entry.Head == null)
        {
            entry.Head = entry.Tail = e;

            this.CheckQueue();

            return;
        }

        // ReSharper disable once NullableWarningSuppressionIsUsed - entry.Tail can only be null when entry.Head is
        // also null.  In that case, the two are set to a non-null value above.
        entry.Tail!.PqNext = e;
        e.PqPrev = entry.Tail;
        entry.Tail = e;

        this.CheckQueue();
    }

    public TransactionInternal.Entry? Dequeue()
    {
        while (true)
        {
            if (this.minRank > this.maxRank)
            {
                this.minRank = int.MaxValue;
                this.maxRank = -1;

                if (this.last.Head != null)
                {
                    TransactionInternal.Entry result1 = this.last.Head;
                    this.Remove(result1);
                    return result1;
                }

                this.CheckQueue();

                return null;
            }

            HeadAndTail? entry = this.entries[this.minRank];

            if (entry?.Head == null)
            {
                this.minRank++;
                continue;
            }

            TransactionInternal.Entry result = entry.Head;
            this.Remove(result);

            while (true)
            {
                entry = this.entries[this.minRank];

                if (entry?.Head == null)
                {
                    this.minRank++;

                    if (this.minRank > this.maxRank)
                    {
                        this.minRank = int.MaxValue;
                        this.maxRank = -1;
                        break;
                    }

                    continue;
                }

                break;
            }

            this.CheckNoSmallerRankThan(result);

            return result;
        }
    }

    private void Remove(TransactionInternal.Entry e)
    {
        if (!e.InPq)
        {
            return;
        }

        e.InPq = false;
        this.CountRanked(e: e, change: -1);

        // ReSharper disable once NullableWarningSuppressionIsUsed - e.InPq would be false if the entry was not able
        // to be found in the queue.
        HeadAndTail entry = e.PqRank == Node.NullRank ? this.last : this.entries[e.PqRank]!;

        e.PqPrev?.PqNext = e.PqNext;
        e.PqNext?.PqPrev = e.PqPrev;

        if (entry.Head == e)
        {
            entry.Head = entry.Head.PqNext;
        }

        if (entry.Tail == e)
        {
            entry.Tail = entry.Tail.PqPrev;
        }

        if (entry.Head == null)
        {
            entry.Tail = null;
        }

        e.PqNext = null;
        e.PqPrev = null;

        this.CheckBucketIsInQueue(entry);
    }

    // The entry that Dequeue selected has the smallest rank in the queue.
    [Conditional("DEBUG")]
    private void CheckNoSmallerRankThan(TransactionInternal.Entry result)
    {
        // CheckQueue below finds an entry outside these ranks.
        for (int i = this.minRank; i <= this.maxRank; i++)
        {
            HeadAndTail? bucket = this.entries[i];

            if (bucket == null)
            {
                continue;
            }

            TransactionInternal.Entry? current = bucket.Head;

            while (current != null)
            {
                if (current.PqRank < result.PqRank)
                {
                    throw new InvalidOperationException(
                        "Priority queue contains less than the expected number of elements.");
                }

                current = current.PqNext;
            }
        }

        this.CheckQueue();
    }

    // After a removal, each entry left in the bucket is still marked as in the queue.
    [Conditional("DEBUG")]
    private void CheckBucketIsInQueue(HeadAndTail entry)
    {
        this.CheckQueue();

        TransactionInternal.Entry? current = entry.Head;

        while (current != null)
        {
            if (!current.InPq)
            {
                throw new InvalidOperationException("Entry was expected to be in the priority queue.");
            }

            current = current.PqNext;
        }
    }

    public void ChangeRank(TransactionInternal.Entry e, int newRank)
    {
        this.Remove(e);
        e.PqRank = newRank;
        this.Enqueue(e);
    }

    private sealed class HeadAndTail(TransactionInternal.Entry? head, TransactionInternal.Entry? tail)
    {
        public TransactionInternal.Entry? Head = head;
        public TransactionInternal.Entry? Tail = tail;
    }
}
