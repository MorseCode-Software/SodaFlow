using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     An object that runs cleanup code when the garbage collector removes it.
/// </summary>
[PublicAPI]
public sealed class Cleanup
{
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    // ReSharper disable once NotAccessedField.Local
    private Stream<UnitInternal>? stream;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Cleanup" /> class which runs
    ///     <paramref name="cleanup" /> when this object is garbage collected.
    /// </summary>
    /// <param name="cleanup">The action to run when this object becomes unreachable.</param>
    /// <remarks>
    ///     This is finalization and not disposal. The action runs at a time that the garbage
    ///     collector selects. To run it at a known time, call <c>CleanupNow</c>. For an object
    ///     that must be released immediately, use <see cref="System.IDisposable" />.
    /// </remarks>
    public Cleanup(Action cleanup)
    {
        Stream<UnitInternal> s = StreamInternal.NeverImpl<UnitInternal>();
        s.AttachListenerInternal(ListenerInternal.CreateFromAction(cleanup));

        this.stream = s;
    }

    internal void CleanupNowImpl() => this.stream = null;
}
