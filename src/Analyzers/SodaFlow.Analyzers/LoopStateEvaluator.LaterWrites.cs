using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a write to a member through a second name of an object. The write comes after the
// last write through the name that the code reads.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Records a write to a member through a name of this code, for
    ///     <see cref="WrittenAfterItsOwner" />.
    /// </summary>
    private void NotesAnOwnWrite(ExpressionSyntax? written)
    {
        if (!this.quiet
            && written != null
            && this.RootOf(written) is { } root
            && this.region.SymbolOf(root) is (ILocalSymbol or IParameterSymbol) and { } name)
        {
            this.NotesAChangeTo(name);
        }
    }

    // Records a change to what a local or a parameter holds, for WrittenAfterItsOwner.
    private void NotesAChangeTo(ISymbol name)
    {
        if (!this.quiet)
        {
            this.Frame.OwnWriteOf[name] = this.region.MemberWrites;
        }
    }

    /// <summary>
    ///     Records that an assignment to a location makes it a second name of the
    ///     value of a name, for <see cref="WrittenAfterItsOwner" />.
    /// </summary>
    /// <param name="location">The target of the assignment.</param>
    /// <param name="value">The expression of the value.</param>
    /// <param name="replaces">True when the location then has only this value.</param>
    public void NotesALinkOfALocation(SyntaxNode location, ExpressionSyntax value, bool replaces)
    {
        if (location is ElementAccessExpressionSyntax or MemberAccessExpressionSyntax or IdentifierNameSyntax
            && this.region.SymbolOf(location) is not (ILocalSymbol or IParameterSymbol)
            && this.NameKeyOf((ExpressionSyntax)location) is { } name)
        {
            this.NotesALink(name: name, value: value, replaces: replaces);
        }
    }

    // Records that a name gets the value of a second name. A sure assignment takes away the links
    // of the name first. A function can run at a different time, thus its code makes no link.
    private void NotesALink(NameKey name, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet || this.function != null)
        {
            return;
        }

        if (replaces)
        {
            this.region.TakesAwayTheLinksOf(name);
        }

        if (this.region.TypeOf(value) is { } type && this.CanBeAnObject(type) && this.NameKeyOf(value) is { } from)
        {
            this.region.Links(
                new NameLink(one: name, other: from, code: FunctionOf(value), position: value.Span.End));
        }
    }

    // A name that the walk can read again: a local or a parameter, a field, or an element at a
    // constant index. An element of a local that got the array of a field is an element of the
    // field.
    private NameKey? NameKeyOf(ExpressionSyntax? name)
    {
        if (name == null)
        {
            return null;
        }

        return Unwrapped(name) switch
        {
            IdentifierNameSyntax at when this.region.SymbolOf(at) is (ILocalSymbol or IParameterSymbol) and { } local =>
                KeepsItsObject(local) ? new NameKey(of: local, owner: null, index: -1) : null,
            // An element at an index that a local keeps has that local as its owner.
            ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } element
                when Unwrapped(arguments[0].Expression) is IdentifierNameSyntax index
                     && this.region.SymbolOf(index) is ILocalSymbol { RefKind: RefKind.None } kept
                     && Unwrapped(element.Expression) is IdentifierNameSyntax array
                     && this.region.SymbolOf(array) is { } collection
                     && (collection is IFieldSymbol || KeepsItsObject(collection)) =>
                new NameKey(of: collection, owner: kept, index: -2),
            var at => this.LocationKeyOf(at) switch
            {
                null => null,
                { Index: >= 0, Of: ILocalSymbol local } key
                    when this.TheFieldThatItNames(local) is { } named
                         && this.region.SymbolOf(named) is IFieldSymbol field =>
                    new NameKey(of: field, owner: null, index: key.Index),
                { } key => new NameKey(of: key.Of, owner: key.Owner, index: key.Index)
            }
        };
    }

    /// <summary>
    ///     The state that the region has for a member, or none. The region gives
    ///     it when a write through a second name of the owner comes after the
    ///     last write through the name of the owner.
    /// </summary>
    /// <remarks>
    ///     An assignment from one name to the other makes the two names of one
    ///     value: see <see cref="NotesALink" />. An object of the store has the
    ///     write in the store when the write was through a name of that object.
    ///     Thus, only a write through a different object counts for it.
    /// </remarks>
    private LoopState WrittenAfterItsOwner(ExpressionSyntax owner, LoopState has, ISymbol member, LoopState field)
    {
        if (field.IsNone || this.region.WritesThroughNamesOf(member) is not { Count: > 0 } writes)
        {
            return LoopState.None;
        }

        int own = this.LastOwnWriteOf(owner: owner, level: 0);

        if (writes[writes.Count - 1].At <= own || this.NameKeyOf(owner) is not { } name)
        {
            return LoopState.None;
        }

        bool isKnown = !has.Objects.IsEmpty && !has.Objects.Contains(MadeObject.Unknown);

        return writes.Any(write => write.At > own
                                   && (!isKnown || !has.Objects.Any(write.Objects.Contains))
                                   && this.AreLinked(one: name, other: write.Name, use: owner))
            ? field
            : LoopState.None;
    }

    // True when a chain of links goes from one name to the other at a use. A link of the code of the
    // use holds when no name of the link got a different value after it. Each name has a link to
    // itself.
    private bool AreLinked(NameKey one, NameKey other, SyntaxNode use)
    {
        List<NameKey> reached = [one];
        SyntaxNode? code = FunctionOf(use);

        for (int i = 0; i < reached.Count; i++)
        {
            if (reached[i].IsTheSameAs(other))
            {
                return true;
            }

            foreach (NameLink link in this.region.AllLinks)
            {
                NameKey? next =
                    link.One.IsTheSameAs(reached[i])
                        ? link.Other
                        : link.Other.IsTheSameAs(reached[i])
                            ? link.One
                            : null;

                if (next is { } found
                    && !reached.Any(known => known.IsTheSameAs(found))
                    && (link.Code != code
                        || code == null
                        || (this.KeepsItsValue(name: link.One, code: code, after: link.Position, before: use.SpanStart)
                            && this.KeepsItsValue(
                                name: link.Other,
                                code: code,
                                after: link.Position,
                                before: use.SpanStart))))
                {
                    reached.Add(found);
                }
            }
        }

        return false;
    }

    // True when no code between two positions can give a name a different value. A call that does
    // not get the array or the owner keeps a location of a field, as WithItsField has it. A call
    // that gets it keeps it also when CanStandBetween permits the code of the callee.
    private bool KeepsItsValue(NameKey name, SyntaxNode code, int after, int before) =>
        new[] { name.Of, name.Owner }.All(part =>
            part is not (ILocalSymbol or IParameterSymbol)
            || !this.GetsAValue(name: part, code: code, after: after, before: before, orAFunctionUsesIt: true))
        && (name is { Index: -1, Owner: null, Of: ILocalSymbol or IParameterSymbol }
            || !code.DescendantNodes(TextSpan.FromBounds(start: after, end: before))
                .Any(node => node.SpanStart > after
                             && node.SpanStart < before
                             && this.CanChangeTheLocation(
                                 node: node,
                                 key: (name.Of, name.Owner, name.Index),
                                 name: name.Of,
                                 aCallCounts: false)
                             && !this.ChangesNoLocationOf(node: node, name: name)));

    // True for a call of a method of this compilation that CanStandBetween permits, for the type of
    // the collection or the owner of a name.
    private bool ChangesNoLocationOf(SyntaxNode node, NameKey name) =>
        node is InvocationExpressionSyntax call
        && this.region.SymbolOf(call) is IMethodSymbol
        {
            MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension,
            IsVirtual: false,
            IsAbstract: false,
            IsOverride: false
        } method
        && (name.Owner ?? name.Of) switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            _ => null
        } is { } type
        && this.CanStandBetween(
            method: method.ReducedFrom ?? method,
            collection: type,
            callees: MaxCalleesBetween,
            call: call,
            callee: null);

    // The number of the last write to a member through the name that an owner starts with. A local
    // can be a second name of a value. Then, a write through each other name of it counts also.
    private int LastOwnWriteOf(ExpressionSyntax owner, int level)
    {
        if (this.RootOf(owner) is not { } root)
        {
            return int.MaxValue;
        }

        switch (this.region.SymbolOf(root))
        {
            case (IFieldSymbol or IPropertySymbol) and { } field:
                return this.region.LastWriteOf(field);
            case (ILocalSymbol or IParameterSymbol) and { } name:
            {
                int last = this.Frame.OwnWriteOf.TryGetValue(key: name, value: out int at) ? at : -1;

                if (level >= MaxOwners)
                {
                    return last;
                }

                ExpressionSyntax?[] others =
                [
                    this.TheLocationOf(root) ?? this.TheFirstNameOf(root),
                    this.Frame.NamesOfElements.TryGetValue(key: name, value: out ElementAccessExpressionSyntax? element)
                        ? element
                        : null,
                    this.TheFieldThatItNames(name)
                ];

                return others.OfType<ExpressionSyntax>()
                    .Aggregate(
                        seed: last,
                        func: (most, other) =>
                            Math.Max(val1: most, val2: this.LastOwnWriteOf(owner: other, level: level + 1)));
            }
            default:
                return int.MaxValue;
        }
    }

    // The local, the parameter, or the field that an owner starts with. The owner can be an
    // element of it or an instance member of it.
    private ExpressionSyntax? RootOf(ExpressionSyntax owner)
    {
        for (ExpressionSyntax at = Unwrapped(owner);;)
        {
            switch (at)
            {
                case ElementAccessExpressionSyntax element:
                    at = Unwrapped(element.Expression);

                    continue;
                case MemberAccessExpressionSyntax access
                    when this.region.SymbolOf(access) is not { IsStatic: true }:
                    at = Unwrapped(access.Expression);

                    continue;
                case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                    return at;
                default:
                    return null;
            }
        }
    }
}
