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
    ///     How many calls deep <see cref="CanStandBetween" /> reads the code of a
    ///     method. A deeper call counts as code that can change the element. The
    ///     result is then a report and not a miss.
    /// </summary>
    private const int MaxCalleesBetween = 2;

    /// <summary>
    ///     Records a write to a member for <see cref="WrittenAtItsElement" />.
    ///     A sure write through an element at a location that the walk can read
    ///     again gives that member of the location only the new state.
    /// </summary>
    /// <remarks>
    ///     Each other write to a member with the same name, through each owner,
    ///     can be a write to the same object. Thus, it adds its state. A copy of
    ///     a struct is no such owner. A write that this frame did not see can
    ///     write each member. Thus, it takes each record away. A write that the
    ///     store makes sure for the object of a record replaces its state. The
    ///     read uses the record only while the element keeps that object.
    /// </remarks>
    /// <param name="written">The owner as the code has it at the write.</param>
    /// <param name="member">The member.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the write always runs in its function.</param>
    /// <param name="has">The state of the owner.</param>
    /// <param name="isKnown">False when the owner can be an object that the store does not know.</param>
    /// <param name="turn">The body of the loop whose each turn always runs the write, or null.</param>
    private void NotesAWriteAtAnElement(
        ExpressionSyntax? written,
        ISymbol member,
        LoopState state,
        bool replaces,
        LoopState has,
        bool isKnown,
        SyntaxNode? turn)
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
            // The same test as the one of Change for the object of the store.
            MadeObject? surely =
                isKnown
                && this.TheOneObjectIn(has) is { } one
                && this.IsSureFor(made: one, replaces: replaces, turn: turn)
                    ? one
                    : null;

            for (int i = 0; i < writes.Count; i++)
            {
                if (writes[i].Member.Name == member.Name)
                {
                    writes[i] =
                        surely != null
                        && writes[i].Object is { } recorded
                        && this.region.Objects.Now(ImmutableArray.Create(recorded)) is { Length: 1 } now
                        && now[0] == surely
                            ? writes[i].With(state)
                            : writes[i].Adding(state);
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
                state: state,
                one: isKnown ? this.TheOneObjectIn(has) : null));
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
    /// <remarks>
    ///     A local that got the array or the list of a field also gets the state
    ///     of the field at a read: see WithItsField. That state keeps each write
    ///     through the field, also a write that this record replaced. Thus, for
    ///     such a local, a call of code of this compilation can come between,
    ///     when <see cref="CanStandBetween" /> permits its code.
    /// </remarks>
    private LoopState? WrittenAtItsElement(MemberAccessExpressionSyntax read)
    {
        if (this.Frame.ElementMemberWrites.Count == 0
            || this.quiet
            || this.Frame.MemberWritesSeen != this.region.MemberWrites
            || this.ElementAt(read.Expression) is not { } at
            || this.region.SymbolOf(read) is not { } member
            || !this.IsAMemberLocation(member)
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
                    use: read,
                    callees: this.TheFieldThatItNames(at.Collection) != null ? MaxCalleesBetween : 0))
            is { } found
            ? found.State.Join(this.TypeState(read))
            : null;
    }

    // True for an element whose object the store does not know, or for a local that got such an
    // element. Also, this frame did not see the last write to a member. That write can be through a
    // second name of the object, such as a static field in a method. The region has its state for
    // the member. A write that this frame saw went to the state of the element also. An element of
    // a view is a second name of an element of the array. Thus, the write that the frame saw can be a
    // write through the other name. A write through a local that got the element gives the local a
    // copy with members and no object. The local is a name of the element also after that write.
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
    private bool IsAMemberLocation(ISymbol member) =>
        member is IFieldSymbol
        || (member is IPropertySymbol { DeclaringSyntaxReferences.IsEmpty: false } && this.IsALocation(member));

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
    // element. The index is a constant, as a key of KeyOf, or a local. A second local of a
    // collection is the first: see TheCollectionIn.
    private (ISymbol Collection, object Index)? ElementKeyOf(ElementAccessExpressionSyntax element)
    {
        if (element.ArgumentList.Arguments.Count != 1
            || Unwrapped(element.Expression) is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not { } named)
        {
            return null;
        }

        ISymbol collection = this.TheCollectionIn(named);

        if (!KeepsItsObject(collection) || !this.KeepsItsElements(collection))
        {
            return null;
        }

        ExpressionSyntax index = element.ArgumentList.Arguments[0].Expression;

        object? key =
            KeyOf(this.region.ModelFor(index).GetConstantValue(index))
            ?? (Unwrapped(index) is IdentifierNameSyntax
                && this.region.SymbolOf(index) is { } local and (ILocalSymbol or IParameterSymbol)
                && KeepsItsObject(local)
                    ? this.ConstantOfALocal(local) ?? local
                    : null);

        return key == null ? null : (collection, key);
    }

    // The key of the constant that a local gets in its declaration, when it gets no other value. An
    // index in such a local is the same index as that constant.
    private object? ConstantOfALocal(ISymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax { Initializer.Value: { } initial } declarator
        && FunctionOf(declarator) is { } code
        && KeyOf(this.region.ModelFor(initial).GetConstantValue(initial)) is { } key
        && this.HasOneValueIn(name: local, code: code)
            ? key
            : null;

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
    // CannotChangeTheElement allows. The last argument is how many calls deep the scan reads code
    // of this compilation: see CanStandBetween.
    private bool KeepsTheElement(
        (ISymbol Collection, object Index, ISymbol? Name, int From) at,
        SyntaxNode code,
        int after,
        int before,
        SyntaxNode use,
        int callees = 0)
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
                         || this.CannotChangeTheElement(node: node, collection: type, callees: callees));
    }

    // True for a node that cannot change an element of a collection of the given type, and that runs
    // no code of the project. It is a write to a local, to a discard, or to a member that is a
    // location. It is a call that CannotChangeItsCollection allows, or a read of a name, a member, or
    // an element with no code. A read of a cell or of a Lazy runs only the functions that the walk
    // follows. Only those can change the element: see RunsNoFunctionThatTheWalkKnows. An operator of
    // the platform on two numbers runs no code. Statements, declarations, literals, lambdas, and
    // conditional expressions that it is part of are such nodes also. The scan reads each branch of
    // such an expression. Each other node is not, such as a creation or a loop. The last argument is
    // the call whose body the node is in, for a node of a callee.
    private bool CannotChangeTheElement(
        SyntaxNode node,
        ITypeSymbol collection,
        int callees,
        (InvocationExpressionSyntax Call, IMethodSymbol Method, bool First)? callee = null) =>
        node switch
        {
            InvocationExpressionSyntax call =>
                this.CannotChangeItsCollection(call: call, collection: collection, callees: callees, callee: callee),
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment =>
                this.region.SymbolOf(assignment.Left) switch
                {
                    ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None } =>
                        assignment.Left is IdentifierNameSyntax,
                    // A discard keeps no value. The creation or the call at its right is a node of its own.
                    IDiscardSymbol => true,
                    { } member => assignment.Left is IdentifierNameSyntax or MemberAccessExpressionSyntax
                                  && this.IsAMemberLocation(member),
                    _ => false
                },
            ElementAccessExpressionSyntax element =>
                this.region.SymbolOf(element) == null
                || this.ElementKeyOf(element) != null
                || this.IsAViewElementOfAnUnknownObject(element),
            IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax =>
                this.region.SymbolOf(node) is not IPropertySymbol property
                || this.IsAMemberLocation(property)
                || this.TheFieldBehind(property) != null
                || (property.DeclaringSyntaxReferences.IsEmpty
                    && (SodaFlowSymbols.IsLazyValue(property) || this.region.Symbols.ReadsItsOwner(property)
                        ? this.RunsNoFunctionThatTheWalkKnows(owner: OwnerOfAProperty(node), callee: callee)
                        : property is { IsVirtual: false, IsAbstract: false, IsOverride: false })),
            BinaryExpressionSyntax binary =>
                this.region.SymbolOf(binary) is IMethodSymbol
                {
                    MethodKind: MethodKind.BuiltinOperator,
                    ContainingType.SpecialType: >= SpecialType.System_Boolean and <= SpecialType.System_Double
                },
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
                or ConditionalExpressionSyntax
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
    // runs no code of the project. A method of this compilation passes only where CanStandBetween
    // permits its code. The method has no override. A method with no body to read is of the
    // platform or SodaFlow. A method of the platform calls no member of an object for it: see
    // PlatformCallers. A method of SodaFlow runs no code of the project, but for a read of a cell
    // with a function that the walk knows. The call cannot get the collection or a view of it, and
    // it gets no function other than a lambda that the code writes there. A literal is no
    // collection, and a new object that gets no argument is no collection also. RunSynchronously
    // runs the function of its task.
    private bool CannotChangeItsCollection(
        InvocationExpressionSyntax call,
        ITypeSymbol collection,
        int callees,
        (InvocationExpressionSyntax Call, IMethodSymbol Method, bool First)? callee)
    {
        if (this.region.SymbolOf(call) is not IMethodSymbol method)
        {
            return false;
        }

        ExpressionSyntax? receiver =
            method is { IsStatic: true, MethodKind: MethodKind.Ordinary }
                ? null
                : (call.Expression as MemberAccessExpressionSyntax)?.Expression;

        // A read of a cell of SodaFlow runs the functions of the cell. CanExecute of a bindable action
        // is a member of ICommand, and reads the enablement cell. A send runs no code of the project
        // before the close.
        bool readsACell =
            SodaFlowSymbols.ReadMembers.Contains(method.Name)
            && (this.region.Symbols.IsSodaFlow(method)
                || (receiver != null && this.region.Symbols.IsBindableAction(this.region.TypeOf(receiver))));

        if (readsACell
                ? !this.RunsNoFunctionThatTheWalkKnows(owner: receiver, callee: callee)
                : method is not
                  {
                      MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension,
                      IsVirtual: false,
                      IsAbstract: false,
                      IsOverride: false
                  }
                  || ((method.ReducedFrom ?? method).DeclaringSyntaxReferences.IsEmpty
                      ? IsRunSynchronously(method)
                        || (!this.region.Symbols.IsSodaFlow(method)
                            && (!SodaFlowSymbols.IsPlatform(method)
                                || PlatformCallers.Of(method) != PlatformCallbacks.None))
                      : !this.CanStandBetween(
                          method: method,
                          collection: collection,
                          callees: callees,
                          call: call,
                          callee: callee)))
        {
            return false;
        }

        return (receiver == null || this.CannotBeTheCollection(value: receiver, collection: collection))
               && call.ArgumentList.Arguments.All(argument =>
                   Unwrapped(argument.Expression)
                       is AnonymousFunctionExpressionSyntax
                       or LiteralExpressionSyntax
                       or BaseObjectCreationExpressionSyntax { ArgumentList: null or { Arguments.Count: 0 } }
                   || (this.region.TypeOf(argument.Expression) is not { TypeKind: TypeKind.Delegate }
                       && this.CannotBeTheCollection(value: argument.Expression, collection: collection)));
    }

    // True for a method of this compilation whose body can come between a write through an
    // element and a read itself. Then, a call of it changes the element no more than its code does
    // at the call. A call in that body goes one more call deep. The only element in the body is an
    // element of an array, with no code. The other tests of an element read the frame of this
    // code, and the body has no part in that frame.
    private bool CanStandBetween(
        IMethodSymbol method,
        ITypeSymbol collection,
        int callees,
        InvocationExpressionSyntax call,
        (InvocationExpressionSyntax Call, IMethodSymbol Method, bool First)? callee) =>
        callees > 0
        && (method.ReducedFrom ?? method).OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is MethodDeclarationSyntax declaration
        && (declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody?.Expression) is { } body
        && body.DescendantNodesAndSelf()
            .All(node => node is ElementAccessExpressionSyntax element
                ? this.region.SymbolOf(element) == null
                : this.CannotChangeTheElement(
                    node: node,
                    collection: collection,
                    callees: callees - 1,
                    callee: (call, method, First: callee == null)));

    // True for a constructor of this compilation whose body can come between, as CanStandBetween
    // says for a method. The creation must run only that body. Thus, the constructor calls no
    // second constructor, and the type gives no member that is not static an initializer. The body
    // has no callee record, thus a read of a parameter there passes no test that needs the argument.
    private bool ConstructorCanStandBetween(IMethodSymbol constructor, ITypeSymbol collection, int callees) =>
        constructor.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is ConstructorDeclarationSyntax { Initializer: null } declaration
        && HasNoInitializers(constructor.ContainingType)
        && (declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody?.Expression) is { } body
        && body.DescendantNodesAndSelf()
            .All(node => node is ElementAccessExpressionSyntax element
                ? this.region.SymbolOf(element) == null
                : this.CannotChangeTheElement(node: node, collection: collection, callees: callees - 1));

    // True for a type whose creation runs no code before the body of its constructor. Its base type
    // has no code, and it gives no member that is not static an initializer.
    private static bool HasNoInitializers(INamedTypeSymbol type) =>
        type.BaseType is null or { SpecialType: SpecialType.System_Object or SpecialType.System_ValueType }
        && type.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(static declared => declared.Members)
            .All(static member => member switch
            {
                FieldDeclarationSyntax field =>
                    field.Modifiers.Any(SyntaxKind.StaticKeyword)
                    || field.Declaration.Variables.All(static one => one.Initializer == null),
                PropertyDeclarationSyntax property =>
                    property.Modifiers.Any(SyntaxKind.StaticKeyword) || property.Initializer == null,
                _ => true
            });

    // The owner of a property that a node reads: the owner of a member access, or of the member
    // access whose name the node is. Null for a property with no owner in the code.
    private static ExpressionSyntax? OwnerOfAProperty(SyntaxNode node) =>
        node switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            _ when node.Parent is MemberAccessExpressionSyntax access && access.Name == node => access.Expression,
            _ => null
        };

    // True when a read through the owner runs no function that the walk knows. That is a function
    // of a cell that it saw built, or a function of a Lazy. The walk follows such a function at the
    // read, and a write there ends the record of the element. A cell that the walk did not see
    // built counts as one that runs no code. In the body of a callee, the evaluator does not know
    // the values. Thus, only a field, a property with no code, or a parameter of the first callee
    // whose argument has no such function passes there.
    private bool RunsNoFunctionThatTheWalkKnows(
        ExpressionSyntax? owner,
        (InvocationExpressionSyntax Call, IMethodSymbol Method, bool First)? callee)
    {
        if (owner == null)
        {
            return false;
        }

        if (callee is not { } of)
        {
            LoopState state = this.Eval(owner);

            return state.Computed.IsEmpty && state.Functions.IsEmpty;
        }

        return this.region.SymbolOf(Unwrapped(owner)) switch
        {
            IFieldSymbol => true,
            IPropertySymbol property => property.DeclaringSyntaxReferences.IsEmpty || this.IsAMemberLocation(property),
            IParameterSymbol parameter when of.First =>
                TheArgumentFor(call: of.Call, method: of.Method, parameter: parameter) is { } argument
                && this.RunsNoFunctionThatTheWalkKnows(owner: argument, callee: null),
            _ => false
        };

        // The argument of a call for a parameter of the method that it calls. The receiver of an
        // extension method is the argument for its first parameter.
        static ExpressionSyntax? TheArgumentFor(
            InvocationExpressionSyntax call,
            IMethodSymbol method,
            IParameterSymbol parameter) =>
            method.ReducedFrom != null && parameter.Ordinal == 0
                ? (call.Expression as MemberAccessExpressionSyntax)?.Expression
                : call.ArgumentList.Arguments
                    .Where((argument, position) =>
                        ParameterFor(method: method, argument: argument, position: position)?.Name == parameter.Name)
                    .Select(static argument => argument.Expression)
                    .FirstOrDefault();
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
