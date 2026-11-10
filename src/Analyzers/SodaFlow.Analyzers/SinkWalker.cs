using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Walks code that runs while a loop is open, and finds each read of a
///     pending value. The code is the block, or the body of a callee that the
///     block calls.
/// </summary>
/// <remarks>
///     A lambda and a local function are values. The walk does not read one
///     at its declaration. It reads one where the code calls it, or gives it to
///     a callee that runs it in the call. At a call to code of this compilation,
///     the evaluator walks the body of the callee with a second walker. This
///     walker reports what that walk found.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage - This class has its own summary, which says more than the base class does.
internal sealed class SinkWalker(in LoopRegion region, in LoopStateEvaluator evaluator, in Action<Finding> sink)
    : CSharpSyntaxWalker
{
    private readonly HashSet<AnonymousFunctionExpressionSyntax> entered = [];

    // The lambdas that the walk read where they are, and that their callee runs one time.
    private readonly HashSet<AnonymousFunctionExpressionSyntax> once = [];
    private readonly LoopStateEvaluator evaluator = evaluator;
    private readonly LoopRegion region = region;
    private readonly Action<Finding> sink = sink;

    public override void Visit(SyntaxNode? node)
    {
        base.Visit(node);

        if (node is not ExpressionSyntax expression)
        {
            return;
        }

        // An expression can make a call with no call syntax: the getter of an indexer, an
        // operator, or a conversion.
        this.Report(this.evaluator.FollowExpression(expression));

        // The indexer of a dictionary of the platform calls a member of the key.
        if (expression is ElementAccessExpressionSyntax keyed)
        {
            this.Report(this.evaluator.FollowPlatformIndexer(keyed));
        }

        if (ConvertsHere(expression))
        {
            this.Report(this.evaluator.FollowConversion(expression));
        }

        if (IsCondition(expression))
        {
            this.Report(this.evaluator.FollowTruth(expression));
        }
    }

    private static bool IsCondition(SyntaxNode expression) =>
        expression.Parent switch
        {
            ConditionalExpressionSyntax choice => choice.Condition == expression,
            IfStatementSyntax choice => choice.Condition == expression,
            WhileStatementSyntax loop => loop.Condition == expression,
            DoStatementSyntax loop => loop.Condition == expression,
            ForStatementSyntax loop => loop.Condition == expression,
            _ => false
        };

    // The places where the compiler can add a conversion to an expression. The owner of a member
    // and the name of a type are not such places.
    private static bool ConvertsHere(SyntaxNode expression) =>
        expression.Parent switch
        {
            ArgumentSyntax
                or EqualsValueClauseSyntax
                or ReturnStatementSyntax
                or ArrowExpressionClauseSyntax
                or InterpolationSyntax
                or BinaryExpressionSyntax
                or ConditionalExpressionSyntax
                or IfStatementSyntax
                or WhileStatementSyntax
                or ExpressionElementSyntax
                or InitializerExpressionSyntax => true,
            AssignmentExpressionSyntax assignment => assignment.Right == expression,
            _ => false
        };

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        foreach (VariableDeclaratorSyntax declarator in node.Declaration.Variables)
        {
            if (declarator.Initializer == null)
            {
                continue;
            }

            this.Visit(declarator.Initializer.Value);

            if (model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.evaluator.Declare(local: local, state: this.evaluator.Eval(declarator.Initializer.Value));
            }

            // A using declaration calls Dispose when the code leaves the block that it is in.
            if (node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            {
                this.Report(this.evaluator.FollowDispose(declarator.Initializer.Value));
            }
        }
    }

    public override void VisitUsingStatement(UsingStatementSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        foreach (VariableDeclaratorSyntax declarator in node.Declaration?.Variables ?? default)
        {
            if (declarator.Initializer != null && model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.evaluator.Declare(local: local, state: this.evaluator.Eval(declarator.Initializer.Value));
            }
        }

        base.VisitUsingStatement(node);

        // The statement calls Dispose on each resource when the code leaves it.
        IEnumerable<ExpressionSyntax> resources =
            node.Expression != null
                ? [node.Expression]
                : (node.Declaration?.Variables ?? default)
                .Select(static declarator => declarator.Initializer?.Value)
                .OfType<ExpressionSyntax>();

        foreach (ExpressionSyntax resource in resources)
        {
            this.Report(this.evaluator.FollowDispose(resource));
        }
    }

    // An interpolation calls ToString on its value.
    public override void VisitInterpolation(InterpolationSyntax node)
    {
        base.VisitInterpolation(node);
        this.Report(this.evaluator.FollowToString(node.Expression));
    }

    // A local function runs when the code calls it, and the evaluator follows it then.
    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
    }

    // A lambda runs when something calls it. A callee that runs it in the call reads it there.
    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
    }

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        this.Visit(node.Expression);
        this.RunQueries(node.Expression);

        // The collection has the state of all its elements, thus each element has that state. An
        // enumerator of this compilation gives what its Current returns also.
        LoopState elements = this.evaluator.Eval(node.Expression).AsElement.Join(this.Enumerate(node));

        if (this.region.ModelFor(node).GetDeclaredSymbol(node) is { } element)
        {
            this.evaluator.Declare(local: element, state: elements);
        }

        this.Visit(node.Statement);
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        this.Visit(node.Expression);
        this.RunQueries(node.Expression);

        LoopState elements = this.evaluator.Eval(node.Expression).AsElement.Join(this.Enumerate(node));

        IMethodSymbol? method =
            this.region.Symbols.DeclaresDeconstruct
                ? this.region.ModelFor(node).GetDeconstructionInfo(node).Method
                : null;

        if (!this.Deconstruct(
                call: node.Variable,
                target: node.Variable,
                value: node.Expression,
                owner: elements,
                method: method,
                replaces: false))
        {
            this.AssignState(target: node.Variable, state: elements, replaces: false);
        }

        this.Visit(node.Statement);
    }

    /// <summary>
    ///     The calls that a foreach makes on a collection of this compilation:
    ///     <c>GetEnumerator</c>, then <c>MoveNext</c> and <c>Current</c> on an
    ///     enumerator of this compilation. The result is the state of what
    ///     <c>Current</c> returns.
    /// </summary>
    private LoopState Enumerate(CommonForEachStatementSyntax node)
    {
        if (!this.region.Symbols.DeclaresEnumerators
            || this.region.ModelFor(node).GetForEachStatementInfo(node) is not { GetEnumeratorMethod: { } start } calls)
        {
            return LoopState.None;
        }

        // The collection itself, and not one of its elements, has the enumerator.
        LoopState collection = this.evaluator.Self(node.Expression);

        // An extension method gets the collection as its argument.
        Followed? enumerator =
            start.IsStatic
                ? this.evaluator.FollowImplied(
                    call: node.Expression,
                    member: (start.ReducedFrom ?? start).OriginalDefinition,
                    owner: null,
                    arguments: [(node.Expression, collection)])
                : this.evaluator.FollowImplied(call: node.Expression, member: start, owner: collection, arguments: []);

        this.Report(enumerator);

        if (enumerator == null)
        {
            return LoopState.None;
        }

        // An iterator has no enumerator object of this compilation, and a method can give the
        // enumerator of a collection of the platform. The result then has the state of the
        // elements.
        if (enumerator.State.Members?.Is(start.ReturnType) != true)
        {
            return enumerator.State;
        }

        if (calls.MoveNextMethod is { } next)
        {
            this.Report(
                this.evaluator.FollowImplied(
                    call: node.Expression,
                    member: next,
                    owner: enumerator.State,
                    arguments: []));
        }

        if (calls.CurrentProperty?.GetMethod is not { } getter)
        {
            return LoopState.None;
        }

        Followed? current =
            this.evaluator.FollowImplied(
                call: node.Expression,
                member: getter,
                owner: enumerator.State,
                arguments: []);

        this.Report(current);

        return current?.State ?? LoopState.None;
    }

    public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        this.DeclarePattern(pattern: node.Pattern, state: this.evaluator.Eval(node.Expression));
        base.VisitIsPatternExpression(node);
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        LoopState state = this.evaluator.Eval(node.Expression);

        foreach (CasePatternSwitchLabelSyntax label in node.Sections
                     .SelectMany(static section => section.Labels)
                     .OfType<CasePatternSwitchLabelSyntax>())
        {
            this.DeclarePattern(pattern: label.Pattern, state: state);
        }

        base.VisitSwitchStatement(node);
    }

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        LoopState state = this.evaluator.Eval(node.GoverningExpression);

        foreach (SwitchExpressionArmSyntax arm in node.Arms)
        {
            this.DeclarePattern(pattern: arm.Pattern, state: state);
        }

        base.VisitSwitchExpression(node);
    }

    /// <summary>
    ///     Gives each variable of a pattern the state of the value that the
    ///     pattern is for. A variable for a member of an object gets the state of
    ///     that member, when the walk knows the members.
    /// </summary>
    private void DeclarePattern(PatternSyntax pattern, LoopState state)
    {
        // A getter that a pattern reads can read a field that the block set, on an object with no
        // state of its own.
        if (state.IsNone && !this.region.HasFields)
        {
            return;
        }

        // A pattern that only holds one more pattern gives the same state to that pattern. The
        // loop goes to it.
        while (true)
        {
            switch (pattern)
            {
                case DeclarationPatternSyntax declaration:
                    this.DeclareDesignation(designation: declaration.Designation, state: state);

                    return;
                case VarPatternSyntax variable:
                    this.DeclareDesignation(designation: variable.Designation, state: state);

                    return;
                case RecursivePatternSyntax recursive:
                {
                    this.DeclareDesignation(designation: recursive.Designation, state: state);

                    foreach (SubpatternSyntax part in recursive.PropertyPatternClause?.Subpatterns ?? default)
                    {
                        ExpressionSyntax? name = part.NameColon?.Name ?? part.ExpressionColon?.Expression;

                        this.DeclarePattern(
                            pattern: part.Pattern,
                            state: name == null ? state : this.ReadPatternMember(name: name, owner: state));
                    }

                    this.DeclarePositional(clause: recursive.PositionalPatternClause, state: state);

                    return;
                }
                case BinaryPatternSyntax binary:
                    this.DeclarePattern(pattern: binary.Left, state: state);
                    pattern = binary.Right;

                    continue;
                case UnaryPatternSyntax unary:
                    pattern = unary.Pattern;

                    continue;
                case ParenthesizedPatternSyntax parenthesized:
                    pattern = parenthesized.Pattern;

                    continue;
                case ListPatternSyntax list:
                    this.DeclareDesignation(designation: list.Designation, state: state);

                    // A slice is a part of the list. Each other pattern is for one element.
                    foreach (PatternSyntax element in list.Patterns)
                    {
                        this.DeclarePattern(
                            pattern: element,
                            state: element is SlicePatternSyntax ? state : state.AsElement);
                    }

                    return;
                case SlicePatternSyntax { Pattern: { } rest }:
                    pattern = rest;

                    continue;
                default:
                    return;
            }
        }
    }

    /// <summary>
    ///     The state of the member that a part of a property pattern names, on a
    ///     value with the given state. The pattern reads the member as a member
    ///     access does, thus the walk checks that read here.
    /// </summary>
    private LoopState ReadPatternMember(ExpressionSyntax name, LoopState owner)
    {
        // A name such as Inner.Now reads a member of a member.
        if (name is MemberAccessExpressionSyntax chain)
        {
            owner = this.ReadPatternMember(name: chain.Expression, owner: owner);
            name = chain.Name;
        }

        if (this.region.SymbolOf(name) is not { } member)
        {
            return owner;
        }

        if (member is IPropertySymbol property)
        {
            if (this.region.Symbols.IsSodaFlow(property))
            {
                if (SodaFlowSymbols.ReadMembers.Contains(property.Name))
                {
                    this.CheckRead(node: name, state: owner, name: property.Name);

                    return owner.Content;
                }
            }
            else if ((property.DeclaringSyntaxReferences.Length > 0 || SodaFlowSymbols.IsLazyValue(property))
                     && this.evaluator.FollowPatternMember(name: name, owner: owner) is { } getter)
            {
                this.Report(getter);

                return getter.State;
            }
        }

        return owner.Members != null
            ? owner.Members.StateOf(owner.Members.Resolve(member)).FromIfUnknown(owner.Origin)
            : owner;
    }

    /// <summary>
    ///     The parts of a positional pattern. A value that is not a tuple gives
    ///     them through a <c>Deconstruct</c> method. Each part then has the state of
    ///     the <c>out</c> parameter for it. Otherwise, each part has the state of
    ///     the value.
    /// </summary>
    private void DeclarePositional(PositionalPatternClauseSyntax? clause, LoopState state)
    {
        if (clause == null)
        {
            return;
        }

        IMethodSymbol? method =
            this.region.Symbols.DeclaresDeconstruct
                ? this.region.ModelFor(clause).GetSymbolInfo(clause).Symbol as IMethodSymbol
                : null;

        Followed? followed =
            method != null && MatchedValueOf(clause) is { } value
                ? this.evaluator.FollowDeconstruct(call: clause, method: method, value: value, owner: state)
                : null;

        this.Report(followed);

        ImmutableArray<IParameterSymbol> outs =
            followed == null || method == null ? ImmutableArray<IParameterSymbol>.Empty : OutsOf(method);

        for (int i = 0; i < clause.Subpatterns.Count; i++)
        {
            this.DeclarePattern(
                pattern: clause.Subpatterns[i].Pattern,
                state: followed == null
                    ? state
                    : i < outs.Length && followed.Outs.TryGetValue(key: outs[i], value: out LoopState? part)
                        ? part
                        : LoopState.None);
        }
    }

    // The expression that a pattern is for: the left side of an is, or the value of a switch.
    private static ExpressionSyntax? MatchedValueOf(SyntaxNode pattern) =>
        pattern.Ancestors()
            .Select(static ancestor =>
                ancestor switch
                {
                    IsPatternExpressionSyntax match => match.Expression,
                    SwitchExpressionSyntax choice => choice.GoverningExpression,
                    SwitchStatementSyntax choice => choice.Expression,
                    _ => null
                })
            .FirstOrDefault(static value => value != null);

    // The out parameters of a Deconstruct method, in order. An extension method has the value as
    // its first parameter, which is not one of them.
    private static ImmutableArray<IParameterSymbol> OutsOf(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).OriginalDefinition.Parameters
        .Where(static parameter => parameter.RefKind == RefKind.Out)
        .ToImmutableArray();

    // The targets of a deconstruction, in order. Null for a target that is not one.
    private static IReadOnlyList<SyntaxNode>? PartsOf(SyntaxNode target) =>
        target switch
        {
            TupleExpressionSyntax tuple => [.. tuple.Arguments.Select(static argument => argument.Expression)],
            DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax several } =>
                [.. several.Variables],
            _ => null
        };

    /// <summary>
    ///     A deconstruction that calls a <c>Deconstruct</c> method of this
    ///     compilation. The walk reports what the method reads, and gives each
    ///     target the state of the <c>out</c> parameter for it. False when the
    ///     deconstruction calls no such method.
    /// </summary>
    private bool Deconstruct(
        SyntaxNode call,
        SyntaxNode target,
        ExpressionSyntax value,
        LoopState owner,
        IMethodSymbol? method,
        bool replaces)
    {
        if (method == null
            || PartsOf(target) is not { } parts
            || this.evaluator.FollowDeconstruct(call: call, method: method, value: value, owner: owner)
                is not { } followed)
        {
            return false;
        }

        this.Report(followed);

        ImmutableArray<IParameterSymbol> outs = OutsOf(method);

        for (int i = 0; i < parts.Count; i++)
        {
            LoopState part =
                i < outs.Length && followed.Outs.TryGetValue(key: outs[i], value: out LoopState? given)
                    ? given
                    : LoopState.None;

            if (parts[i] is ExpressionSyntax expression)
            {
                this.AssignState(target: expression, state: part, replaces: replaces);
            }
            else
            {
                this.DeclareDesignation(designation: parts[i], state: part);
            }
        }

        return true;
    }

    /// <summary>
    ///     An assignment to a property or an indexer runs its setter. An
    ///     assignment with <c>+=</c> or <c>-=</c> to an event runs an accessor of
    ///     the event. The walk reads an accessor of this compilation that has a
    ///     body.
    /// </summary>
    private void CheckAccessor(AssignmentExpressionSyntax node)
    {
        IMethodSymbol? accessor =
            this.region.SymbolOf(node.Left) switch
            {
                IPropertySymbol { SetMethod: { } setter } => setter,
                IEventSymbol { AddMethod: { } add } when node.IsKind(SyntaxKind.AddAssignmentExpression) => add,
                IEventSymbol { RemoveMethod: { } remove } when node.IsKind(SyntaxKind.SubtractAssignmentExpression) =>
                    remove,
                _ => null
            };

        if (accessor == null)
        {
            return;
        }

        if (accessor.DeclaringSyntaxReferences.IsEmpty)
        {
            // The indexer of a dictionary of the platform calls a member of the key.
            if (node.Left is ElementAccessExpressionSyntax keyed)
            {
                this.Report(
                    this.evaluator.FollowPlatformMember(
                        member: accessor,
                        collection: keyed.Expression,
                        values: keyed.ArgumentList.Arguments.Select(static index => index.Expression)));
            }

            return;
        }

        (ExpressionSyntax? owner, BracketedArgumentListSyntax? indexes) =
            node.Left switch
            {
                MemberAccessExpressionSyntax member => (member.Expression, null),
                ElementAccessExpressionSyntax element => (element.Expression, element.ArgumentList),
                _ => ((ExpressionSyntax?)null, (BracketedArgumentListSyntax?)null)
            };

        // The accessor gets each index, and then the value.
        this.Report(
            this.evaluator.FollowImplied(
                call: node,
                member: accessor,
                owner: owner is null or ThisExpressionSyntax ? null : this.evaluator.Eval(owner),
                arguments:
                [
                    .. (indexes?.Arguments ?? default).Select(index =>
                        (index.Expression, this.evaluator.Eval(index.Expression))),
                    (node.Right, this.evaluator.Eval(node.Right))
                ]));
    }

    // The name of a part of a property pattern is a member of the value that the pattern is for.
    // It is not code of its own. ReadPatternMember checks the read that it is.
    public override void VisitSubpattern(SubpatternSyntax node) => this.Visit(node.Pattern);

    private void DeclareDesignation(SyntaxNode? designation, LoopState state)
    {
        if (designation == null)
        {
            return;
        }

        SemanticModel model = this.region.ModelFor(designation);

        foreach (SingleVariableDesignationSyntax single in designation.DescendantNodesAndSelf()
                     .OfType<SingleVariableDesignationSyntax>())
        {
            if (model.GetDeclaredSymbol(single) is { } declared)
            {
                this.evaluator.Declare(local: declared, state: state);
            }
        }
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            base.VisitAssignmentExpression(node);

            // A delegate that gets one more function, as in handlers += () => ..., can be each of
            // its functions.
            if (node.IsKind(SyntaxKind.AddAssignmentExpression)
                && this.evaluator.Eval(node.Right) is { Functions.IsEmpty: false } added)
            {
                this.AssignState(target: node.Left, state: added, replaces: false);
            }

            this.CheckAccessor(node);

            return;
        }

        // The assignment writes its target, and does not read it. Only the owner of the target
        // is read.
        switch (node.Left)
        {
            case IdentifierNameSyntax or TupleExpressionSyntax or DeclarationExpressionSyntax:
                break;
            case MemberAccessExpressionSyntax access:
                this.Visit(access.Expression);
                break;
            case ElementAccessExpressionSyntax element:
                this.Visit(element.Expression);
                this.Visit(element.ArgumentList);
                break;
            default:
                this.Visit(node.Left);
                break;
        }

        this.Visit(node.Right);
        this.CheckAccessor(node);

        bool replaces = this.AlwaysRuns(node);

        // A deconstruction of a value that is not a tuple calls a Deconstruct method.
        IMethodSymbol? deconstruct =
            this.region.Symbols.DeclaresDeconstruct
            && node.Right is not TupleExpressionSyntax
            && PartsOf(node.Left) != null
                ? this.region.ModelFor(node).GetDeconstructionInfo(node).Method
                : null;

        if (!this.Deconstruct(
                call: node,
                target: node.Left,
                value: node.Right,
                owner: this.evaluator.Eval(node.Right),
                method: deconstruct,
                replaces: replaces))
        {
            this.Assign(target: node.Left, value: node.Right, replaces: replaces);
        }
    }

    /// <summary>
    ///     True for an assignment that is a statement at the first level of its
    ///     function, with no branch and no loop around it. Each call of the function
    ///     runs it, thus the state that it gives replaces the state before it.
    /// </summary>
    /// <remarks>
    ///     A lambda that the walk reads where it is has a callee that runs it. A
    ///     transaction and a loop block run it one time, and the walk then looks at
    ///     what is around the call. A query runs it one time for each element, which
    ///     can be no time, thus an assignment in it does not always run.
    /// </remarks>
    private bool AlwaysRuns(SyntaxNode assignment)
    {
        if (assignment.Parent is not ExpressionStatementSyntax statement)
        {
            return false;
        }

        foreach (SyntaxNode ancestor in statement.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda when this.entered.Contains(lambda):
                    if (!this.once.Contains(lambda))
                    {
                        return false;
                    }

                    continue;
                case AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or MemberDeclarationSyntax
                    or AccessorDeclarationSyntax:
                    return true;
                // A block, and a statement that always runs what is in it. Also the call that
                // runs a lambda, and the statement that the call is in.
                case BlockSyntax
                    or UsingStatementSyntax
                    or LockStatementSyntax
                    or CheckedStatementSyntax
                    or UnsafeStatementSyntax
                    or FixedStatementSyntax
                    or ArgumentSyntax
                    or ArgumentListSyntax
                    or InvocationExpressionSyntax
                    or ExpressionStatementSyntax
                    or EqualsValueClauseSyntax
                    or VariableDeclaratorSyntax
                    or VariableDeclarationSyntax
                    or LocalDeclarationStatementSyntax
                    or ReturnStatementSyntax:
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Gives a target the state of a value. A deconstruction of a tuple
    ///     expression gives each part of the tuple to its own target.
    /// </summary>
    private void Assign(SyntaxNode target, ExpressionSyntax value, bool replaces)
    {
        if (target is TupleExpressionSyntax targets
            && value is TupleExpressionSyntax values
            && targets.Arguments.Count == values.Arguments.Count)
        {
            for (int i = 0; i < targets.Arguments.Count; i++)
            {
                this.Assign(
                    target: targets.Arguments[i].Expression,
                    value: values.Arguments[i].Expression,
                    replaces: replaces);
            }

            return;
        }

        LoopState whole = this.evaluator.Eval(value);

        // A deconstruction of a value that is not a tuple expression. Each target gets the state
        // of its element, when the evaluator knows the elements. Otherwise, each gets the state of
        // the value.
        IReadOnlyList<SyntaxNode>? parts = PartsOf(target);

        if (parts == null
            || whole.Members == null
            || this.region.TypeOf(value) is not INamedTypeSymbol { IsTupleType: true } type
            || type.TupleElements.Length != parts.Count)
        {
            this.AssignState(target: target, state: whole, replaces: replaces);

            return;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            LoopState element =
                whole.Members.Has(type.TupleElements[i])
                    ? whole.Members.StateOf(type.TupleElements[i]).FromIfUnknown(whole.Origin)
                    : whole;

            if (parts[i] is ExpressionSyntax expression)
            {
                this.AssignState(target: expression, state: element, replaces: replaces);
            }
            else
            {
                this.DeclareDesignation(designation: parts[i], state: element);
            }
        }
    }

    /// <param name="target">The expression that gets the value.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">
    ///     True when the assignment always runs. Then, a local has the new state
    ///     only. Otherwise, it has the new state and the state before it together.
    /// </param>
    private void AssignState(SyntaxNode target, LoopState state, bool replaces)
    {
        switch (target)
        {
            case DeclarationExpressionSyntax declaration:
                this.DeclareDesignation(designation: declaration.Designation, state: state);

                return;
            // A value that is not a tuple expression: each part gets the state of all of it.
            case TupleExpressionSyntax parts:
                foreach (ArgumentSyntax part in parts.Arguments)
                {
                    this.AssignState(target: part.Expression, state: state, replaces: replaces);
                }

                return;
            // A value put into a collection is in the collection.
            case ElementAccessExpressionSyntax element:
                this.evaluator.Absorb(target: element.Expression, state: state);

                return;
        }

        switch (this.region.SymbolOf(target))
        {
            case { } local and (ILocalSymbol or IParameterSymbol) when target is IdentifierNameSyntax:
                if (replaces)
                {
                    this.evaluator.Assign(local: local, state: state);
                }
                else
                {
                    this.evaluator.Declare(local: local, state: state);
                }

                break;
            // A member of the object that the code is in: a constructor that sets its members, or
            // a method that does.
            case { IsStatic: false } own and (IFieldSymbol or IPropertySymbol)
                when target is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }:
                this.evaluator.SetMember(member: own, state: state);
                break;
            case { } member and (IFieldSymbol or IPropertySymbol) when !this.region.Symbols.IsSodaFlow(member):
                this.evaluator.SetMemberOf(
                    owner: (target as MemberAccessExpressionSyntax)?.Expression,
                    member: member,
                    state: state);

                break;
        }
    }

    public override void VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        base.VisitCollectionExpression(node);

        // A spread enumerates its value.
        foreach (SpreadElementSyntax spread in node.Elements.OfType<SpreadElementSyntax>())
        {
            this.RunQueries(spread.Expression);
            this.Report(this.evaluator.FollowEnumeration(spread.Expression));
        }

        // A collection expression for a set of the platform calls Add for each element, and that
        // Add calls a member of the element.
        if (!this.region.Symbols.DeclaresKeyMembers
            || this.region.ModelFor(node).GetTypeInfo(node).ConvertedType is not INamedTypeSymbol made
            || !SodaFlowSymbols.IsPlatform(made)
            || made.GetMembers("Add").OfType<IMethodSymbol>().FirstOrDefault(static add => add.Parameters.Length == 1)
                is not { } add)
        {
            return;
        }

        this.Report(
            this.evaluator.FollowPlatformMember(
                member: add,
                collection: node,
                values: node.Elements.OfType<ExpressionElementSyntax>().Select(static element => element.Expression)));
    }

    public override void VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        // An object initializer names a member and gives it a value. Only the value is code that
        // runs.
        bool setsMembers =
            node.IsKind(SyntaxKind.ObjectInitializerExpression) || node.IsKind(SyntaxKind.WithInitializerExpression);

        foreach (ExpressionSyntax expression in node.Expressions)
        {
            this.Visit(
                setsMembers && expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax } assignment
                    ? assignment.Right
                    : expression);

            this.CheckInitialized(initializer: node, expression: expression, setsMembers: setsMembers);
        }
    }

    /// <summary>
    ///     An object initializer runs the setter of each property that it names.
    ///     A collection initializer calls <c>Add</c> for each element. The walk
    ///     reads a setter or an <c>Add</c> of this compilation that has a body.
    /// </summary>
    private void CheckInitialized(SyntaxNode initializer, ExpressionSyntax expression, bool setsMembers)
    {
        if (initializer.Parent
            is not (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or WithExpressionSyntax))
        {
            return;
        }

        ExpressionSyntax made = (ExpressionSyntax)initializer.Parent;

        if (setsMembers)
        {
            // An initializer with an index calls the indexer, and the indexer of a dictionary of
            // the platform calls a member of the key.
            if (expression is AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax keyed }
                && this.region.Symbols.DeclaresKeyMembers
                && this.region.SymbolOf(keyed) is IPropertySymbol { SetMethod: { } indexer })
            {
                this.Report(
                    this.evaluator.FollowPlatformMember(
                        member: indexer,
                        collection: made,
                        values: keyed.ArgumentList.Arguments.Select(static index => index.Expression)));
            }

            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assignment
                && this.region.SymbolOf(name) is IPropertySymbol
                {
                    SetMethod: { DeclaringSyntaxReferences.IsEmpty: false } setter
                })
            {
                this.Report(
                    this.evaluator.FollowImplied(
                        call: assignment,
                        member: setter,
                        owner: this.evaluator.Eval(made),
                        arguments: [(assignment.Right, this.evaluator.Eval(assignment.Right))]));
            }

            return;
        }

        if (!initializer.IsKind(SyntaxKind.CollectionInitializerExpression)
            || !(this.region.Symbols.DeclaresAdd || this.region.Symbols.DeclaresKeyMembers)
            || this.region.ModelFor(expression).GetCollectionInitializerSymbolInfo(expression).Symbol
                is not IMethodSymbol add)
        {
            return;
        }

        // An element with braces gives each of its values to one Add. An array, and not a
        // collection expression. As an arm of this conditional, that expression takes the type of
        // the other arm, and the conversion gives an empty list.
        IEnumerable<ExpressionSyntax> values =
            expression is InitializerExpressionSyntax several ? several.Expressions : new[] { expression };

        // An Add of the platform can call a member of the element, as the Add of a set does.
        if (add.DeclaringSyntaxReferences.IsEmpty)
        {
            this.Report(this.evaluator.FollowPlatformMember(member: add, collection: made, values: values));

            return;
        }

        // An extension method gets the collection as its first argument.
        IEnumerable<ExpressionSyntax> given = add.IsStatic ? values.Prepend(made) : values;

        this.Report(
            this.evaluator.FollowImplied(
                call: expression,
                member: (add.ReducedFrom ?? add).OriginalDefinition,
                owner: add.IsStatic ? null : this.evaluator.Eval(made),
                arguments: [.. given.Select(value => (value, this.evaluator.Eval(value)))]));
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (this.region.SymbolOf(node) is not IMethodSymbol method)
        {
            base.VisitInvocationExpression(node);

            return;
        }

        this.Visit(node.Expression);

        SodaFlowSymbols symbols = this.region.Symbols;
        SeparatedSyntaxList<ArgumentSyntax> arguments = node.ArgumentList.Arguments;
        ExpressionSyntax? receiver = LoopStateEvaluator.ReceiverOf(symbols: symbols, invocation: node, method: method);
        CalleeKind kind = this.evaluator.Classify(call: node, method: method);

        // A query method of the platform and an iterator run nothing in the call.
        bool waits = this.evaluator.Waits(call: node, method: method);

        // An operator that keeps its function has nothing for the evaluator to follow.
        Followed? followed =
            kind == CalleeKind.Library
            && !OperatorTable.FunctionsInTheCall.Contains(LoopStateEvaluator.OperatorName(method))
                ? null
                : this.evaluator.Follow(node);

        switch (kind)
        {
            // An operator keeps its function for a firing after the close, but for the few that
            // run it in the block.
            case CalleeKind.Library:
                this.VisitArguments(
                    arguments: arguments,
                    runs: Runs,
                    given: LoopState.None,
                    oneTime: OperatorTable.RunsFunctionOneTime(method));

                this.RunGiven(method: method, receiver: receiver, arguments: arguments);

                break;
            // The evaluator follows the body, and a lambda is a value that the body gets.
            case CalleeKind.Body:
                this.VisitArguments(
                    arguments: arguments,
                    runs: Runs,
                    given: LoopState.None,
                    oneTime: false);

                this.AssignOuts(arguments: arguments, method: method, followed: followed, moved: LoopState.None);

                break;
            // A query keeps its functions for the code that enumerates it. The walk reads them
            // there.
            case CalleeKind.Mover when waits:
                this.VisitArguments(
                    arguments: arguments,
                    runs: static (_, _) => false,
                    given: LoopState.None,
                    oneTime: false);

                break;
            // Code that cannot know the library runs a lambda in the call, with the values that
            // the call gave it. It can put an argument into its receiver, as Add does to a list.
            case CalleeKind.Mover:
            {
                // The callee gives one of the values to a lambda or to an out parameter, and not
                // the collection that contains them.
                LoopState moved =
                    this.evaluator.Moved(invocation: node, method: method, withReceiver: true).AsElement;

                this.VisitArguments(
                    arguments: arguments,
                    runs: Runs,
                    given: moved,
                    oneTime: false);

                this.RunGiven(method: method, receiver: receiver, arguments: arguments);

                if (receiver != null && method.ReducedFrom == null)
                {
                    this.evaluator.Absorb(
                        target: receiver,
                        state: this.evaluator.Moved(invocation: node, method: method, withReceiver: false));
                }

                this.AssignOuts(arguments: arguments, method: method, followed: null, moved: moved);

                // The callee can call a member of an object that it gets, as a sort calls CompareTo.
                this.Report(this.evaluator.FollowPlatformCall(invocation: node, method: method));

                break;
            }
            // The analyzer cannot see a pending value that goes to this callee.
            case CalleeKind.Unknown:
            {
                this.VisitArguments(
                    arguments: arguments,
                    runs: Runs,
                    given: LoopState.None,
                    oneTime: false);

                this.RunGiven(method: method, receiver: receiver, arguments: arguments);

                IMethodSymbol called = method.ReducedFrom ?? method;

                if (method.ReducedFrom != null && receiver != null)
                {
                    this.CheckEscape(callee: called, argument: receiver);
                }

                this.CheckEscapes(callee: called, arguments: arguments);

                break;
            }
            default:
                throw new ArgumentOutOfRangeException(paramName: nameof(node), message: "A callee kind has no case.");
        }

        // The walk reports what the body of an iterator reads where something enumerates the
        // result.
        if (waits)
        {
            this.evaluator.Wait(node);
        }
        else
        {
            this.Report(followed);
        }

        // CanExecute is an ICommand member, which a bindable action implements. Thus, the read
        // members count on a library receiver too, and not only on a library method.
        if (receiver != null
            && SodaFlowSymbols.ReadMembers.Contains(LoopStateEvaluator.OperatorName(method))
            && (kind == CalleeKind.Library || symbols.IsCarrierType(this.region.TypeOf(receiver))))
        {
            this.CheckRead(node: node, owner: receiver, name: method.Name);
        }

        return;

        // A lambda written at the call is code that the walk reads here when the callee runs it
        // in the call.
        bool Runs(ArgumentSyntax argument, int position) =>
            LoopStateEvaluator.RunsFunction(
                kind: kind,
                method: method,
                parameter: LoopStateEvaluator.ParameterFor(method: method, argument: argument, position: position));
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: node.Initializer);

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: node.Initializer);

    public override void VisitConstructorInitializer(ConstructorInitializerSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: null);

    public override void VisitPrimaryConstructorBaseType(PrimaryConstructorBaseTypeSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: null);

    /// <summary>
    ///     A call of a constructor: the creation of an object, or the constructor
    ///     that a constructor calls first.
    /// </summary>
    private void VisitCreation(
        SyntaxNode node,
        BaseArgumentListSyntax? arguments,
        SyntaxNode? initializer)
    {
        if (this.region.SymbolOf(node) is not IMethodSymbol constructor)
        {
            this.Visit(arguments);
            this.Visit(initializer);

            return;
        }

        CalleeKind kind = this.evaluator.Classify(call: node, method: constructor);

        // A constructor gets a lambda as a value. The body says what runs it, when the evaluator
        // follows the body. A constructor of code that the analyzer cannot read keeps it for a
        // time after the call.
        if (arguments != null)
        {
            this.VisitArguments(
                arguments: arguments.Arguments,
                runs: static (_, _) => false,
                given: LoopState.None,
                oneTime: false);

            // A constructor with no body to read enumerates a sequence that it gets, as the
            // constructor of a list does.
            if (kind != CalleeKind.Body)
            {
                for (int i = 0; i < arguments.Arguments.Count; i++)
                {
                    ArgumentSyntax argument = arguments.Arguments[i];

                    if (SodaFlowSymbols.IsEnumerated(
                            SodaFlowSymbols.ParameterOf(
                                    callee: constructor.OriginalDefinition,
                                    argument: argument,
                                    position: i)
                                ?.Type))
                    {
                        this.RunQueries(argument.Expression);
                    }
                }
            }
        }

        this.Visit(initializer);
        this.Report(this.evaluator.Follow(node));

        if (arguments == null)
        {
            return;
        }

        switch (kind)
        {
            case CalleeKind.Unknown:
                this.CheckEscapes(callee: constructor, arguments: arguments.Arguments);

                break;
            // A constructor of the platform can call a member of an object that it gets, as the
            // constructor of a set does.
            case CalleeKind.Mover:
                this.Report(
                    this.evaluator.FollowPlatformCreation(
                        constructor: constructor,
                        arguments: arguments.Arguments));

                break;
            // The evaluator followed the body, and the library has no constructor to read.
            case CalleeKind.Body:
            case CalleeKind.Library:
                break;
            default:
                throw new ArgumentOutOfRangeException(paramName: nameof(node), message: "A callee kind has no case.");
        }
    }

    /// <summary>
    ///     The arguments of a call. An argument that is a lambda is code that the
    ///     walk reads here only when the callee runs it in the call.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="runs">
    ///     True when the callee runs, in the call, the function that it gets for
    ///     the argument at the position.
    /// </param>
    /// <param name="given">
    ///     The state of the values that the callee can give to a lambda, which
    ///     each parameter of the lambda then has.
    /// </param>
    /// <param name="oneTime">True when the callee runs the function one time, and not zero or many.</param>
    /// <param name="onlyLambdas">
    ///     True for the functions of a query, at the time when something
    ///     enumerates it. The walk read each other argument where the code made the
    ///     query.
    /// </param>
    private void VisitArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        Func<ArgumentSyntax, int, bool> runs,
        LoopState given,
        bool oneTime,
        bool onlyLambdas = false)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            ArgumentSyntax argument = arguments[i];

            if (argument.Expression is not AnonymousFunctionExpressionSyntax lambda)
            {
                if (!onlyLambdas)
                {
                    this.Visit(argument);
                }

                continue;
            }

            if (!runs(arg1: argument, arg2: i) || !this.entered.Add(lambda))
            {
                continue;
            }

            if (oneTime)
            {
                this.once.Add(lambda);
            }

            if (!given.IsNone)
            {
                SemanticModel model = this.region.ModelFor(lambda);

                foreach (ParameterSyntax parameter in LoopStateEvaluator.ParametersOf(lambda))
                {
                    if (model.GetDeclaredSymbol(parameter) is { } declared)
                    {
                        this.evaluator.Declare(local: declared, state: given);
                    }
                }
            }

            // The lambda is part of this code. The walk reads it where it is, with the same
            // evaluator.
            this.Visit(lambda.Body);
        }
    }

    /// <summary>
    ///     The sequences that a callee with no body to read enumerates in the
    ///     call. It enumerates each one that it gets for a parameter whose type is
    ///     a sequence or an enumerator. The type is the type that the callee itself
    ///     knows. Thus, a query given for <c>object</c> or for a type parameter is
    ///     a value that the callee keeps.
    /// </summary>
    private void RunGiven(
        IMethodSymbol method,
        ExpressionSyntax? receiver,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        // Most methods get no sequence, and the answer for a method is the same at each call.
        bool starts = method is { ReducedFrom: null, Name: "GetEnumerator" or "MoveNext" };

        // A delegate has the types of its own parameters, and a method has those of its definition.
        IMethodSymbol callee =
            method.MethodKind == MethodKind.DelegateInvoke ? method : (method.ReducedFrom ?? method).OriginalDefinition;

        if (!starts && !this.region.Symbols.TakesASequence(callee))
        {
            return;
        }

        // The receiver of an extension method is the argument of its first parameter. A call of
        // GetEnumerator or MoveNext on a sequence is the start of an enumeration.
        if (receiver != null
            && arguments.All(argument => argument.Expression != receiver)
            && (method.ReducedFrom != null
                ? SodaFlowSymbols.IsEnumerated(method.ReducedFrom.OriginalDefinition.Parameters[0].Type)
                : starts))
        {
            this.RunQueries(receiver);
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (SodaFlowSymbols.IsEnumerated(
                    LoopStateEvaluator.ParameterFor(method: method, argument: arguments[i], position: i)?.Type))
            {
                this.RunQueries(arguments[i].Expression);
            }
        }
    }

    /// <summary>
    ///     Reads what each query in the value of an expression runs. The caller
    ///     is code that enumerates that value.
    /// </summary>
    private void RunQueries(ExpressionSyntax written) => this.Run(this.evaluator.Eval(written));

    private void Run(LoopState state)
    {
        foreach (QueryValue query in state.Queries)
        {
            this.Run(query: query, to: this.sink);
        }
    }

    /// <summary>
    ///     Reads what one query runs, one time. The code of the query is part of
    ///     the code that made it, thus a walker for that code reads it. A query
    ///     from a callee keeps what the walk of the callee found, and this gives
    ///     those findings.
    /// </summary>
    private void Run(QueryValue query, Action<Finding> to)
    {
        if (query.TakeHeld() is { } held)
        {
            foreach (Finding finding in held)
            {
                to(finding);
            }

            return;
        }

        if (!query.Start())
        {
            return;
        }

        SinkWalker walker =
            ReferenceEquals(objA: query.Definer, objB: this.evaluator) && ReferenceEquals(objA: to, objB: this.sink)
                ? this
                : new SinkWalker(region: this.region, evaluator: query.Definer, sink: to);

        walker.RunHere(query.Call);
    }

    private void RunHere(SyntaxNode call)
    {
        switch (call)
        {
            // Each part but the first source runs when something enumerates the query.
            case QueryExpressionSyntax query:
                this.Visit(query.Body);

                return;
            case InvocationExpressionSyntax node when this.region.SymbolOf(node) is IMethodSymbol method:
            {
                SeparatedSyntaxList<ArgumentSyntax> arguments = node.ArgumentList.Arguments;

                // A query method of the platform: its functions, the members that it calls on an
                // object, and the enumeration of its source.
                if (this.evaluator.Classify(call: node, method: method) == CalleeKind.Mover)
                {
                    this.VisitArguments(
                        arguments: arguments,
                        runs: (argument, position) =>
                            LoopStateEvaluator.RunsFunction(
                                kind: CalleeKind.Mover,
                                method: method,
                                parameter: LoopStateEvaluator.ParameterFor(
                                    method: method,
                                    argument: argument,
                                    position: position)),
                        given: this.evaluator.Moved(invocation: node, method: method, withReceiver: true).AsElement,
                        oneTime: false,
                        onlyLambdas: true);

                    this.Report(this.evaluator.FollowPlatformCall(invocation: node, method: method));
                }

                // For an iterator, the body. For a query, a function that is not a lambda.
                this.Report(this.evaluator.Follow(node));

                return;
            }
        }
    }

    /// <summary>
    ///     Reads each query of the frame of this code that nothing enumerated, and
    ///     gives what it found for each one apart. The evaluator calls it when the
    ///     code of a callee returns, and gives each query its findings.
    /// </summary>
    public IReadOnlyList<(QueryValue Query, IReadOnlyList<Finding> Found)> RunWaitingApart()
    {
        LoopFrame frame = this.evaluator.Frame;
        List<(QueryValue Query, IReadOnlyList<Finding> Found)> all = [];

        // The work of one query can make one more, thus the list can get longer in this loop.
        int next = 0;

        while (next < frame.Waiting.Count)
        {
            QueryValue query = frame.Waiting[next++];
            List<Finding> found = [];
            this.Run(query: query, to: found.Add);
            all.Add((query, found));
        }

        return all;
    }

    // The first source of a query expression is read where the code makes the query. Each other
    // part runs when something enumerates the query.
    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        this.Visit(node.FromClause.Expression);
        this.evaluator.Wait(node);
    }

    // A second from clause and a join clause enumerate their source for each element.
    public override void VisitFromClause(FromClauseSyntax node)
    {
        base.VisitFromClause(node);
        this.RunQueries(node.Expression);
    }

    public override void VisitJoinClause(JoinClauseSyntax node)
    {
        base.VisitJoinClause(node);
        this.RunQueries(node.InExpression);
    }

    /// <summary>
    ///     Gives the target of each <c>out</c> or <c>ref</c> argument the state
    ///     that the callee gives to that parameter.
    /// </summary>
    private void AssignOuts(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol method,
        Followed? followed,
        LoopState moved)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        int offset = method.ReducedFrom != null ? 1 : 0;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (!arguments[i].RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
                && !arguments[i].RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
            {
                continue;
            }

            if (SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i + offset)
                is not { } parameter)
            {
                continue;
            }

            LoopState state =
                followed == null
                    ? moved
                    : followed.Outs.TryGetValue(key: parameter, value: out LoopState? assigned)
                        ? assigned
                        : LoopState.None;

            this.AssignState(target: arguments[i].Expression, state: state, replaces: false);
        }
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // Only the owner is code. The name is the member, which the check below reads.
        this.Visit(node.Expression);
        this.CheckPropertyRead(node: node, owner: node.Expression);

        // The name of a method with no body to read, used as a value, as in q.ToList. A call of
        // that delegate enumerates the sequence. The walk reads the query here, where it has the
        // sequence.
        if (node.Parent is not InvocationExpressionSyntax
            && this.region.SymbolOf(node)
                is IMethodSymbol { ReducedFrom: { } extension, DeclaringSyntaxReferences.IsEmpty: true } group
            && !this.region.Symbols.IsSodaFlow(group)
            && !SodaFlowSymbols.MakesQuery(group)
            && SodaFlowSymbols.IsEnumerated(extension.OriginalDefinition.Parameters[0].Type))
        {
            this.RunQueries(node.Expression);
        }
    }

    public override void VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
    {
        if (LoopStateEvaluator.ConditionalReceiver(node) is { } owner)
        {
            this.CheckPropertyRead(node: node, owner: owner);
        }
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        // A name with no owner can be a property of the object that the code is in. Its getter
        // can read a member of that object.
        if (this.evaluator.HasThisMembers
            && this.region.SymbolOf(node) is IPropertySymbol { IsStatic: false, DeclaringSyntaxReferences.Length: > 0 })
        {
            this.Report(this.evaluator.Follow(node));
        }
    }

    /// <summary>
    ///     A property of the library that reads the current value, or a property
    ///     with a getter of this compilation, on the given owner.
    /// </summary>
    private void CheckPropertyRead(SyntaxNode node, ExpressionSyntax owner)
    {
        if (this.region.SymbolOf(node) is not IPropertySymbol property)
        {
            return;
        }

        if (!this.region.Symbols.IsSodaFlow(property))
        {
            // Only a property of this compilation has a getter that the evaluator can read. The
            // value of a Lazy is the result of a function, which can be one of this compilation.
            if (property.DeclaringSyntaxReferences.Length > 0 || SodaFlowSymbols.IsLazyValue(property))
            {
                this.Report(this.evaluator.Follow(node));
            }
        }
        else if (SodaFlowSymbols.ReadMembers.Contains(property.Name))
        {
            this.CheckRead(node: node, owner: owner, name: property.Name);
        }
    }

    /// <summary>A member that reads the current value of its owner.</summary>
    private void CheckRead(SyntaxNode node, ExpressionSyntax owner, string name) =>
        this.CheckRead(node: node, state: this.evaluator.Eval(owner), name: name);

    /// <summary>A member that reads the current value of an owner with the given state.</summary>
    private void CheckRead(SyntaxNode node, LoopState state, string name)
    {
        if (!state.Pending)
        {
            return;
        }

        this.sink(
            new Finding(
                home: this.evaluator.Frame,
                origin: state.Origin,
                escape: false,
                diagnostic: Diagnostic.Create(
                    descriptor: Diagnostics.ReadBeforeClose,
                    location: node.GetLocation(),
                    name,
                    this.region.PlaceholderName),
                subject: name));
    }

    private void Report(Followed? followed)
    {
        if (followed == null)
        {
            return;
        }

        foreach (Finding finding in followed.Findings)
        {
            this.sink(finding);
        }
    }

    private void CheckEscapes(ISymbol callee, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (ArgumentSyntax argument in arguments)
        {
            this.CheckEscape(callee: callee, argument: argument.Expression);
        }
    }

    /// <summary>
    ///     A pending value given to code that the analyzer cannot read. An
    ///     <c>AsCell()</c> at the argument is the assertion of the caller and reports
    ///     nothing.
    /// </summary>
    private void CheckEscape(ISymbol callee, ExpressionSyntax argument)
    {
        LoopState state = this.evaluator.Eval(argument);

        // Only a library value escapes. An object of the caller that holds a pending member goes
        // where the caller sends it, as it does with the looped types. The walk checks its reads
        // through its members.
        if (!state.Pending
            || !this.region.Symbols.IsCarrierType(this.region.TypeOf(argument))
            || LoopStateEvaluator.IsAsserted(argument))
        {
            return;
        }

        string subject = callee.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        this.sink(
            new Finding(
                home: this.evaluator.Frame,
                origin: state.Origin,
                escape: true,
                diagnostic: Diagnostic.Create(
                    descriptor: Diagnostics.Escape,
                    location: argument.GetLocation(),
                    this.region.PlaceholderName,
                    subject),
                subject: subject));
    }
}
