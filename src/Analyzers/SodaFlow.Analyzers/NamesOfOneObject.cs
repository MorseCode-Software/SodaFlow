using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The names that hold one object, as <c>d</c> and <c>a[0]</c> after
///     <c>a[0] = d</c>. One name got the object from a second name in a
///     straight line, or a write to a member went through the name. A sure
///     write through one of the names gives the member its state for each name.
/// </summary>
internal sealed class NamesOfOneObject
{
    /// <summary>
    ///     Each name, from the position where it got the object. A name that got
    ///     a different object after that position is no name of this object.
    /// </summary>
    public List<NameOfAnObject> Names { get; } = [];

    /// <summary>
    ///     The state of each member that a sure write through one of the names
    ///     gave, with the end of that write. Each write after it that can be to
    ///     the same object added its state. Accurate is true when no such write
    ///     came after it. At is the number of member writes of the region at the
    ///     last change of the state.
    /// </summary>
    public List<(ISymbol Member, int End, LoopState State, bool Accurate, int At)> Members { get; } = [];

    /// <summary>
    ///     The state of each member that a write through a second static field
    ///     gave after the last sure write through one of the names. That field
    ///     can hold the object of a static field of the names. A method can give
    ///     the one field the object of the other. A sure write through one of the
    ///     names removes the state of its member.
    /// </summary>
    /// <remarks>
    ///     A write that the frame did not see does not remove the states, as it
    ///     does for <see cref="Members" />. Such a write can only replace a
    ///     state. Thus, the record can keep a pending value that the object no
    ///     longer has, and a report is then on the careful side. Without the
    ///     state, a read can miss a pending value that the object has.
    /// </remarks>
    public List<(ISymbol Member, LoopState State)> ThroughASecondField { get; } = [];
}
