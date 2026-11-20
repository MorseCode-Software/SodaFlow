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
    ///     the same object added its state.
    /// </summary>
    public List<(ISymbol Member, int End, LoopState State)> Members { get; } = [];
}
