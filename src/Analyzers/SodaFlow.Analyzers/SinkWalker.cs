using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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

    // The most reads of one loop. The walk stops before that when a read leaves what a read before
    // left. The number is only a guard for a loop whose reads have no such end.
    internal const int MaxReadsOfALoop = 32;

    private readonly Dictionary<StatementSyntax, int> firstEnds = [];

    // The key is a function that the walk asked about. The value is the position of the first
    // statement that can end a call of the function before the last statement.
    private readonly Dictionary<SyntaxNode, int> firstExits = [];

    // The key is the body of a loop that the walk is in. The value has, for a local of the code
    // around the loop, the state before its first assignment of each read. Only an assignment that
    // each turn always runs has an entry.
    private readonly Dictionary<SyntaxNode, Dictionary<ISymbol, LoopState>> hadBefore = [];

    // The locals of hadBefore that this read of the body gave a value.
    private readonly HashSet<(SyntaxNode Turn, ISymbol Local)> assignedInThisRead = [];

    // The lambdas that the walk read where they are, and that their callee runs one time.
    private readonly HashSet<AnonymousFunctionExpressionSyntax> once = [];
    private readonly LoopStateEvaluator evaluator = evaluator;
    private readonly LoopRegion region = region;
    private readonly Action<Finding> sink = sink;

    // The query whose code the walk reads now, when something enumerates that query.
    private QueryValue? running;

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

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) =>
        this.DeclareEach(declaration: node.Declaration, disposes: node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword));

    // Gives each variable of a declaration the state of its initializer. A local declaration
    // statement and the declaration of a for statement are such declarations.
    private void DeclareEach(VariableDeclarationSyntax declaration, bool disposes)
    {
        SemanticModel model = this.region.ModelFor(declaration);

        foreach (VariableDeclaratorSyntax declarator in declaration.Variables)
        {
            if (declarator.Initializer == null)
            {
                continue;
            }

            this.Visit(declarator.Initializer.Value);

            if (model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.evaluator.Declare(
                    local: local,
                    state: this.evaluator.Eval(declarator.Initializer.Value),
                    from: declarator.Initializer.Value);

                this.evaluator.HoldsValueOf(local: local, value: declarator.Initializer.Value, replaces: true);
                this.evaluator.GetsElementsOf(collection: declarator, literal: declarator.Initializer.Value);
            }

            // A using declaration calls Dispose when the code leaves the block that it is in.
            if (disposes)
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
        LoopState elements =
            LoopStateEvaluator.ElementOf(
                    collection: this.evaluator.Eval(node.Expression),
                    type: this.region.ModelFor(node).GetForEachStatementInfo(node).ElementType)
                .Join(this.Enumerate(node));

        if (this.region.ModelFor(node).GetDeclaredSymbol(node) is { } element)
        {
            this.evaluator.Declare(local: element, state: elements);
            this.evaluator.HoldsOneOf(local: element, collection: node.Expression);
        }

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        this.Visit(node.Statement);
        this.VisitTheSecondTurn(loop: node, body: node.Statement, before: before);
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        this.Visit(node.Expression);
        this.RunQueries(node.Expression);

        LoopState elements = this.evaluator.Eval(node.Expression).AsElement.Join(this.Enumerate(node));

        IMethodSymbol? method =
            this.region.Symbols.DeclaresDeconstruct || elements.Members is { HasParts: true }
                ? this.region.ModelFor(node).GetDeconstructionInfo(node).Method
                : null;

        if (!this.Deconstruct(
                call: node.Variable,
                target: node.Variable,
                value: node.Expression,
                owner: elements,
                method: method,
                replaces: false,
                turn: null))
        {
            this.AssignState(target: node.Variable, state: elements, replaces: false, turn: null);
        }

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        this.Visit(node.Statement);
        this.VisitTheSecondTurn(loop: node, body: node.Statement, before: before);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        // The parts of a for statement, in the order in which the code runs them. The
        // incrementors run after the body.
        if (node.Declaration is { } declaration)
        {
            this.DeclareEach(declaration: declaration, disposes: false);
        }

        foreach (ExpressionSyntax initializer in node.Initializers)
        {
            this.Visit(initializer);
        }

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        this.Visit(node.Condition);
        this.Visit(node.Statement);

        foreach (ExpressionSyntax incrementor in node.Incrementors)
        {
            this.Visit(incrementor);
        }

        this.VisitTheSecondTurn(
            loop: node,
            body: node.Statement,
            before: before,
            condition: node.Condition,
            incrementors: node.Incrementors);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        base.VisitWhileStatement(node);
        this.VisitTheSecondTurn(loop: node, body: node.Statement, before: before, condition: node.Condition);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        base.VisitDoStatement(node);
        this.VisitTheSecondTurn(loop: node, body: node.Statement, before: before, after: node.Condition);
    }

    /// <summary>
    ///     Reads a turn of a loop a second time, after the first read of the loop.
    ///     The second turn of the loop reads what the first turn gave to each
    ///     location that is older than the turn.
    /// </summary>
    /// <param name="loop">The loop statement.</param>
    /// <param name="body">The body of the loop.</param>
    /// <param name="before">
    ///     The result of <see cref="LoopStateEvaluator.ShapeOfWhatALoopLeaves" />
    ///     before the first read.
    /// </param>
    /// <param name="condition">The condition that the loop reads before each turn, if it has one.</param>
    /// <param name="incrementors">The expressions that a <c>for</c> runs after each turn.</param>
    /// <param name="after">The condition that a <c>do</c> reads after each turn.</param>
    private void VisitTheSecondTurn(
        SyntaxNode loop,
        SyntaxNode body,
        int before,
        SyntaxNode? condition = null,
        SeparatedSyntaxList<ExpressionSyntax> incrementors = default,
        SyntaxNode? after = null)
    {
        this.evaluator.EndTurns(body);

        // A read that leaves what a read before left gives the next read nothing new. That is
        // true for the first read also. When it changes no location that is older than the turn,
        // the second turn is the same as the first one.
        HashSet<int> left = [before];

        // A value can come to its read only after two or more turns. The walk reads the loop again
        // while a read gives a location a new state. The locals of the body keep the state of the
        // last read, because the result of a function can be a local of a loop.
        for (int read = 2;
             read <= MaxReadsOfALoop && left.Add(this.evaluator.ShapeOfWhatALoopLeaves(body));
             read++)
        {
            this.evaluator.StartTheSecondTurn(loop: loop, turn: body);

            // The walk reads a lambda that a callee runs in the call one time. The next turn runs
            // it again.
            this.entered.RemoveWhere(lambda => lambda != loop && LoopStateEvaluator.IsIn(node: lambda, code: loop));
            this.assignedInThisRead.RemoveWhere(assigned => assigned.Turn == body);

            this.Visit(condition);
            this.Visit(body);

            // A for runs its incrementors after the body, and a do reads its condition there.
            foreach (ExpressionSyntax incrementor in incrementors)
            {
                this.Visit(incrementor);
            }

            this.Visit(after);
            this.evaluator.EndTurns(body);
        }

        // After the loop, a local has also what it had before an assignment of a turn.
        if (this.hadBefore.TryGetValue(key: body, value: out Dictionary<ISymbol, LoopState>? had))
        {
            this.hadBefore.Remove(body);
            this.assignedInThisRead.RemoveWhere(assigned => assigned.Turn == body);

            foreach (KeyValuePair<ISymbol, LoopState> local in had)
            {
                this.evaluator.Declare(local: local.Key, state: local.Value);
            }
        }
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
                    this.DeclareVar(pattern: variable, state: state);

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

    private void DeclareVar(VarPatternSyntax pattern, LoopState state)
    {
        if (pattern.Designation is ParenthesizedVariableDesignationSyntax several)
        {
            this.DeclareParts(several: several, value: MatchedValueOf(pattern), state: state);
        }
        else
        {
            this.DeclareDesignation(designation: pattern.Designation, state: state);
        }
    }

    /// <summary>
    ///     The variables of a var pattern with parts, as in
    ///     <c>var (key, value)</c>. For a value that is not a tuple, the pattern
    ///     calls a <c>Deconstruct</c> method, as a positional pattern does. Each
    ///     variable then has the state of the <c>out</c> parameter for it. A pair
    ///     of the platform that has a state for each of its parts gives each
    ///     variable its part. Otherwise, each variable has the state of the value.
    /// </summary>
    private void DeclareParts(ParenthesizedVariableDesignationSyntax several, ExpressionSyntax? value, LoopState state)
    {
        IMethodSymbol? deconstruct =
            this.region.Symbols.DeclaresDeconstruct || state.Members is { HasParts: true }
                ? (this.region.ModelFor(several).GetOperation(several) as IRecursivePatternOperation)?.DeconstructSymbol
                as IMethodSymbol
                : null;

        IReadOnlyList<LoopState>? states =
            deconstruct == null ? null : PartsOfAPair(pair: state, deconstruct: deconstruct);

        if (states == null
            && deconstruct != null
            && value != null
            && this.evaluator.FollowDeconstruct(call: several, method: deconstruct, value: value, owner: state)
                is { } followed)
        {
            this.Report(followed);
            states = OutStates(method: deconstruct, followed: followed);
        }

        for (int i = 0; i < several.Variables.Count; i++)
        {
            LoopState part = states == null ? state : i < states.Count ? states[i] : LoopState.None;

            // A part with parts of its own is one more Deconstruct, of the value of that part.
            if (several.Variables[i] is ParenthesizedVariableDesignationSyntax inner)
            {
                this.DeclareParts(several: inner, value: value, state: part);
            }
            else
            {
                this.DeclareDesignation(designation: several.Variables[i], state: part);
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
                // The getter of an iterator runs nothing at the pattern. The walk reports what it
                // found where something enumerates the result.
                if (LoopStateEvaluator.IsIteratorProperty(property))
                {
                    return this.evaluator.Waiting(read: name, state: getter.State);
                }

                this.Report(getter);

                return getter.State;
            }
        }

        if (owner.Members == null)
        {
            return owner;
        }

        // A pair that the evaluator saw made has a state for each of its parts.
        if (SodaFlowSymbols.HoldsParts(member.ContainingType) && owner.Members.Has(member))
        {
            return owner.Members.StateOf(member).FromIfUnknown(owner.Origin);
        }

        if (owner.Members.CanOwn(member))
        {
            return owner.Members.StateThrough(member).FromIfUnknown(owner.Origin);
        }

        // The members are those of an object that the owner contains, as a pair of the platform
        // contains its value. A member that is not a sequence is one of those values.
        ITypeSymbol? type =
            member switch
            {
                IPropertySymbol read => read.Type,
                IFieldSymbol read => read.Type,
                _ => null
            };

        return SodaFlowSymbols.IsSequence(type) ? owner : owner.AsElement;
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
            this.region.Symbols.DeclaresDeconstruct || state.Members is { HasParts: true }
                ? this.region.ModelFor(clause).GetSymbolInfo(clause).Symbol as IMethodSymbol
                : null;

        IReadOnlyList<LoopState>? states = method == null ? null : PartsOfAPair(pair: state, deconstruct: method);

        if (states == null
            && method != null
            && MatchedValueOf(clause) is { } value
            && this.evaluator.FollowDeconstruct(call: clause, method: method, value: value, owner: state)
                is { } followed)
        {
            this.Report(followed);
            states = OutStates(method: method, followed: followed);
        }

        for (int i = 0; i < clause.Subpatterns.Count; i++)
        {
            this.DeclarePattern(
                pattern: clause.Subpatterns[i].Pattern,
                state: states == null
                    ? state
                    : i < states.Count
                        ? states[i]
                        : LoopState.None);
        }
    }

    // The state that a followed Deconstruct method gives to each out parameter, in order.
    private static IReadOnlyList<LoopState> OutStates(IMethodSymbol method, Followed followed) =>
    [
        .. OutsOf(method)
            .Select(parameter =>
                followed.Outs.TryGetValue(key: parameter, value: out LoopState? given) ? given : LoopState.None)
    ];

    /// <summary>
    ///     The state that <c>Deconstruct</c> of the platform gives to each
    ///     target, for a pair that has a state for each of its parts. Each
    ///     <c>out</c> parameter has the name of its property. Null for each other
    ///     value.
    /// </summary>
    private static IReadOnlyList<LoopState>? PartsOfAPair(LoopState pair, IMethodSymbol deconstruct)
    {
        // An extension method gets the pair as its first argument, as the one for Tuple does.
        IMethodSymbol definition = (deconstruct.ReducedFrom ?? deconstruct).OriginalDefinition;
        ITypeSymbol holder = definition.IsStatic ? definition.Parameters[0].Type : definition.ContainingType;

        // A Deconstruct of this compilation can give each parameter a different value.
        if (pair.Members is not { } members || !SodaFlowSymbols.HoldsParts(holder))
        {
            return null;
        }

        List<LoopState> states = [];

        foreach (IParameterSymbol parameter in OutsOf(deconstruct))
        {
            if (SodaFlowSymbols.PartFor(holder: holder, parameter: parameter) is not { } part || !members.Has(part))
            {
                return null;
            }

            states.Add(members.StateOf(part).FromIfUnknown(pair.Origin));
        }

        return states;
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
        bool replaces,
        SyntaxNode? turn)
    {
        if (method == null || PartsOf(target) is not { } parts)
        {
            return false;
        }

        IReadOnlyList<LoopState>? states = PartsOfAPair(pair: owner, deconstruct: method);

        if (states == null)
        {
            if (this.evaluator.FollowDeconstruct(call: call, method: method, value: value, owner: owner)
                is not { } followed)
            {
                return false;
            }

            this.Report(followed);
            states = OutStates(method: method, followed: followed);
        }

        for (int i = 0; i < parts.Count; i++)
        {
            LoopState part = i < states.Count ? states[i] : LoopState.None;

            if (parts[i] is ExpressionSyntax expression)
            {
                this.AssignState(target: expression, state: part, replaces: replaces, turn: turn);
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
            // The indexer of a dictionary of the platform calls a member of the key. The first key
            // of an empty dictionary has no second key to compare it with.
            if (node.Left is ElementAccessExpressionSyntax keyed)
            {
                this.Report(
                    this.evaluator.FollowPlatformMember(
                        member: accessor,
                        collection: keyed.Expression,
                        values: keyed.ArgumentList.Arguments.Select(static index => index.Expression),
                        alone: this.evaluator.IsEmptyHere(collection: keyed.Expression, put: node),
                        others: [keyed.Expression],
                        before: null));
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
                this.AssignState(target: node.Left, state: added, replaces: false, turn: null);
            }

            // A coalescing assignment runs only when its target is null. Thus, the target keeps
            // the state that it had, and gets the state of the value also.
            if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            {
                this.Assign(target: node.Left, value: node.Right, replaces: false, turn: null);
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

        bool replaces = this.AlwaysRuns(assignment: node, turn: out SyntaxNode? turn);
        LoopState right = this.evaluator.Eval(node.Right);

        // A deconstruction of a value that is not a tuple calls a Deconstruct method.
        IMethodSymbol? deconstruct =
            (this.region.Symbols.DeclaresDeconstruct || right.Members is { HasParts: true })
            && node.Right is not TupleExpressionSyntax
            && PartsOf(node.Left) != null
                ? this.region.ModelFor(node).GetDeconstructionInfo(node).Method
                : null;

        if (!this.Deconstruct(
                call: node,
                target: node.Left,
                value: node.Right,
                owner: right,
                method: deconstruct,
                replaces: replaces,
                turn: turn))
        {
            this.Assign(target: node.Left, value: node.Right, replaces: replaces, turn: turn);
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
    /// <param name="assignment">The assignment.</param>
    /// <param name="turn">
    ///     The body of a loop whose each turn runs the assignment. The assignment
    ///     is then a statement at the first level of that body, and no statement
    ///     before it can end the turn. Null for each other assignment.
    /// </param>
    private bool AlwaysRuns(SyntaxNode assignment, out SyntaxNode? turn)
    {
        turn = null;

        return assignment.Parent is ExpressionStatementSyntax statement
               && this.AlwaysRunsAt(place: statement, turn: out turn);
    }

    /// <summary>
    ///     Where a call always runs, for the evaluator that follows the call.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <returns>
    ///     The first part is true when each run of the function of the call runs
    ///     the call. The second part is the body of the loop whose each turn always
    ///     runs the call, or null when no loop of that function does.
    /// </returns>
    public (bool InItsFunction, SyntaxNode? Turn) WhereACallRuns(SyntaxNode call)
    {
        if (WhereItAlwaysRuns(call) is not { } place)
        {
            return (false, null);
        }

        bool inItsFunction = this.AlwaysRunsAt(place: place, turn: out SyntaxNode? turn);

        return (inItsFunction, turn);
    }

    // The statement that always evaluates an expression when the statement runs. For the
    // expression that is the body of a lambda or of a local function, that expression or its
    // clause. Null when an operator, a conditional expression, or a function between the two can
    // skip the expression.
    private static SyntaxNode? WhereItAlwaysRuns(SyntaxNode expression)
    {
        SyntaxNode child = expression;

        for (SyntaxNode? parent = expression.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                // An expression gets to an if statement with no statement between only from its
                // condition.
                case ExpressionStatementSyntax
                    or LocalDeclarationStatementSyntax
                    or ReturnStatementSyntax
                    or IfStatementSyntax
                    or ArrowExpressionClauseSyntax:
                    return parent;
                case AnonymousFunctionExpressionSyntax lambda:
                    return lambda.ExpressionBody == child ? child : null;
                case StatementSyntax or SwitchExpressionArmSyntax or QueryClauseSyntax or SelectOrGroupClauseSyntax:
                    return null;
            }

            if (CanSkip(parent: parent, child: child))
            {
                return null;
            }
        }

        return null;
    }

    // True when an expression can skip one of its parts. Such a part is an arm of a conditional
    // expression, or the part after a conditional access. The right side of an operator that stops
    // at its left side is one also.
    private static bool CanSkip(SyntaxNode parent, SyntaxNode child) =>
        parent switch
        {
            ConditionalExpressionSyntax conditional => conditional.Condition != child,
            BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LogicalAndExpression
                or (int)SyntaxKind.LogicalOrExpression
                or (int)SyntaxKind.CoalesceExpression
            } binary => binary.Right == child,
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceAssignmentExpression } assignment =>
                assignment.Right == child,
            ConditionalAccessExpressionSyntax access => access.WhenNotNull == child,
            _ => false
        };

    // The same as AlwaysRuns, for a statement or for the expression that is the body of a function.
    private bool AlwaysRunsAt(SyntaxNode place, out SyntaxNode? turn)
    {
        turn = null;

        bool inALambda = false;

        foreach (SyntaxNode ancestor in place.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda when this.entered.Contains(lambda):
                    if (this.FirstExitOf(lambda) < place.SpanStart)
                    {
                        return false;
                    }

                    // A callee runs the lambda one time for each element. Each run always runs the
                    // statement, and a run is a turn.
                    if (!this.once.Contains(lambda))
                    {
                        turn = inALambda ? null : lambda.Body;

                        return false;
                    }

                    inALambda = true;

                    continue;
                // The walk does not look for a statement that can end the lambda before the
                // assignment. Thus, an assignment in a lambda is of no turn.
                case ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                    when !inALambda
                         && LoopStateEvaluator.LoopBodyOf(ancestor) is { } body
                         && this.FirstEndOfATurnOf(body) > place.SpanStart:
                    turn = body;

                    return false;
                // A call of the function that ends at a statement before the assignment does not
                // run the assignment.
                case AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or MemberDeclarationSyntax
                    or AccessorDeclarationSyntax:
                    // A return statement is no statement before itself.
                    return this.FirstExitOf(ancestor) >= place.SpanStart;
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

    // The position of the first statement that can end a call of a function before the last
    // statement. It is a return, a yield break, or a goto. A throw is not one of them, because the
    // walk reads no code that runs after an exception.
    private int FirstExitOf(SyntaxNode function)
    {
        if (!this.firstExits.TryGetValue(key: function, value: out int first))
        {
            first =
                function.DescendantNodes(node =>
                        node == function
                        || node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                    .FirstOrDefault(static node =>
                        node is ReturnStatementSyntax or GotoStatementSyntax
                        || node.IsKind(SyntaxKind.YieldBreakStatement))
                    ?.SpanStart
                ?? int.MaxValue;

            this.firstExits[function] = first;
        }

        return first;
    }

    // The position of the first statement that can end a turn before the end of the body. A turn
    // that ends there does not run the statements after it.
    private int FirstEndOfATurnOf(StatementSyntax body)
    {
        if (!this.firstEnds.TryGetValue(key: body, value: out int first))
        {
            first =
                body.DescendantNodesAndSelf(static node =>
                        node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                    .FirstOrDefault(node => EndsATurnOf(body: body, node: node))
                    ?.SpanStart
                ?? int.MaxValue;

            this.firstEnds[body] = first;
        }

        return first;
    }

    // A break statement is for the loop or the switch statement nearest to it, and a continue
    // statement is for the loop nearest to it. Each other jump ends the turn of each loop that it
    // is in.
    private static bool EndsATurnOf(SyntaxNode body, SyntaxNode node) =>
        node switch
        {
            ReturnStatementSyntax or ThrowStatementSyntax or ThrowExpressionSyntax or GotoStatementSyntax => true,
            YieldStatementSyntax yield => yield.IsKind(SyntaxKind.YieldBreakStatement),
            ContinueStatementSyntax =>
                node.Ancestors().Select(LoopStateEvaluator.LoopBodyOf).First(static found => found != null) == body,
            BreakStatementSyntax =>
                node.Ancestors()
                    .First(static around =>
                        around is SwitchStatementSyntax || LoopStateEvaluator.LoopBodyOf(around) != null)
                == body.Parent,
            _ => false
        };

    /// <summary>
    ///     Gives a target the state of a value. A deconstruction of a tuple
    ///     expression gives each part of the tuple to its own target.
    /// </summary>
    private void Assign(SyntaxNode target, ExpressionSyntax value, bool replaces, SyntaxNode? turn)
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
                    replaces: replaces,
                    turn: turn);
            }

            return;
        }

        LoopState whole = this.evaluator.Eval(value);

        if (target is IdentifierNameSyntax
            && this.region.SymbolOf(target) is { } named and (ILocalSymbol or IParameterSymbol))
        {
            this.evaluator.HoldsValueOf(
                local: named,
                value: value,
                replaces: replaces || IsNewInEachTurn(local: named, turn: turn));
        }

        // An element that gets a value puts it into the collection. Each other target that gets
        // a literal has the elements of that literal.
        if (target is ElementAccessExpressionSyntax indexed)
        {
            this.evaluator.Gets(collection: indexed.Expression, value: value);
        }
        else if (target is ExpressionSyntax written and not TupleExpressionSyntax and not DeclarationExpressionSyntax)
        {
            this.evaluator.GetsElementsOf(collection: written, literal: value);
        }

        // A deconstruction of a value that is not a tuple expression. Each target gets the state
        // of its element, when the evaluator knows the elements. Otherwise, each gets the state of
        // the value.
        IReadOnlyList<SyntaxNode>? parts = PartsOf(target);

        if (parts == null
            || whole.Members == null
            || this.region.TypeOf(value) is not INamedTypeSymbol { IsTupleType: true } type
            || type.TupleElements.Length != parts.Count)
        {
            this.AssignState(target: target, state: whole, replaces: replaces, turn: turn, from: value);

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
                this.AssignState(target: expression, state: element, replaces: replaces, turn: turn);
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
    /// <param name="turn">
    ///     The body of the loop whose each turn always runs the assignment. Null
    ///     when no loop does.
    /// </param>
    /// <param name="from">The expression of the value, when the state is the state of that expression.</param>
    private void AssignState(
        SyntaxNode target,
        LoopState state,
        bool replaces,
        SyntaxNode? turn,
        ExpressionSyntax? from = null)
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
                    this.AssignState(target: part.Expression, state: state, replaces: replaces, turn: turn);
                }

                return;
            // A value put into a collection is in the collection. A setter with no body to read
            // keeps each index also, as the indexer of a dictionary keeps its key.
            case ElementAccessExpressionSyntax element:
                this.evaluator.Absorb(target: element.Expression, state: state);

                if (this.region.SymbolOf(element) is IPropertySymbol { DeclaringSyntaxReferences.IsEmpty: true })
                {
                    foreach (ArgumentSyntax index in element.ArgumentList.Arguments)
                    {
                        this.evaluator.Absorb(
                            target: element.Expression,
                            state: this.evaluator.Eval(index.Expression));
                    }
                }

                return;
        }

        switch (this.region.SymbolOf(target))
        {
            case { } local and (ILocalSymbol or IParameterSymbol) when target is IdentifierNameSyntax:
                if (replaces || IsNewInEachTurn(local: local, turn: turn))
                {
                    this.evaluator.Assign(local: local, state: state, kept: from);
                }
                else if (turn != null)
                {
                    this.AssignInATurn(local: local, state: state, turn: turn);
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
                    state: state,
                    replaces: replaces,
                    turn: turn);

                break;
        }
    }

    // A local of the code around a loop has only the new value until the end of the turn. After the
    // loop, it can have the value from before the assignment also. The loop can run no turn, and a
    // turn can end before the next assignment.
    private void AssignInATurn(ISymbol local, LoopState state, SyntaxNode turn)
    {
        if (!this.hadBefore.TryGetValue(key: turn, value: out Dictionary<ISymbol, LoopState>? had))
        {
            had = new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default);
            this.hadBefore[turn] = had;
        }

        LoopState before = this.evaluator.AssignInATurn(local: local, state: state);

        // A second assignment in the same turn replaces a value that no code after the loop can
        // read. No statement between the two can end the turn.
        if (this.assignedInThisRead.Add((turn, local)))
        {
            had[local] = had.TryGetValue(key: local, value: out LoopState? earlier) ? earlier.Join(before) : before;
        }
    }

    // A local that the body of a loop declares is new in each turn. Thus, an assignment that each
    // turn always runs gives the local only the new value.
    private static bool IsNewInEachTurn(ISymbol local, SyntaxNode? turn) =>
        turn != null && LoopStateEvaluator.IsDeclaredIn(local: local, code: turn);

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

        ExpressionSyntax[] spreads =
            [.. node.Elements.OfType<SpreadElementSyntax>().Select(static spread => spread.Expression)];

        List<ExpressionSyntax[]> before = [];

        // Each element is one Add, and the set has the elements before it.
        foreach (ExpressionElementSyntax element in node.Elements.OfType<ExpressionElementSyntax>())
        {
            this.Report(
                this.evaluator.FollowPlatformMember(
                    member: add,
                    collection: node,
                    values: [element.Expression],
                    alone: before.Count == 0 && spreads.Length == 0,
                    others: spreads,
                    before: [.. before]));

            before.Add([element.Expression]);
        }
    }

    // The values of each element that an initializer gives before one of its elements. An element
    // with braces gives its values to one Add, and an element with an index gives its indexes.
    private static IEnumerable<IEnumerable<ExpressionSyntax>> Before(
        SyntaxNode initializer,
        ExpressionSyntax expression) =>
        ((InitializerExpressionSyntax)initializer).Expressions
        .TakeWhile(earlier => earlier != expression)
        .Select(static earlier =>
            earlier switch
            {
                InitializerExpressionSyntax several => several.Expressions,
                AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax keyed } =>
                    keyed.ArgumentList.Arguments.Select(static index => index.Expression),
                _ => [earlier]
            });

    // True for the first element of an initializer of a collection that is empty before it.
    private bool IsTheFirstElement(SyntaxNode initializer, ExpressionSyntax expression, SyntaxNode made) =>
        initializer is InitializerExpressionSyntax { Expressions.Count: > 0 } all
        && all.Expressions[0] == expression
        && this.IsNew(made);

    // True for a collection that is empty before its initializer. A constructor with no argument
    // makes such a collection. So does a constructor of the platform whose parameters are only
    // comparers and numbers, as a capacity is. A constructor that gets a sequence can put objects
    // into the collection.
    private bool IsNew(SyntaxNode made) =>
        made
            is ObjectCreationExpressionSyntax { ArgumentList: null or { Arguments.Count: 0 } }
            or ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 }
        || (this.region.SymbolOf(made) is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
            && SodaFlowSymbols.IsPlatform(constructor.ContainingType)
            && constructor.Parameters.All(static parameter =>
                SodaFlowSymbols.IsComparer(parameter.Type) || parameter.Type.SpecialType == SpecialType.System_Int32));

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
                        values: keyed.ArgumentList.Arguments.Select(static index => index.Expression),
                        alone: this.IsTheFirstElement(initializer: initializer, expression: expression, made: made),
                        others: this.IsNew(made) ? [] : [made],
                        before: Before(initializer: initializer, expression: expression)));
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
            this.Report(
                this.evaluator.FollowPlatformMember(
                    member: add,
                    collection: made,
                    values: values,
                    alone: this.IsTheFirstElement(initializer: initializer, expression: expression, made: made),
                    others: this.IsNew(made) ? [] : [made],
                    before: Before(initializer: initializer, expression: expression)));

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

                this.AssignOuts(
                    arguments: arguments,
                    method: method,
                    followed: followed,
                    moved: LoopState.None,
                    receiver: receiver);

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
                    oneTime: false,
                    source: receiver);

                this.RunGiven(method: method, receiver: receiver, arguments: arguments);
                this.RunThroughFunctions(call: node, method: method, given: moved);

                // The callee can put a value that it gets into its owner, and into a collection
                // that it gets as an argument.
                this.evaluator.CanChangeItsArguments(method: method, arguments: arguments, receiver: receiver);

                if (receiver != null && method.ReducedFrom == null)
                {
                    this.evaluator.CanGet(collection: receiver, method: method, arguments: arguments);

                    this.evaluator.Absorb(
                        target: receiver,
                        state: this.evaluator.Moved(invocation: node, method: method, withReceiver: false));

                    // A search reads its argument, and the collection does not get it. An object
                    // of this compilation that is not a sequence is not a collection. It can call
                    // a method of object, as Equals is, and that method keeps nothing.
                    if (!SodaFlowSymbols.IsASearch(method)
                        && this.region.TypeOf(receiver) is { } owner
                        && (SodaFlowSymbols.IsSequence(owner) || SodaFlowSymbols.IsPlatform(owner)))
                    {
                        foreach (ArgumentSyntax argument in arguments)
                        {
                            this.evaluator.Gets(collection: receiver, value: argument.Expression);
                        }
                    }
                }

                if (LoopStateEvaluator.ExchangeOf(
                        invocation: node,
                        definition: (method.ReducedFrom ?? method).OriginalDefinition) is { } exchange)
                {
                    // The result is the state that the location has now. The evaluator keeps the
                    // state of an expression, thus it must read the result before the location
                    // changes.
                    this.evaluator.Eval(node);

                    LoopState put = this.evaluator.Eval(exchange.Value);

                    // Exchange always puts the value there. CompareExchange can leave the old one.
                    bool always = method.Name == "Exchange";

                    this.AssignState(
                        target: exchange.Location,
                        state: always ? put : this.evaluator.Eval(exchange.Location).Join(put),
                        replaces: always,
                        turn: null);
                }
                else
                {
                    this.AssignOuts(
                        arguments: arguments,
                        method: method,
                        followed: null,
                        moved: moved,
                        receiver: null);

                    this.LinkOuts(arguments: arguments, owner: receiver);
                }

                // The callee can call a member of an object that it gets, as a sort calls CompareTo.
                this.Report(this.evaluator.FollowPlatformCall(invocation: node, method: method));

                break;
            }
            // The analyzer cannot see a pending value that goes to this callee.
            case CalleeKind.Unknown:
            {
                // The callee can keep a function that it gets, and run it after the call. Thus, the
                // walk reads such a function with no collection that holds only what went in.
                this.evaluator.RanCodeWithNoBody();

                this.VisitArguments(
                    arguments: arguments,
                    runs: Runs,
                    given: LoopState.None,
                    oneTime: false);

                this.RunGiven(method: method, receiver: receiver, arguments: arguments);

                IMethodSymbol called = method.ReducedFrom ?? method;

                // A method with no body to read can keep an object that it gets. An indexer of its
                // owner, or a foreach on the owner, can give the object back. Thus, the owner holds
                // it, as a collection holds the object of a put.
                if (receiver != null && method.ReducedFrom == null)
                {
                    foreach (ArgumentSyntax argument in arguments)
                    {
                        this.evaluator.Gets(collection: receiver, value: argument.Expression);
                    }
                }

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

    // A method of the platform gives an out variable one of the values that its owner has, as
    // TryGetValue does.
    private void LinkOuts(SeparatedSyntaxList<ArgumentSyntax> arguments, ExpressionSyntax? owner)
    {
        if (owner == null)
        {
            return;
        }

        foreach (ArgumentSyntax argument in
                 arguments.Where(static argument => argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)))
        {
            ISymbol? variable =
                argument.Expression switch
                {
                    DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single } =>
                        this.region.ModelFor(single).GetDeclaredSymbol(single),
                    IdentifierNameSyntax name => this.region.SymbolOf(name),
                    _ => null
                };

            if (variable is ILocalSymbol or IParameterSymbol)
            {
                this.evaluator.HoldsOneOf(local: variable, collection: owner);
            }
        }
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
                if (SodaFlowSymbols.TakesXmlContent(constructor.OriginalDefinition))
                {
                    this.RunContent(arguments.Arguments);
                }

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

        Followed? followed = this.evaluator.Follow(node);
        this.Report(followed);

        if (arguments != null && kind == CalleeKind.Body)
        {
            this.AssignOuts(
                arguments: arguments.Arguments,
                method: constructor,
                followed: followed,
                moved: LoopState.None,
                receiver: null);
        }

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
    /// <param name="source">
    ///     The collection whose values the callee gives to a lambda. A parameter
    ///     of the lambda is then a second name for one of those values. Null when
    ///     the callee gives no such value.
    /// </param>
    private void VisitArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        Func<ArgumentSyntax, int, bool> runs,
        LoopState given,
        bool oneTime,
        bool onlyLambdas = false,
        ExpressionSyntax? source = null)
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

            if (!given.IsNone || source != null)
            {
                SemanticModel model = this.region.ModelFor(lambda);

                foreach (ParameterSyntax parameter in LoopStateEvaluator.ParametersOf(lambda))
                {
                    if (model.GetDeclaredSymbol(parameter) is not { } declared)
                    {
                        continue;
                    }

                    if (!given.IsNone)
                    {
                        this.evaluator.Declare(local: declared, state: given);
                    }

                    // The callee gives the function the values that its owner has.
                    if (source != null)
                    {
                        this.evaluator.HoldsOneOf(local: declared, collection: source);
                    }
                }
            }

            // The lambda is part of this code. The walk reads it where it is, with the same
            // evaluator. A callee that does not run it one time runs it for each element. A run for
            // one element is a turn, and the walk reads the lambda again as it reads the body of a
            // loop.
            if (!oneTime)
            {
                this.evaluator.RunsForEachElement(lambda);
            }

            int before = this.evaluator.ShapeOfWhatALoopLeaves(lambda.Body);
            this.Visit(lambda.Body);

            if (oneTime)
            {
                continue;
            }

            this.VisitTheSecondTurn(loop: lambda, body: lambda.Body, before: before);
        }
    }

    /// <summary>
    ///     Reads what a callee with no body to read enumerates through a function
    ///     that it runs in the call.
    /// </summary>
    /// <remarks>
    ///     A callee that knows the result of the function as a sequence enumerates
    ///     that result, as <c>SelectMany</c> does. <c>Select</c> knows the result
    ///     as a type parameter, and keeps it. A function with no code to read can
    ///     enumerate each sequence that it gets, as <c>Enumerable.Count</c> given
    ///     by its name does. It can also give back what it got.
    /// </remarks>
    /// <param name="call">The call.</param>
    /// <param name="method">The callee.</param>
    /// <param name="given">The state of the values that the callee can give to a function.</param>
    private void RunThroughFunctions(InvocationExpressionSyntax call, IMethodSymbol method, LoopState given)
    {
        // A delegate has the types of its own parameters, and a method has those of its definition.
        IMethodSymbol callee =
            method.MethodKind == MethodKind.DelegateInvoke ? method : (method.ReducedFrom ?? method).OriginalDefinition;

        if (!this.region.Symbols.TakesAFunction(callee))
        {
            return;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        for (int i = 0; i < arguments.Count; i++)
        {
            ExpressionSyntax written = arguments[i].Expression;

            IParameterSymbol? parameter =
                LoopStateEvaluator.ParameterFor(method: method, argument: arguments[i], position: i);

            // The delegate of the definition has the types that the callee itself knows.
            if (parameter?.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } known }
                || !LoopStateEvaluator.RunsFunction(kind: CalleeKind.Mover, method: method, parameter: parameter))
            {
                continue;
            }

            bool enumeratesResult = SodaFlowSymbols.IsEnumerated(known.ReturnType);

            if (written is AnonymousFunctionExpressionSyntax lambda)
            {
                if (enumeratesResult)
                {
                    foreach (ExpressionSyntax result in LoopStateEvaluator.ResultsOf(lambda))
                    {
                        this.RunQueries(result);
                    }
                }

                continue;
            }

            // The delegate of the call has the types of the values that the function gets.
            if (this.region.ModelFor(written).GetTypeInfo(written).ConvertedType
                is not INamedTypeSymbol { DelegateInvokeMethod: { } invoked })
            {
                continue;
            }

            if (!this.evaluator.Eval(written).Functions.IsEmpty)
            {
                if (enumeratesResult)
                {
                    this.Run(state: this.evaluator.ResultOfFunction(written), enumerated: invoked.ReturnType);
                }

                continue;
            }

            // A method by its name has the types that the method itself knows. Each other delegate
            // has only its own types.
            IMethodSymbol? named = this.region.SymbolOf(written) as IMethodSymbol;

            ImmutableArray<IParameterSymbol> knows =
                named == null ? invoked.Parameters : (named.ReducedFrom ?? named).OriginalDefinition.Parameters;

            // The receiver in the name of an extension method is the argument of its first parameter.
            int receivers = named?.ReducedFrom == null ? 0 : 1;

            for (int position = 0; position < invoked.Parameters.Length; position++)
            {
                if (SodaFlowSymbols.IsEnumerated(knows[position + receivers].Type))
                {
                    this.Run(state: given, enumerated: invoked.Parameters[position].Type);
                }
            }

            if (enumeratesResult)
            {
                this.Run(state: given, enumerated: invoked.ReturnType);
            }
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

        // A serializer has no parameter that is a sequence, and it enumerates what it gets.
        if (SodaFlowSymbols.IsSerializer(callee))
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                this.Report(this.evaluator.FollowEnumeration(argument.Expression));

                this.Serialize(
                    written: argument.Expression,
                    state: this.evaluator.Eval(argument.Expression),
                    serializer: callee,
                    depth: 0);
            }

            return;
        }

        if (SodaFlowSymbols.TakesXmlContent(callee))
        {
            this.RunContent(arguments);

            return;
        }

        // The element kept its content, and enumerates it now.
        if (receiver != null && SodaFlowSymbols.WritesXmlContent(callee))
        {
            this.Run(state: this.evaluator.Eval(receiver), enumerated: null);

            return;
        }

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
                    this.evaluator.KnownTypeFor(method: method, argument: arguments[i], position: i)))
            {
                this.RunQueries(arguments[i].Expression);
            }
        }
    }

    // A member of LINQ to XML enumerates each sequence in the content that it gets. That includes
    // a sequence in an array or in a second sequence, and a sequence of this compilation.
    private void RunContent(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (ArgumentSyntax argument in arguments)
        {
            this.Report(this.evaluator.FollowEnumeration(argument.Expression));
            this.Run(state: this.evaluator.Eval(argument.Expression), enumerated: null);
        }
    }

    /// <summary>
    ///     Reads what a serializer runs when it writes a value. It enumerates
    ///     each query that the value is or contains. For an object of this
    ///     compilation, it calls the getter of each property that it writes. It
    ///     writes the result of that getter in the same way.
    /// </summary>
    /// <remarks>
    ///     An object of this compilation can keep a query that no property gives.
    ///     The serializer does not get that query. Thus, the walk reads the members
    ///     of such an object only through its getters.
    /// </remarks>
    private void Serialize(SyntaxNode written, LoopState state, IMethodSymbol serializer, int depth)
    {
        // The elements of a tuple are fields, and not each serializer writes a field.
        if (state.Members is { IsTuple: true } && !SodaFlowSymbols.WritesFields(serializer))
        {
            return;
        }

        this.Run(state: state, enumerated: null);

        // An object in an object in an object: the depth has an end, as a followed call has.
        if (state.Members is not { } members || depth > 3)
        {
            return;
        }

        // An anonymous object or a tuple: the serializer writes each value in it.
        if (!members.HasType)
        {
            foreach (KeyValuePair<ISymbol, LoopState> member in members.Members)
            {
                this.Serialize(written: written, state: member.Value, serializer: serializer, depth: depth + 1);
            }

            return;
        }

        foreach (IPropertySymbol property in members.Properties)
        {
            if (property.GetMethod is { } read
                && SodaFlowSymbols.IsSerialized(property: property, serializer: serializer)
                && this.evaluator.FollowImplied(call: written, member: read, owner: state, arguments: [])
                    is { } getter)
            {
                this.Report(getter);
                this.Serialize(written: written, state: getter.State, serializer: serializer, depth: depth + 1);
            }
        }
    }

    /// <summary>
    ///     Reads what each query in the value of an expression runs. The caller
    ///     is code that enumerates that value.
    /// </summary>
    private void RunQueries(ExpressionSyntax written)
    {
        LoopState state = this.evaluator.Eval(written);

        // Most values are not a query and contain none.
        if (!state.Queries.IsEmpty)
        {
            this.Run(state: state, enumerated: this.region.TypeOf(written));
        }
    }

    /// <summary>
    ///     Reads what the queries of a state run, for code that enumerates a value
    ///     with that state.
    /// </summary>
    /// <remarks>
    ///     A state has each query that the value is or contains, with no level.
    ///     The code enumerates the value itself. Thus, the walk reads only a query
    ///     that can be that value. A query in the value waits for the code that
    ///     enumerates it.
    /// </remarks>
    /// <param name="state">The state of the value.</param>
    /// <param name="enumerated">
    ///     The type of the value as the code enumerates it. Null for code that
    ///     enumerates each sequence in the value, as a serializer does.
    /// </param>
    private void Run(LoopState state, ITypeSymbol? enumerated)
    {
        foreach (QueryValue query in state.Queries.Where(query => this.CanBe(query: query, enumerated: enumerated)))
        {
            this.Run(query: query, to: this.sink);
        }
    }

    // A query is an object of one type: the type of the expression that makes it, which is an
    // interface. It can be the enumerated value when the compiler converts that type to the
    // enumerated type with no cast. The name of a property in a pattern has no type.
    private bool CanBe(QueryValue query, ITypeSymbol? enumerated)
    {
        if (enumerated == null || SaysLittle(enumerated))
        {
            return true;
        }

        ITypeSymbol? own = query.Call is ExpressionSyntax made ? this.region.TypeOf(made) : null;

        return own == null || SaysLittle(own) || this.region.Converts(from: own, to: enumerated);
    }

    // A type that does not tell a sequence from one of its elements. The IEnumerable that is not
    // generic and an enumerator do not have the type of the element. A type parameter can be a
    // sequence.
    private static bool SaysLittle(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Collections_IEnumerable
        || SodaFlowSymbols.IsEnumerator(type)
        || HasTypeParameter(type);

    private static bool HasTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => HasTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(HasTypeParameter),
            _ => false
        };

    /// <summary>
    ///     Reads what one query runs, one time. The code of the query is part of
    ///     the code that made it, thus a walker for that code reads it. A query
    ///     from a callee keeps what the walk of the callee found, and this gives
    ///     those findings.
    /// </summary>
    private void Run(QueryValue query, Action<Finding> to)
    {
        this.running?.Enumerates(query);

        if (query.TakeHeld() is { } held)
        {
            foreach (Finding finding in held)
            {
                to(finding);
            }

            // The findings that a query keeps are those of its own code. A source keeps its own.
            foreach (QueryValue source in query.Sources)
            {
                this.Run(query: source, to: to);
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
                : query.Definer.ReadBy(new SinkWalker(region: this.region, evaluator: query.Definer, sink: to));

        QueryValue? outer = walker.running;
        walker.running = query;
        walker.RunHere(query.Call);
        walker.running = outer;
    }

    private void RunHere(SyntaxNode call)
    {
        switch (call)
        {
            // The walk read the first source where the code made the query. The query enumerates
            // that source, and each other part runs, when something enumerates the query.
            case QueryExpressionSyntax query:
                this.RunQueries(query.FromClause.Expression);
                this.Visit(query.Body);

                return;
            case InvocationExpressionSyntax node when this.region.SymbolOf(node) is IMethodSymbol method:
            {
                SeparatedSyntaxList<ArgumentSyntax> arguments = node.ArgumentList.Arguments;
                bool isOfThePlatform = this.evaluator.Classify(call: node, method: method) == CalleeKind.Mover;

                LoopState given =
                    isOfThePlatform
                        ? this.evaluator.Moved(invocation: node, method: method, withReceiver: true).AsElement
                        : LoopState.None;

                // A query method of the platform: its functions, the sequences that it gets, and
                // the members that it calls on an object.
                if (isOfThePlatform)
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
                        given: given,
                        oneTime: false,
                        onlyLambdas: true,
                        source: LoopStateEvaluator.ReceiverOf(
                            symbols: this.region.Symbols,
                            invocation: node,
                            method: method));

                    // The state of the query has the queries of its sources, but a source can
                    // have a different type. The query enumerates each source.
                    this.RunGiven(
                        method: method,
                        receiver: LoopStateEvaluator.ReceiverOf(
                            symbols: this.region.Symbols,
                            invocation: node,
                            method: method),
                        arguments: arguments);

                    this.Report(this.evaluator.FollowPlatformCall(invocation: node, method: method));
                }

                // For an iterator, the body. For a query, a function that is not a lambda.
                this.Report(this.evaluator.Follow(node));

                if (isOfThePlatform)
                {
                    this.RunThroughFunctions(call: node, method: method, given: given);
                }

                return;
            }
            // The read of a property whose getter is an iterator.
            case MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or IdentifierNameSyntax:
                this.Report(this.evaluator.Follow(call));

                return;
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
    ///     that the callee gives to that parameter. Each other argument gets the
    ///     state of a value that the callee put into its object.
    /// </summary>
    /// <param name="arguments">The arguments of the call.</param>
    /// <param name="method">The callee.</param>
    /// <param name="followed">What the evaluator found in the body, if it has one.</param>
    /// <param name="moved">The state that a callee with no body gives to a parameter.</param>
    /// <param name="receiver">
    ///     The owner in the call. For an extension method, it is the argument
    ///     for the first parameter.
    /// </param>
    private void AssignOuts(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol method,
        Followed? followed,
        LoopState moved,
        ExpressionSyntax? receiver)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        int offset = method.ReducedFrom != null ? 1 : 0;

        if (offset == 1
            && receiver != null
            && followed != null
            && followed.Outs.TryGetValue(key: definition.Parameters[0], value: out LoopState? intoReceiver))
        {
            this.evaluator.Merge(target: receiver, state: intoReceiver);
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i + offset)
                is not { } parameter)
            {
                continue;
            }

            if (!arguments[i].RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
                && !arguments[i].RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
            {
                if (followed != null && followed.Outs.TryGetValue(key: parameter, value: out LoopState? put))
                {
                    this.evaluator.Merge(target: arguments[i].Expression, state: put);
                }

                continue;
            }

            LoopState state =
                followed == null
                    ? moved
                    : followed.Outs.TryGetValue(key: parameter, value: out LoopState? assigned)
                        ? assigned
                        : LoopState.None;

            this.AssignState(target: arguments[i].Expression, state: state, replaces: false, turn: null);
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

    // A getter runs at the read of its property. The getter of an iterator runs nothing there:
    // the read makes a query, and the walk reads the getter where something enumerates it.
    private void ReadGetter(SyntaxNode node, IPropertySymbol property)
    {
        if (LoopStateEvaluator.IsIteratorProperty(property))
        {
            this.evaluator.Wait(node);
        }
        else
        {
            this.Report(this.evaluator.Follow(node));
        }
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        // A name with no owner can be a property of the object that the code is in. Its getter
        // can read a member of that object.
        if (this.evaluator.HasThisMembers
            && this.region.SymbolOf(node)
                is IPropertySymbol { IsStatic: false, DeclaringSyntaxReferences.Length: > 0 } property)
        {
            this.ReadGetter(node: node, property: property);
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
                this.ReadGetter(node: node, property: property);
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
