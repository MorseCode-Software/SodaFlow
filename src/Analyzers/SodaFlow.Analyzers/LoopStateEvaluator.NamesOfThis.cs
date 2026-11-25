using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a second name of the object that the code is in. Such a name is a local, an element, or
// a field that got this.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Records a local or a location that gets the object that the code is
    ///     in. A read or a write of a member through it is then a read or a
    ///     write through this. That is true while no code gives it a different
    ///     value.
    /// </summary>
    /// <param name="local">The local, or null for a location.</param>
    /// <param name="location">The expression of the location, or null for a local.</param>
    /// <param name="value">The expression of the value.</param>
    /// <param name="replaces">True when the target then has only this value.</param>
    public void NotesANameOfThis(ISymbol? local, ExpressionSyntax? location, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet || FunctionOf(value) is not { } code)
        {
            return;
        }

        // A local or a location that gets a different value is no name of this from that time.
        if (this.Frame.NamesOfThis.Count > 0)
        {
            NameKey? given = location == null ? null : this.ThisKeyOf(location);

            this.Frame.NamesOfThis.RemoveAll(one =>
                (local != null && SymbolEqualityComparer.Default.Equals(x: one.Local, y: local))
                || (given is { } name && one.Key is { } key && key.IsTheSameAs(name)));
        }

        if (!replaces || this.function != null)
        {
            return;
        }

        ExpressionSyntax got = Unwrapped(value);

        if (got is ThisExpressionSyntax || this.IsANameOfThis(got))
        {
            if (local is ILocalSymbol { RefKind: RefKind.None })
            {
                this.Frame.NamesOfThis.Add((local, null, code, value.Span.End));
            }
            else if (location != null && this.ThisKeyOf(location) is { } key)
            {
                this.Frame.NamesOfThis.Add((null, key, code, value.Span.End));
            }

            return;
        }

        // An element of a literal, as in [this], in the declaration of a local.
        if (local is ILocalSymbol { RefKind: RefKind.None }
            && got is CollectionExpressionSyntax { Elements: var elements })
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] is ExpressionElementSyntax { Expression: var one }
                    && Unwrapped(one) is ThisExpressionSyntax)
                {
                    NameKey element = new(of: local, owner: null, index: i);
                    this.Frame.NamesOfThis.Add((null, element, code, value.Span.End));
                }
            }
        }
    }

    // True for an owner that is a name of the object that the code is in, from NotesANameOfThis.
    private bool IsANameOfThis(ExpressionSyntax owner)
    {
        if (this.Frame.NamesOfThis.Count == 0 || FunctionOf(owner) is not { } code)
        {
            return false;
        }

        ExpressionSyntax name = Unwrapped(owner);
        ISymbol? local = name is IdentifierNameSyntax ? this.region.SymbolOf(name) : null;
        NameKey? key = local is ILocalSymbol ? null : this.ThisKeyOf(name);

        return this.Frame.NamesOfThis.Any(one =>
            one.Code == code
            && one.From <= owner.SpanStart
            && (one.Local != null
                ? SymbolEqualityComparer.Default.Equals(x: one.Local, y: local)
                  && !this.GetsAValue(
                      name: one.Local,
                      code: code,
                      after: one.From,
                      before: owner.SpanStart,
                      orAFunctionUsesIt: false)
                : key is { } read
                  && one.Key is { } kept
                  && kept.IsTheSameAs(read)
                  && this.KeepsTheLocation(key: read, code: code, after: one.From, before: owner.SpanStart)));
    }

    // True when no code between two positions can give a location a different value. A call that gets
    // a collection can change its element at a key that is no number. Thus, such an element has the
    // parts of an element at a number.
    private bool KeepsTheLocation(NameKey key, SyntaxNode code, int after, int before) =>
        new[] { key.Of, key.Owner }.All(part =>
            part is not (ILocalSymbol or IParameterSymbol)
            || !this.GetsAValue(name: part, code: code, after: after, before: before, orAFunctionUsesIt: true))
        && !code.DescendantNodes(TextSpan.FromBounds(start: after, end: before))
            .Any(node => node.SpanStart >= after
                         && node.SpanStart < before
                         && this.CanChangeTheLocation(
                             node: node,
                             key: (key.Of, key.Owner, key.Index == -3 ? 0 : key.Index),
                             name: key.Of,
                             aCallCounts: true));

    // The state of a member of the object that the code is in, as a read through this gives it. A
    // read through this or through a field gets the member as the origin of a write through a name.
    // A read in a function gets the origin of the value, as a method of this that the function calls
    // does.
    // The region has each write to the member through an owner that can be this object. A sure write
    // through this replaces each such write before it.
    private LoopState OfThisObject(ISymbol member, LoopState field, ExpressionSyntax read, bool throughThisOrAField) =>
        (throughThisOrAField
         && this.function == null
         && this.Frame.WrittenThroughANameOfThis.TryGetValue(
             key: member.OriginalDefinition,
             value: out LoopState? named)
            ? named
            : this.thisMembers.TryGetValue(key: member.OriginalDefinition, value: out LoopState? own)
                ? own
                : this.OfTheImplementationsOnThis(member))
        .Join(
            !field.IsNone
            && this.Frame.OwnWriteOf.TryGetValue(key: member.OriginalDefinition, value: out int replaced)
            && this.region.LastWriteOf(member) <= replaced
                ? LoopState.None
                : field)
        .Join(this.TypeState(read));
}
