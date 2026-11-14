using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The writes to a member through the variable of a foreach statement, for
///     the time that the walk is in the statement.
/// </summary>
/// <remarks>
///     The walk reads the body of the statement one or more times. The body
///     gives each element one value when each read has one write to a member,
///     and that write is through the variable.
/// </remarks>
/// <param name="elements">The expression of each element of the collection, at the start of the statement.</param>
/// <param name="start">The number of writes to a member in the region, at the start of the statement.</param>
/// <param name="member">The member of the last write. Null before the first write.</param>
/// <param name="state">The state of the value of each write, together.</param>
/// <param name="same">False after a write to a second member, or to a member that is not a location.</param>
/// <param name="visits">The number of writes through the variable.</param>
/// <param name="last">The number of writes to a member in the region, after the last write through the variable.</param>
internal sealed class WritesThroughEach(
    in IReadOnlyList<ExpressionSyntax> elements,
    in int start,
    in ISymbol? member,
    in LoopState state,
    in bool same,
    in int visits,
    in int last)
{
    private readonly int last = last;
    private readonly bool same = same;
    private readonly int start = start;
    private readonly int visits = visits;

    public IReadOnlyList<ExpressionSyntax> Elements { get; } = elements;

    public ISymbol? Member { get; } = member;

    public LoopState State { get; } = state;

    /// <summary>The same record after one more write through the variable.</summary>
    /// <param name="written">The member.</param>
    /// <param name="value">The state of the value.</param>
    /// <param name="isALocation">True for a member that a read gives the last write of.</param>
    /// <param name="writes">The number of writes to a member in the region, after the write.</param>
    public WritesThroughEach With(ISymbol written, LoopState value, bool isALocation, int writes) =>
        new(
            elements: this.Elements,
            start: this.start,
            member: written,
            state: this.State.Join(value),
            same: this.same
                  && isALocation
                  && (this.Member == null || SymbolEqualityComparer.Default.Equals(x: this.Member, y: written)),
            visits: this.visits + 1,
            last: writes);

    /// <summary>
    ///     True when each write to a member, from the start of the statement to
    ///     now, is a write through the variable to one member.
    /// </summary>
    /// <param name="writes">The number of writes to a member in the region now.</param>
    public bool IsTheOnlyWrite(int writes) =>
        this.same && this.visits > 0 && this.last == this.start + this.visits && this.last == writes;
}
