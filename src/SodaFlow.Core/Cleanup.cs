using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     An object which allows for arbitrary cleanup code to safely run when this object is garbage collected.
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
    ///     This is finalization, not deterministic disposal: the action runs eventually, at a time
    ///     the collector chooses. Call <c>CleanupNow</c> to run it at
    ///     a known moment instead, and prefer <see cref="System.IDisposable" /> for anything that
    ///     must be released promptly.
    /// </remarks>
    public Cleanup(Action cleanup)
    {
        Stream<UnitInternal> s = StreamInternal.NeverImpl<UnitInternal>();
        s.AttachListenerImpl(ListenerInternal.CreateFromAction(cleanup));

        this.stream = s;
    }

    internal void CleanupNowImpl() => this.stream = null;
}
