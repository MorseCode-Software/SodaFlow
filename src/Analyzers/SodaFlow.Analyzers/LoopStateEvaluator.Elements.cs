using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the state of a member of the element at a location that the walk can read again.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Records a write to a member for <see cref="WrittenAtItsElement" />.
    ///     A sure write through an element at a location that the walk can read
    ///     again gives that member of the location only the new state.
    /// </summary>
    /// <remarks>
    ///     Each other write to a member with the same name, through each owner,
    ///     can be a write to the same object. Thus, it adds its state. A copy of
    ///     a struct is no such owner. A write that this frame did not see can
    ///     write each member. Thus, it takes each record away.
    /// </remarks>
    /// <param name="written">The owner as the code has it at the write.</param>
    /// <param name="member">The member.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the write always runs in its function.</param>
    private void NotesAWriteAtAnElement(ExpressionSyntax? written, ISymbol member, LoopState state, bool replaces)
    {
        if (this.quiet)
        {
            return;
        }

        List<ElementMemberWrite> writes = this.Frame.ElementMemberWrites;

        if (this.Frame.MemberWritesSeen != this.region.MemberWrites - 1)
        {
            writes.Clear();
        }

        this.Frame.MemberWritesSeen = this.region.MemberWrites;

        if (writes.Count > 0
            && !(written is IdentifierNameSyntax && this.region.SymbolOf(written) is { } copy && HasACopy(copy)))
        {
            for (int i = 0; i < writes.Count; i++)
            {
                if (writes[i].Member.Name == member.Name)
                {
                    writes[i] = writes[i].Adding(state);
                }
            }
        }

        if (!replaces
            || written?.Parent is not MemberAccessExpressionSyntax { Parent: { } write }
            || this.ElementAt(written) is not { } at
            || FunctionOf(written) is not { } code
            || !this.KeepsTheElement(at: at, code: code, after: at.From, before: write.Span.End, use: written))
        {
            return;
        }

        ISymbol definition = member.OriginalDefinition;
        writes.RemoveAll(one => one.IsOf(collection: at.Collection, index: at.Index, member: definition));

        writes.Add(
            new ElementMemberWrite(
                collection: at.Collection,
                index: at.Index,
                member: definition,
                end: write.Span.End,
                state: state));
    }

    /// <summary>
    ///     Records that a local got an element at a location that the walk can
    ///     read again. The local is then a name of the object of that element. A
    ///     copy of a struct is no such name.
    /// </summary>
    private void NotesANameOfAnElement(ISymbol local, ExpressionSyntax value)
    {
        if (this.quiet)
        {
            return;
        }

        if (this.Frame.NamesOfElements.Count > 0)
        {
            this.Frame.NamesOfElements.Remove(local);
        }

        if (Unwrapped(value) is ElementAccessExpressionSyntax element
            && local is ILocalSymbol { Type.IsReferenceType: true } named
            && this.region.TypeOf(element) is { } type
            && this.region.ModelFor(element).Compilation.ClassifyCommonConversion(source: type, destination: named.Type)
                is { IsIdentity: true } or { IsReference: true, IsImplicit: true }
            && this.ElementKeyOf(element) != null)
        {
            this.Frame.NamesOfElements[local] = element;
        }
    }

    /// <summary>
    ///     The state of a read of a member through an element at a location that
    ///     the walk can read again, from <see cref="NotesAWriteAtAnElement" />.
    ///     Null when no record is for the read, or when code between the write
    ///     and the read can change the element.
    /// </summary>
    private LoopState? WrittenAtItsElement(MemberAccessExpressionSyntax read)
    {
        if (this.Frame.ElementMemberWrites.Count == 0
            || this.quiet
            || this.Frame.MemberWritesSeen != this.region.MemberWrites
            || this.ElementAt(read.Expression) is not { } at
            || this.region.SymbolOf(read) is not { } member
            || !IsAMemberLocation(member)
            || FunctionOf(read) is not { } code)
        {
            return null;
        }

        ISymbol definition = member.OriginalDefinition;

        return this.Frame.ElementMemberWrites.FirstOrDefault(write =>
                write.State is { Members: null, Objects.IsEmpty: true }
                && write.IsOf(collection: at.Collection, index: at.Index, member: definition)
                && this.KeepsTheElement(
                    at: at,
                    code: code,
                    after: Math.Min(val1: write.End, val2: at.From),
                    before: read.Expression.SpanStart,
                    use: read))
            is { } found
            ? found.State.Join(this.TypeState(read))
            : null;
    }

    // True for an element whose object the store does not know, or for a local that got such an
    // element. Also, this frame did not see the last write to a member. That write can be through a
    // second name of the object, such as a static field in a method. The region has its state for
    // the member. A write that this frame saw went to the state of the element also. An element of
    // a view is a second name of an element of the array. Thus, the frame can have seen the write
    // through the other name. A write through a local that got the element gives the local a copy
    // with members and no object. The local is a name of the element also after that write.
    private bool CanBeWrittenThroughAField(LoopState owner, ExpressionSyntax written) =>
        (owner.Objects.Contains(MadeObject.Unknown)
         && ((this.Frame.MemberWritesSeen != this.region.MemberWrites
              && (Unwrapped(written) is ElementAccessExpressionSyntax || this.ElementAt(written) != null))
             || (Unwrapped(written) is ElementAccessExpressionSyntax viewed && this.TheArrayOfAViewOf(viewed) != null)))
        || (owner.Objects.IsEmpty
            && this.Frame.MemberWritesSeen != this.region.MemberWrites
            && Unwrapped(written) is IdentifierNameSyntax
            && this.ElementAt(written) != null);

    // True for a member that is a location and no code: a field, or a property of this compilation
    // with no body. A property of a library has code that the walk does not read.
    private static bool IsAMemberLocation(ISymbol member) =>
        member is IFieldSymbol
        || (member is IPropertySymbol { DeclaringSyntaxReferences.IsEmpty: false } && IsALocation(member));

    // The location that an owner names: an element, or a local that got one. From is the end of
    // that element in the code. Null for each other owner.
    private (ISymbol Collection, object Index, ISymbol? Name, int From)? ElementAt(ExpressionSyntax owner)
    {
        ExpressionSyntax written = Unwrapped(owner);

        if (written is ElementAccessExpressionSyntax element)
        {
            return this.ElementKeyOf(element) is (var collection, var index)
                ? (collection, index, null, element.Span.End)
                : null;
        }

        if (written is not IdentifierNameSyntax)
        {
            return null;
        }

        // A name with ref has the location of its element. Each other local got a copy of the
        // reference to the object of its element.
        ElementAccessExpressionSyntax? named =
            this.TheLocationOf(written) is { } location
                ? Unwrapped(location) as ElementAccessExpressionSyntax
                : this.Frame.NamesOfElements.Count > 0
                  && this.region.SymbolOf(written) is ILocalSymbol local
                  && this.Frame.NamesOfElements.TryGetValue(key: local, value: out ElementAccessExpressionSyntax? got)
                    ? got
                    : null;

        return named != null
               && this.ElementKeyOf(named) is (var of, var at)
               && this.region.SymbolOf(written) is { } name
            ? (of, at, name, named.Span.End)
            : null;
    }

    // The collection and the index of an element at a location that the walk can read again. The
    // collection is a local or a parameter, and a read of the same index of it gives the same
    // element. The index is a constant, as a key of KeyOf, or a local.
    private (ISymbol Collection, object Index)? ElementKeyOf(ElementAccessExpressionSyntax element)
    {
        if (element.ArgumentList.Arguments.Count != 1
            || Unwrapped(element.Expression) is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not { } collection
            || !KeepsItsObject(collection)
            || !this.KeepsItsElements(collection))
        {
            return null;
        }

        ExpressionSyntax index = element.ArgumentList.Arguments[0].Expression;

        object? key =
            KeyOf(this.region.ModelFor(index).GetConstantValue(index))
            ?? (Unwrapped(index) is IdentifierNameSyntax
                && this.region.SymbolOf(index) is { } local and (ILocalSymbol or IParameterSymbol)
                && KeepsItsObject(local)
                    ? local
                    : null);

        return key == null ? null : (collection, key);
    }

    // True for a collection whose read of one index runs no code of the project and gives the
    // element that the last put there gave. It is an array with one dimension, a List, or a
    // Dictionary with the default comparer, which only a creation with no argument gives. The
    // local of such a dictionary must have no second value.
    private bool KeepsItsElements(ISymbol collection) =>
        (collection is ILocalSymbol local ? local.Type : ((IParameterSymbol)collection).Type) switch
        {
            IArrayTypeSymbol => true,
            INamedTypeSymbol { Name: "List", Arity: 1 } list => SodaFlowSymbols.IsPlatform(list),
            INamedTypeSymbol { Name: "Dictionary", TypeArguments: { Length: 2 } types } dictionary
                when SodaFlowSymbols.IsPlatform(dictionary) && IsATypeOfConstantKeys(types[0]) =>
                collection.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    is VariableDeclaratorSyntax
                    {
                        Initializer.Value: BaseObjectCreationExpressionSyntax
                        {
                            ArgumentList: null or { Arguments.Count: 0 }
                        }
                    } declarator
                && FunctionOf(declarator) is { } code
                && !this.UsesOf(name: collection, code: code)
                    .Any(one => one.Gives && (one.InAFunction || one.Position > declarator.Span.End)),
            _ => false
        };

    // Each use of a name in a function, as GetsAValue reads them. The frame keeps them for the
    // function of its code, thus the walk reads that function one time for each name.
    private ImmutableArray<(int Position, bool Gives, bool InAFunction)> UsesOf(ISymbol name, SyntaxNode code)
    {
        if (this.Frame.UsesOfNames.TryGetValue(
                key: name,
                value: out (SyntaxNode Code, ImmutableArray<(int Position, bool Gives, bool InAFunction)> Uses) known)
            && known.Code == code)
        {
            return known.Uses;
        }

        ImmutableArray<(int Position, bool Gives, bool InAFunction)> uses =
            code.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(use => use.Identifier.ValueText == name.Name
                              && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: name))
                .Select(use => (use.SpanStart, GivesAValue(use), FunctionOf(use) != code))
                .ToImmutableArray();

        this.Frame.UsesOfNames[name] = (code, uses);

        return uses;
    }

    // True when the element at a location is the same object at a use as at a position before it,
    // in one function. A loop or a label around the use can run the use again after code that
    // comes after it. Thus, no such statement can be around the use. A goto case goes to a case
    // label between the two, and the scan takes no label. No code between the two can give the
    // collection, the index, or the local a value. A function that uses one of them can run
    // between, thus no function can use them. Each node between must be one that
    // CannotChangeTheElement allows.
    private bool KeepsTheElement(
        (ISymbol Collection, object Index, ISymbol? Name, int From) at,
        SyntaxNode code,
        int after,
        int before,
        SyntaxNode use)
    {
        if (after > before
            || use.Ancestors()
                .TakeWhile(around => around != code)
                .Any(static around => around is ForStatementSyntax
                    or CommonForEachStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
                    or LabeledStatementSyntax))
        {
            return false;
        }

        if (new[] { at.Collection, at.Index as ISymbol, at.Name }.Any(part =>
                part != null
                && this.UsesOf(name: part, code: code)
                    .Any(one => one.InAFunction || (one.Gives && one.Position > after && one.Position < before))))
        {
            return false;
        }

        ITypeSymbol type = at.Collection is ILocalSymbol local ? local.Type : ((IParameterSymbol)at.Collection).Type;

        return code.DescendantNodes(TextSpan.FromBounds(start: after, end: before))
            .All(node => node.SpanStart < after
                         || node.Span.End > before
                         || this.CannotChangeTheElement(node: node, collection: type));
    }

    // True for a node that cannot change an element of a collection of the given type, and that
    // runs no code of the project. It is a write to a local or to a member that is a location. It
    // is a call that CannotChangeItsCollection allows, or a read of a name, a member, or an element
    // with no code. Statements, declarations, literals, and lambdas that it is part of are such
    // nodes also. Each other node is not, such as a creation, an operator, or a loop.
    private bool CannotChangeTheElement(SyntaxNode node, ITypeSymbol collection) =>
        node switch
        {
            InvocationExpressionSyntax call => this.CannotChangeItsCollection(call: call, collection: collection),
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment =>
                this.region.SymbolOf(assignment.Left) switch
                {
                    ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None } =>
                        assignment.Left is IdentifierNameSyntax,
                    { } member => assignment.Left is IdentifierNameSyntax or MemberAccessExpressionSyntax
                                  && IsAMemberLocation(member),
                    _ => false
                },
            ElementAccessExpressionSyntax element =>
                this.region.SymbolOf(element) == null
                || this.ElementKeyOf(element) != null
                || this.IsAViewElementOfAnUnknownObject(element),
            IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax =>
                this.region.SymbolOf(node) is not IPropertySymbol property
                || IsAMemberLocation(property)
                || (property is
                    {
                        DeclaringSyntaxReferences.IsEmpty: true, IsVirtual: false, IsAbstract: false, IsOverride: false
                    }
                    && !SodaFlowSymbols.IsLazyValue(property)
                    && !(this.region.Symbols.IsSodaFlow(property)
                         && SodaFlowSymbols.ReadMembers.Contains(property.Name))),
            ArgumentSyntax argument => argument.RefKindKeyword.IsKind(SyntaxKind.None),
            LocalDeclarationStatementSyntax declaration => declaration.UsingKeyword.IsKind(SyntaxKind.None),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression),
            ExpressionStatementSyntax
                or BlockSyntax
                or EmptyStatementSyntax
                or VariableDeclarationSyntax
                or VariableDeclaratorSyntax
                or EqualsValueClauseSyntax
                or ArgumentListSyntax
                or BracketedArgumentListSyntax
                or LiteralExpressionSyntax
                or ParenthesizedExpressionSyntax
                or ThisExpressionSyntax
                or PredefinedTypeSyntax
                or TypeArgumentListSyntax
                or QualifiedNameSyntax
                or ArrayTypeSyntax
                or ArrayRankSpecifierSyntax
                or OmittedArraySizeExpressionSyntax
                or NullableTypeSyntax
                or SimpleLambdaExpressionSyntax
                or ParenthesizedLambdaExpressionSyntax
                or ParameterListSyntax
                or ParameterSyntax => true,
            _ => false
        };

    // True for a read of an element of a view of a local array, whose object the store does not
    // know. The read does not change the element. A write to a member through it adds its state to
    // the record of the element of the array. The store has each write to an object that it knows,
    // and that answer is more accurate. Thus, only an object that the store does not know passes.
    private bool IsAViewElementOfAnUnknownObject(ElementAccessExpressionSyntax element) =>
        this.TheArrayOfAViewOf(element) != null && this.Eval(element).Objects.Contains(MadeObject.Unknown);

    // True for a call that cannot change an element of a collection of the given type, and that
    // runs no code of the project. The method has no body to read and no override. A method of
    // the platform calls no member of an object for it: see PlatformCallers. A method of SodaFlow
    // reads no cell and sends nothing. The call cannot get the collection or a view of it, and it
    // gets no function other than a lambda that the code writes there. RunSynchronously runs the
    // function of its task.
    private bool CannotChangeItsCollection(InvocationExpressionSyntax call, ITypeSymbol collection)
    {
        if (this.region.SymbolOf(call) is not IMethodSymbol method
            || !(method.ReducedFrom ?? method).DeclaringSyntaxReferences.IsEmpty
            || method.MethodKind is not (MethodKind.Ordinary or MethodKind.ReducedExtension)
            || method.IsVirtual
            || method.IsAbstract
            || method.IsOverride
            || IsRunSynchronously(method)
            || (this.region.Symbols.IsSodaFlow(method)
                ? method.Name == "Send" || SodaFlowSymbols.ReadMembers.Contains(method.Name)
                : !SodaFlowSymbols.IsPlatform(method) || PlatformCallers.Of(method) != PlatformCallbacks.None))
        {
            return false;
        }

        ExpressionSyntax? receiver =
            method is { IsStatic: true, MethodKind: MethodKind.Ordinary }
                ? null
                : (call.Expression as MemberAccessExpressionSyntax)?.Expression;

        return (receiver == null || this.CannotBeTheCollection(value: receiver, collection: collection))
               && call.ArgumentList.Arguments.All(argument =>
                   Unwrapped(argument.Expression) is AnonymousFunctionExpressionSyntax
                   || (this.region.TypeOf(argument.Expression) is not { TypeKind: TypeKind.Delegate }
                       && this.CannotBeTheCollection(value: argument.Expression, collection: collection)));
    }

    // True for a value that cannot be a collection of the given type, or a view of its elements. No
    // conversion goes between the two types, and the value is not a span, a memory, or a segment of
    // an array. A value with no type is not such a value.
    private bool CannotBeTheCollection(ExpressionSyntax value, ITypeSymbol collection) =>
        this.region.TypeOf(value) is { } type and not (ITypeParameterSymbol or { IsRefLikeType: true })
        && !(type is INamedTypeSymbol { Name: "Memory" or "ReadOnlyMemory" or "ArraySegment" }
             && SodaFlowSymbols.IsPlatform(type))
        && !this.region.ModelFor(value).Compilation.ClassifyCommonConversion(source: collection, destination: type)
            .Exists;
}
