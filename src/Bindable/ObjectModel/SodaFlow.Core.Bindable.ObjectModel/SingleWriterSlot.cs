using System.Threading;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     A value that one thread writes and other threads read, where a reader always
///     gets a value that the writer stored in full. The usual name is a sequence
///     lock.
/// </summary>
/// <remarks>
///     <para>
///         A bindable gives each change to the binding thread. Before, the post
///         captured the value in a closure, and the closure and its delegate were two
///         objects for each change. The slot holds the value in a field, and the post
///         is one delegate that the bindable keeps.
///     </para>
///     <para>
///         A field alone is not sufficient. A value larger than a word is two or more
///         stores. Thus, a reader can see one store of a newer value with one store of
///         an older value. The version counter shows that condition. The writer makes
///         the counter odd before the stores and even after them. A reader that finds
///         the counter odd reads again, and so does a reader that finds it changed
///         after the read. Only one thread writes, because the writer is a listener
///         in a transaction, and a transaction holds the lock of the process. A reader
///         that competes with a writer that stores without pause reads again until
///         the writer pauses. A bindable writes one time for each transaction.
///     </para>
/// </remarks>
internal sealed class SingleWriterSlot<T>
{
    private T? value;
    private int version;

    /// <summary>Stores a value. Only the one writer calls this.</summary>
    public void Write(T value)
    {
        // The increment is a full fence, thus the store of the value cannot move before it.
        // The volatile write is a release, thus the store cannot move after it.
        int odd = Interlocked.Increment(ref this.version);
        this.value = value;
        Volatile.Write(location: ref this.version, value: odd + 1);
    }

    /// <summary>
    ///     Reads the last value that <see cref="Write" /> stored in full. A read before
    ///     the first write gives the default value.
    /// </summary>
    public T Read()
    {
        SpinWait spin = default;

        while (true)
        {
            int before = Volatile.Read(ref this.version);

            if ((before & 1) == 0)
            {
                T? read = this.value;

                // The barrier keeps the load of the value before the second load of the version.
                Thread.MemoryBarrier();

                if (Volatile.Read(ref this.version) == before)
                {
                    // ReSharper disable once NullableWarningSuppressionIsUsed - The field holds a value that Write stored, or the default before the first write, which the summary states.
                    return read!;
                }
            }

            spin.SpinOnce();
        }
    }
}
