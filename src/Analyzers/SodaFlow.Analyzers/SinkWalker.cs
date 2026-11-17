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
    // left. A value that goes through a local in each turn needs one read for each of those locals.
    // Each read moves the value to one more local, and each read walks the full body. Thus, the time
    // increases as the square of the number of those locals: 1.5 ms for 5 locals, 855 ms for 320.
    // The number is only a guard for a defect of the analyzer: no code has that many locals in a
    // chain.
    internal const int MaxReadsOfALoop = 1000;

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

    // The position after each await that can stop the call, with the block or the switch section
    // around it. The statements of that part after the position run only when the task completes.
    private readonly List<(SyntaxNode Block, int From)> later = [];

    // For an iterator that the code stops after its first item: the position after each yield
    // return, with the part around it. The code of that part after the position does not run.
    private readonly List<(SyntaxNode Block, int From)> afterTheItem = [];

    private bool takesOneItem;

    public override void Visit(SyntaxNode? node)
    {
        this.region.StopWhenCanceled();

        if (node != null
            && ((this.later.Count > 0 && IsAfter(parts: this.later, node: node))
                || (this.afterTheItem.Count > 0 && IsAfter(parts: this.afterTheItem, node: node))))
        {
            return;
        }

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

        // The compiler says where it adds a conversion or a test for true. The walk keeps no list
        // of such places, because a list missed places. The walk asks only in a compilation that
        // declares such an operator, and not where the compiler never adds one.
        if (this.region.Symbols is { DeclaresConversions: false, DeclaresTruth: false }
            || NeverConverts(expression))
        {
            return;
        }

        // The compiler gives the conversion of an expression in parentheses to the parentheses
        // also, and the walk asks there. It gives the test for true only to the expression in them.
        if (expression.Parent is not ParenthesizedExpressionSyntax)
        {
            this.Report(this.evaluator.FollowConversion(expression));
        }

        this.Report(this.evaluator.FollowTruth(expression));
    }

    // The compiler adds no conversion to the owner of a member, to a member name, to an assignment
    // target, or to a type name. This list is only for speed. A place that it does not have gets a
    // question with no result.
    private static bool NeverConverts(SyntaxNode expression) =>
        expression.Parent switch
        {
            MemberAccessExpressionSyntax
                or MemberBindingExpressionSyntax
                or ConditionalAccessExpressionSyntax
                or QualifiedNameSyntax
                or AliasQualifiedNameSyntax
                or TypeArgumentListSyntax
                or VariableDeclarationSyntax
                or ObjectCreationExpressionSyntax
                or TypeOfExpressionSyntax
                or DefaultExpressionSyntax
                or DeclarationExpressionSyntax
                or ArrayTypeSyntax
                or NullableTypeSyntax => true,
            InvocationExpressionSyntax call => call.Expression == expression,
            ElementAccessExpressionSyntax access => access.Expression == expression,
            AssignmentExpressionSyntax assignment => assignment.Left == expression,
            CastExpressionSyntax cast => cast.Type == expression,
            BinaryExpressionSyntax test =>
                test.Right == expression
                && (test.IsKind(SyntaxKind.IsExpression) || test.IsKind(SyntaxKind.AsExpression)),
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
            if (disposes && !this.EndsLater(declaration))
            {
                this.CheckDisposed(declarator.Initializer.Value);
            }
        }
    }

    // True when the block of a using declaration ends after an await of a task that is not
    // complete. The walk can read that await after the declaration. Thus, the test looks at each
    // await of the block. An await before the declaration that stops the call stops it before the
    // declaration also, and the walk does not read the declaration.
    private bool EndsLater(SyntaxNode declaration)
    {
        if (declaration.Parent?.Parent is not { } scope)
        {
            return false;
        }

        int end = scope.Span.End - 1;

        return scope
            .DescendantNodes(static node =>
                node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(node =>
                node is AwaitExpressionSyntax or CommonForEachStatementSyntax
                && this.LaterAfter(node) is ({ } part, _)
                && part.Span.Contains(end));
    }

    // True when the code at a position runs only after an await completes.
    private bool RunsLaterAt(int position)
    {
        foreach ((SyntaxNode block, int from) in this.later)
        {
            if (position >= from && block.Span.Contains(position))
            {
                return true;
            }
        }

        return false;
    }

    // The Dispose that a using calls on its resource. An object of this compilation has a body to
    // read. The Dispose of a bindable action of the library reads its enablement cell, as a call of
    // that Dispose in the code does.
    private void CheckDisposed(ExpressionSyntax resource)
    {
        if (this.evaluator.FollowDispose(resource) is { } followed)
        {
            this.Report(followed);

            return;
        }

        LoopState state = this.evaluator.Eval(resource);

        if (state.Pending && this.region.Symbols.IsBindableAction(this.region.TypeOf(resource)))
        {
            this.CheckRead(node: resource, state: state, name: "Dispose");
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

        // The statement calls Dispose on each resource when the code leaves it. An await in it can
        // make that run later.
        if (this.RunsLaterAt(node.Span.End - 1))
        {
            return;
        }

        IEnumerable<ExpressionSyntax> resources =
            node.Expression != null
                ? [node.Expression]
                : (node.Declaration?.Variables ?? default)
                .Select(static declarator => declarator.Initializer?.Value)
                .OfType<ExpressionSyntax>();

        foreach (ExpressionSyntax resource in resources)
        {
            this.CheckDisposed(resource);
        }
    }

    // An interpolation calls ToString on its value.
    public override void VisitInterpolation(InterpolationSyntax node)
    {
        base.VisitInterpolation(node);
        this.Report(this.evaluator.FollowInterpolation(node));
    }

    // An async function runs in the call until it awaits a task that is not complete. The rest
    // runs when the task completes, which is after the call. CompleteTasks says which tasks the
    // walk knows as complete before the await. A local function or a stored lambda has a walker
    // of its own. Thus, the positions of this walker do not apply to its code.
    public override void VisitAwaitExpression(AwaitExpressionSyntax node)
    {
        base.VisitAwaitExpression(node);

        if (this.LaterAfter(node) is { } part)
        {
            this.later.Add(part);
        }
    }

    // The part of the code that runs after the task of an await completes, and the position where
    // it starts. Null for an await that cannot stop the call, and where the walk does not know the
    // part.
    private (SyntaxNode Block, int From)? LaterAfter(SyntaxNode node) =>
        node switch
        {
            AwaitExpressionSyntax awaited =>
                !CompleteTasks.IsComplete(region: this.region, task: awaited.Expression)
                && LaterPartOf(awaited) is { } block
                    ? (block, awaited.Span.End)
                    : null,
            CommonForEachStatementSyntax loop when loop.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) =>
                this.LaterAfterEachItem(loop),
            _ => null
        };

    private void AwaitsEachItem(SyntaxNode node)
    {
        if (this.LaterAfter(node) is { } part)
        {
            this.later.Add(part);
        }
    }

    // An await foreach awaits each MoveNextAsync. The body runs in the call only for an item that
    // the sequence gives in the call. The code after the loop runs in the call only when each item
    // comes in the call.
    private (SyntaxNode Block, int From)? LaterAfterEachItem(CommonForEachStatementSyntax loop)
    {
        (bool first, bool each) = CompleteTasks.ItemsComeAtOnce(region: this.region, sequence: loop.Expression);

        return !each && PartAround(loop) is { } block
            ? (block, first ? loop.Statement.Span.End : loop.Statement.SpanStart)
            : null;
    }

    // The largest block or switch section that always runs the statement of the await to its end.
    // Null when a statement, a branch, or a function between the two can skip the await. A goto to
    // a label after the await can skip it also.
    private static SyntaxNode? LaterPartOf(SyntaxNode node) =>
        (WhereItAlwaysRuns(node) ?? HeaderOf(node)) is StatementSyntax statement ? PartAround(statement) : null;

    private static SyntaxNode? PartAround(SyntaxNode statement)
    {
        SyntaxNode? block = null;
        SyntaxNode part = statement;

        for (SyntaxNode? place = statement.Parent;
             place != null && RunsToItsEnd(statement: place, part: part);
             part = place, place = place.Parent)
        {
            block = place is BlockSyntax or SwitchSectionSyntax ? place : block;
        }

        return block?.DescendantNodes().OfType<LabeledStatementSyntax>().Any() == false ? block : null;
    }

    // The statement that always runs an expression of its header before its other parts. That
    // expression is the collection of a foreach, the value of a switch, the first condition of a
    // while, or the resource of a using. Null for each other expression.
    private static StatementSyntax? HeaderOf(SyntaxNode expression)
    {
        SyntaxNode child = expression;

        for (SyntaxNode? parent = expression.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case CommonForEachStatementSyntax loop:
                    return loop.Expression == child ? loop : null;
                case SwitchStatementSyntax choice:
                    return choice.Expression == child ? choice : null;
                case WhileStatementSyntax loop:
                    return loop.Condition == child ? loop : null;
                case UsingStatementSyntax used:
                    return used.Expression == child || used.Declaration == child ? used : null;
                case StatementSyntax
                    or AnonymousFunctionExpressionSyntax
                    or SwitchExpressionArmSyntax
                    or QueryClauseSyntax
                    or SelectOrGroupClauseSyntax:
                    return null;
            }

            if (CanSkip(parent: parent, child: child))
            {
                return null;
            }
        }

        return null;
    }

    // True when each run of a statement runs a part of it to its end. A do runs its body one time at
    // least. A try runs its block. With a catch clause, the code after the try can run when the
    // block stops before the await. Thus, only a try with no catch clause counts. A switch section
    // runs its statements in their order, as a block does.
    private static bool RunsToItsEnd(SyntaxNode statement, SyntaxNode part) =>
        statement switch
        {
            BlockSyntax
                or SwitchSectionSyntax
                or UsingStatementSyntax
                or LockStatementSyntax
                or CheckedStatementSyntax
                or UnsafeStatementSyntax
                or FixedStatementSyntax => true,
            DoStatementSyntax loop => loop.Statement == part,
            TryStatementSyntax attempt => attempt.Block == part && attempt.Catches.Count == 0,
            _ => false
        };

    // True when each turn of a loop runs an await of a task that is not complete. That await is in
    // the body, and its block holds the body. The next turn then runs after the task completes,
    // which is after the call, and the walk reads no second turn. A continue can start the next
    // turn before the await. Synchronous code has no such await, and the test is one test of an
    // empty list. A yield return of an iterator that the code stops after its first item ends each
    // turn also.
    private bool AwaitEndsEachTurn(SyntaxNode body) =>
        (this.later.Count > 0 || this.afterTheItem.Count > 0)
        && body is BlockSyntax turn
        && this.later.Concat(this.afterTheItem).Any(awaited =>
            awaited.Block.Span.Contains(turn.Span)
            && awaited.From > turn.SpanStart
            && awaited.From <= turn.Span.End)
        && !turn.DescendantNodes(descendIntoChildren: static node =>
                node is not (AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or ForStatementSyntax
                    or CommonForEachStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax))
            .OfType<ContinueStatementSyntax>()
            .Any();

    private static bool IsAfter(List<(SyntaxNode Block, int From)> parts, SyntaxNode node)
    {
        foreach ((SyntaxNode block, int from) in parts)
        {
            if (node.SpanStart >= from && block.Span.Contains(node.Span))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Makes the walk stop the iterator after its first item. The code after
    ///     the first yield return on each path does not run. A finally block
    ///     around that yield return runs at the Dispose of the enumerator.
    /// </summary>
    public void TakesOneItem() => this.takesOneItem = true;

    public override void VisitYieldStatement(YieldStatementSyntax node)
    {
        base.VisitYieldStatement(node);

        // A yield break ends the iterator also. Thus, the walk stops at each yield statement.
        if (this.takesOneItem)
        {
            this.StopsAfter(node);
        }
    }

    // Records the code that does not run after the yield return of the first item. A try statement
    // with a finally block splits that code: the finally block runs, and the code after the try
    // statement does not. A goto to a label after the yield return can skip it.
    private void StopsAfter(SyntaxNode yielded)
    {
        List<(SyntaxNode Block, int From)> parts = [];
        SyntaxNode? block = null;
        int from = yielded.Span.End;
        SyntaxNode part = yielded;

        for (SyntaxNode? place = yielded.Parent;
             place != null && RunsToItsEnd(statement: place, part: part);
             part = place, place = place.Parent)
        {
            if (place is TryStatementSyntax { Finally: not null } attempt)
            {
                if (block != null)
                {
                    parts.Add((block, from));
                }

                from = attempt.Span.End;
            }

            block = place is BlockSyntax or SwitchSectionSyntax ? place : block;
        }

        if (block != null)
        {
            parts.Add((block, from));
        }

        if (parts.Count > 0 && !parts[parts.Count - 1].Block.DescendantNodes().OfType<LabeledStatementSyntax>().Any())
        {
            this.afterTheItem.AddRange(parts);
        }
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
        this.AwaitsEachItem(node);

        // The collection has the state of all its elements, thus each element has that state. An
        // enumerator of this compilation gives what its Current returns also.
        ITypeSymbol? type = this.region.ModelFor(node).GetForEachStatementInfo(node).ElementType;
        LoopState collection = this.evaluator.Eval(node.Expression);
        LoopState ofTheCollection = LoopStateEvaluator.ElementOf(collection: collection, type: type);

        LoopState enumerated = this.Enumerate(node: node, enumeratorState: out LoopState? enumeratorState);
        LoopState elements = ofTheCollection.Join(enumerated);

        // The first turn has the first element, when the walk knows the positions. Thus, an await
        // that stops the first turn stops it before a later element.
        (LoopState First, int Count)? start = enumerated.IsNone ? this.evaluator.FirstElementOf(node.Expression) : null;
        LoopState? first = start is ({ } one, _) && one.Shape != elements.Shape ? one : null;

        // The conversion of each element to the type of the variable has no node of its own.
        if (this.region.Symbols.DeclaresConversions
            && this.region.ModelFor(node).GetForEachStatementInfo(node).ElementConversion
                is { IsUserDefined: true, MethodSymbol: { } conversion })
        {
            this.Report(
                this.evaluator.FollowImplied(
                    call: node.Expression,
                    member: conversion,
                    owner: null,
                    arguments: [(node.Expression, elements)]));
        }

        ILocalSymbol? element = this.region.ModelFor(node).GetDeclaredSymbol(node);

        if (element != null)
        {
            this.evaluator.Declare(local: element, state: first ?? elements);
            this.evaluator.HoldsOneOf(local: element, collection: node.Expression);
        }

        // Each turn reads the array of the start, also when the body gives the name a new array.
        ArrayOfAForeach? array = element != null ? this.evaluator.ArrayOf(loop: node, state: collection) : null;

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        this.evaluator.StartsEach(node);
        this.Visit(node.Statement);

        // A later turn can have each element. The variable then changes what the first read
        // leaves, and the walk reads a second turn. A later turn of an array also has what a turn
        // before put into the array.
        Action? startsALaterTurn =
            element != null && array != null && this.evaluator.PutsIntoALaterElement(loop: node, array: array)
                ? this.StartsALaterTurn(
                    loop: node,
                    element: element,
                    elements: elements,
                    array: array,
                    type: type,
                    count: start?.Count)
                : null;

        if (startsALaterTurn != null)
        {
            startsALaterTurn();
        }
        else if (first != null && element != null)
        {
            this.evaluator.Declare(local: element, state: elements);
        }

        this.VisitTheSecondTurn(loop: node, body: node.Statement, before: before, startsATurn: startsALaterTurn);

        // The loop has a turn for each element when it always runs and no statement ends a turn
        // or the loop before its end.
        this.evaluator.EndsEach(
            loop: node,
            hasATurnForEachElement: this.FirstEndOfATurnOf(node.Statement) == int.MaxValue
                                    && this.AlwaysRunsAt(place: node, turn: out SyntaxNode? around)
                                    && around == null);

        this.DisposeTheEnumerator(node: node, enumerator: enumeratorState);
    }

    // Gives the variable of a foreach over an array its elements for the next turn: the elements of
    // the start and the elements now. Each turn reads the element at its own position. Thus, no
    // turn reads what the last turn puts into the array. The read for the last turn, and each read
    // after it, keeps the elements that the reads before it gave.
    private Action StartsALaterTurn(
        ForEachStatementSyntax loop,
        ISymbol element,
        LoopState elements,
        ArrayOfAForeach array,
        ITypeSymbol? type,
        int? count)
    {
        int readsThatPutForALaterTurn = (count ?? int.MaxValue) - 1;
        LoopState later = elements;

        return () =>
        {
            if (readsThatPutForALaterTurn > 0)
            {
                readsThatPutForALaterTurn--;
                later = elements.Join(this.evaluator.ElementsNow(loop: loop, array: array, type: type));
            }

            this.evaluator.Declare(local: element, state: later);
        };
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        this.Visit(node.Expression);
        this.RunQueries(node.Expression);
        this.AwaitsEachItem(node);

        LoopState ofTheCollection = this.evaluator.Eval(node.Expression).AsElement;
        LoopState enumerated = this.Enumerate(node: node, enumeratorState: out LoopState? enumeratorState);
        LoopState elements = ofTheCollection.Join(enumerated);

        IMethodSymbol? method =
            this.region.Symbols.DeclaresDeconstruct
            || this.region.Symbols.DeclaresRecords
            || elements.Members is { HasParts: true }
                ? this.region.ModelFor(node).GetDeconstructionInfo(node).Method
                : null;

        // The first turn has the first element, as for a foreach with one variable.
        LoopState? first =
            enumerated.IsNone
            && this.evaluator.FirstElementOf(node.Expression) is ({ } one, _)
            && one.Shape != elements.Shape
                ? one
                : null;

        this.DeconstructAnElement(node: node, method: method, element: first ?? elements);

        // The walk keeps one result for each Deconstruct that it follows at a node. Thus, it forgets
        // the result for the first element, and the later elements get a result of their own.
        if (first != null && method != null)
        {
            this.evaluator.ForgetsTheDeconstructOf(call: node.Variable, method: method);
        }

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        this.Visit(node.Statement);

        // A later turn can have each element. See VisitForEachStatement.
        if (first != null)
        {
            this.DeconstructAnElement(node: node, method: method, element: elements);
        }

        this.VisitTheSecondTurn(loop: node, body: node.Statement, before: before);
        this.DisposeTheEnumerator(node: node, enumerator: enumeratorState);
    }

    // Gives each part of the variable of a foreach statement its part of an element.
    private void DeconstructAnElement(ForEachVariableStatementSyntax node, IMethodSymbol? method, LoopState element)
    {
        if (!this.Deconstruct(
                call: node.Variable,
                target: node.Variable,
                value: node.Expression,
                owner: element,
                method: method,
                replaces: false,
                turn: null))
        {
            this.AssignState(target: node.Variable, state: element, replaces: false, turn: null);
        }
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

    // A branch that a constant condition rules out never runs. Thus, the walk does not read it.
    public override void VisitIfStatement(IfStatementSyntax node)
    {
        if (this.ConstantOf(node.Condition) is not { } always)
        {
            base.VisitIfStatement(node);

            return;
        }

        this.Visit(node.Condition);
        this.Visit(always ? node.Statement : node.Else);
    }

    // The value of a condition that is a constant. Null for each other condition. Only names,
    // literals, and operators can make a constant. Thus, the walk asks the compiler only about
    // such a condition.
    private bool? ConstantOf(SyntaxNode condition) =>
        condition.DescendantNodesAndSelf()
            .All(static part =>
                part
                    is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax
                    or LiteralExpressionSyntax
                    or ParenthesizedExpressionSyntax
                    or PrefixUnaryExpressionSyntax
                    or BinaryExpressionSyntax)
        && this.region.ModelFor(condition).GetConstantValue(condition) is { Value: bool value }
            ? value
            : null;

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        // A loop whose condition is the constant false runs no turn.
        if (this.ConstantOf(node.Condition) == false)
        {
            this.Visit(node.Condition);

            return;
        }

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Statement);
        base.VisitWhileStatement(node);
        this.VisitTheSecondTurn(loop: node, body: node.Statement, before: before, condition: node.Condition);
    }

    // A goto to a label that comes before it in the block runs the statements from the label again.
    // The walk reads those statements as the turn of a loop, as it reads a do statement. The labeled
    // statement stands for the turn. Thus, only a local that it declares is new in each turn. A local
    // that a later statement of the turn declares is the same variable in each turn.
    public override void VisitBlock(BlockSyntax node)
    {
        SyntaxList<StatementSyntax> statements = node.Statements;

        if (!statements.Any(static statement => statement is LabeledStatementSyntax))
        {
            base.VisitBlock(node);

            return;
        }

        int first = 0;

        while (first < statements.Count)
        {
            int last =
                statements[first] is LabeledStatementSyntax label
                    ? LastJumpBack(statements: statements, label: label, from: first)
                    : -1;

            if (last < 0)
            {
                this.Visit(statements[first]);
                first++;

                continue;
            }

            StatementSyntax[] turn = [.. statements.Skip(first).Take(last - first + 1)];
            int before = this.evaluator.ShapeOfWhatALoopLeaves(statements[first]);

            foreach (StatementSyntax part in turn)
            {
                this.Visit(part);
            }

            this.VisitTheSecondTurn(loop: node, body: statements[first], before: before, parts: turn);
            first = last + 1;
        }
    }

    // The position in the statements of the last one, from the label on, that has a goto to the
    // label. The result is -1 when no statement has one. A lambda or a local function cannot go to
    // a label of the code around it.
    private static int LastJumpBack(SyntaxList<StatementSyntax> statements, LabeledStatementSyntax label, int from)
    {
        string name = label.Identifier.ValueText;

        for (int last = statements.Count - 1; last >= from; last--)
        {
            if (statements[last]
                .DescendantNodesAndSelf(static node =>
                    node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                .OfType<GotoStatementSyntax>()
                .Any(jump =>
                    jump.IsKind(SyntaxKind.GotoStatement)
                    && jump.Expression is IdentifierNameSyntax target
                    && target.Identifier.ValueText == name))
            {
                return last;
            }
        }

        return -1;
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
    /// <param name="parts">
    ///     The statements of a turn that is not one node, in the order in which
    ///     they run. The walk reads them in place of the body.
    /// </param>
    /// <param name="startsATurn">
    ///     What the walk does at the end of each read, before the next turn.
    /// </param>
    private void VisitTheSecondTurn(
        SyntaxNode loop,
        SyntaxNode body,
        int before,
        SyntaxNode? condition = null,
        SeparatedSyntaxList<ExpressionSyntax> incrementors = default,
        SyntaxNode? after = null,
        IReadOnlyList<SyntaxNode>? parts = null,
        Action? startsATurn = null)
    {
        this.evaluator.EndTurns(body);

        // A read that leaves what a read before left gives the next read nothing new. That is
        // true for the first read also. When it changes no location that is older than the turn,
        // the second turn is the same as the first one.
        HashSet<int> left = [before];
        bool hasOneTurn = EndsTheLoopOnEachPath(loop: loop, body: body);

        // A value can come to its read only after two or more turns. The walk reads the loop again
        // while a read gives a location a new state. The locals of the body keep the state of the
        // last read, because the result of a function can be a local of a loop.
        for (int read = 2;
             !hasOneTurn
             && read <= MaxReadsOfALoop
             && !this.AwaitEndsEachTurn(body)
             && left.Add(this.evaluator.ShapeOfWhatALoopLeaves(body));
             read++)
        {
            this.evaluator.StartTheSecondTurn(loop: loop, turn: body);

            // The walk reads a lambda that a callee runs in the call one time. The next turn runs
            // it again.
            this.entered.RemoveWhere(lambda => lambda != loop && LoopStateEvaluator.IsIn(node: lambda, code: loop));
            this.assignedInThisRead.RemoveWhere(assigned => assigned.Turn == body);

            this.Visit(condition);

            foreach (SyntaxNode part in parts ?? [body])
            {
                this.Visit(part);
            }

            // A for runs its incrementors after the body, and a do reads its condition there.
            foreach (ExpressionSyntax incrementor in incrementors)
            {
                this.Visit(incrementor);
            }

            this.Visit(after);
            this.evaluator.EndTurns(body);
            startsATurn?.Invoke();
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

        this.evaluator.GivesBackWhatATurnReplaced(body);
    }

    // True when each path through the body of a loop statement ends the loop. The loop then has one
    // turn at most. A continue for the loop starts the next turn. The test reads the last statement
    // of the body first, and that statement is usually not a jump.
    public static bool EndsTheLoopOnEachPath(SyntaxNode loop, SyntaxNode body) =>
        loop is ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
        && body is StatementSyntax turn
        && EndsTheLoop(turn)
        && turn.DescendantNodes(static node =>
                node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<ContinueStatementSyntax>()
            .All(next => next.Ancestors().Select(LoopStateEvaluator.LoopBodyOf).First(static found => found != null)
                         != turn);

    // True for a statement that ends the loop around it on each path. Each branch ends with a
    // break, a return, or a throw. A break in a block or in an if is for that loop.
    private static bool EndsTheLoop(StatementSyntax statement) =>
        statement switch
        {
            BreakStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax => true,
            YieldStatementSyntax yield => yield.IsKind(SyntaxKind.YieldBreakStatement),
            BlockSyntax { Statements: { Count: > 0 } statements } => EndsTheLoop(statements.Last()),
            IfStatementSyntax { Else: { } otherwise } choice =>
                EndsTheLoop(choice.Statement) && EndsTheLoop(otherwise.Statement),
            _ => false
        };

    /// <summary>
    ///     The calls that a foreach makes on a collection of this compilation:
    ///     <c>GetEnumerator</c>, then <c>MoveNext</c> and <c>Current</c> on an
    ///     enumerator of this compilation. The result is the state of what
    ///     <c>Current</c> returns.
    /// </summary>
    /// <param name="node">The foreach statement.</param>
    /// <param name="enumeratorState">
    ///     The state of the enumerator, when it is an object of this
    ///     compilation. Null for each other enumerator.
    /// </param>
    private LoopState Enumerate(CommonForEachStatementSyntax node, out LoopState? enumeratorState)
    {
        enumeratorState = null;

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

        enumeratorState = enumerator.State;

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

    // A foreach calls Dispose on an enumerator that has it, when the loop ends. An await in the body
    // can make the end of the last turn run later.
    private void DisposeTheEnumerator(CommonForEachStatementSyntax node, LoopState? enumerator)
    {
        if (enumerator == null
            || this.RunsLaterAt(node.Span.End - 1)
            || !this.region.Symbols.DeclaresDispose
            || this.region.ModelFor(node).GetForEachStatementInfo(node).DisposeMethod is not { } dispose)
        {
            return;
        }

        this.Report(
            this.evaluator.FollowImplied(call: node.Expression, member: dispose, owner: enumerator, arguments: []));
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

        // A goto case or a goto default to a section at or before its own runs that section again.
        // The walk reads the sections from that one to the last such jump as the turn of a loop, as
        // for a label.
        if (this.JumpsBack(node) is not (var first, var last))
        {
            base.VisitSwitchStatement(node);

            return;
        }

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Sections[first]);
        base.VisitSwitchStatement(node);

        this.VisitTheSecondTurn(
            loop: node,
            body: node.Sections[first],
            before: before,
            parts: [.. node.Sections.Skip(first).Take(last - first + 1)]);
    }

    // The first section that a goto case or a goto default goes back to, and the last section with
    // such a jump. Null when no jump goes back. A goto case of a switch inside a section goes to a
    // label of that switch, which no section here has. A lambda or a local function cannot jump out
    // of its code.
    private (int First, int Last)? JumpsBack(SwitchStatementSyntax node)
    {
        GotoStatementSyntax[] jumps =
        [
            .. node.Sections
                .SelectMany(static section => section.Statements)
                .SelectMany(static statement =>
                    statement.DescendantNodesAndSelf(static inner =>
                        inner is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
                .OfType<GotoStatementSyntax>()
                .Where(static jump => !jump.IsKind(SyntaxKind.GotoStatement))
        ];

        if (jumps.Length == 0)
        {
            return null;
        }

        SemanticModel model = this.region.ModelFor(node);
        (int First, int Last)? found = null;

        foreach (GotoStatementSyntax jump in jumps)
        {
            int from = node.Sections.IndexOf(section => section.Span.Contains(jump.Span));

            int to =
                model.GetOperation(jump) is IBranchOperation { Target: { } target }
                    ? node.Sections.IndexOf(section =>
                        section.Labels.Any(label =>
                            SymbolEqualityComparer.Default.Equals(x: model.GetDeclaredSymbol(label), y: target)))
                    : -1;

            if (to >= 0 && to <= from)
            {
                found =
                    found is (var first, var last)
                        ? (Math.Min(val1: first, val2: to), Math.Max(val1: last, val2: from))
                        : (to, from);
            }
        }

        return found;
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
                    this.Report(this.evaluator.FollowListPattern(pattern: list, owner: state));
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
            this.region.Symbols.DeclaresDeconstruct
            || this.region.Symbols.DeclaresRecords
            || state.Members is { HasParts: true }
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
                if (this.region.Symbols.ReadsItsOwner(property))
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

        return LoopStateEvaluator.StateOfAMember(owner: owner, member: member);
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
            || this.region.Symbols.DeclaresRecords
            || state.Members is { HasParts: true }
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
            // Code that can know the library can read a value in its setter, as in a method. A
            // setter with no body to read is then such code.
            if (node.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && this.evaluator.Classify(call: node, method: accessor) == CalleeKind.Unknown)
            {
                this.CheckEscape(callee: accessor.AssociatedSymbol ?? accessor, argument: node.Right);
            }

            // The setter of a two-way bindable reads the initial value of its cell before it
            // compares the new value. A compound assignment reads the getter first, and the walk
            // reports that read.
            if (node.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && node.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" } written
                && this.region.Symbols.IsSodaFlow(accessor))
            {
                this.CheckRead(node: node.Left, owner: written.Expression, name: "Value");
            }

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

            // A delegate that loses the last occurrence of a function, as in handlers -= handler.
            if (node.IsKind(SyntaxKind.SubtractAssignmentExpression)
                && this.AlwaysRuns(assignment: node, turn: out SyntaxNode? removedIn)
                && removedIn == null
                && this.evaluator.AfterTheLastRemove(node) is { } left)
            {
                this.AssignState(target: node.Left, state: left, replaces: true, turn: null);
            }

            // A coalescing assignment runs only when its target is null. Thus, the target keeps
            // the state that it had, and gets the state of the value also. An element at a
            // position that has no object always gets the value. An element at a position that
            // has an object for sure gets nothing, and the positions stay.
            if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression) && !this.evaluator.HasAnObjectAt(node.Left))
            {
                SyntaxNode? itsTurn = null;

                bool assigns =
                    this.evaluator.HasNoObjectAt(node.Left) && this.AlwaysRuns(assignment: node, turn: out itsTurn);

                this.Assign(target: node.Left, value: node.Right, replaces: assigns, turn: itsTurn);
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

        // A deconstruction of a value that is not a tuple calls a Deconstruct method. The compiler
        // gives no such method for a tuple expression, thus the test for a tuple is only for speed.
        IMethodSymbol? deconstruct =
            (this.region.Symbols.DeclaresDeconstruct
             || this.region.Symbols.DeclaresRecords
             || right.Members is { HasParts: true })
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

        return assignment.Parent switch
        {
            ExpressionStatementSyntax statement => this.AlwaysRunsAt(place: statement, turn: out turn),
            // The expression that is the body of a function is the one statement of that function.
            ArrowExpressionClauseSyntax clause => this.AlwaysRunsAt(place: clause, turn: out turn),
            AnonymousFunctionExpressionSyntax lambda when lambda.ExpressionBody == assignment =>
                this.AlwaysRunsAt(place: assignment, turn: out turn),
            _ => false
        };
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

        // A new local of a deconstruction gets its part of a tuple expression as a declaration
        // does. Thus, it can be a name of the location of that part.
        switch (target is DeclarationExpressionSyntax { Designation: { } designated } ? designated : target)
        {
            case ParenthesizedVariableDesignationSyntax several
                when value is TupleExpressionSyntax each && several.Variables.Count == each.Arguments.Count:
                for (int i = 0; i < several.Variables.Count; i++)
                {
                    this.Assign(
                        target: several.Variables[i],
                        value: each.Arguments[i].Expression,
                        replaces: replaces,
                        turn: turn);
                }

                return;
            case SingleVariableDesignationSyntax single:
                if (this.region.ModelFor(single).GetDeclaredSymbol(single) is { } declared)
                {
                    this.evaluator.Declare(local: declared, state: this.evaluator.Eval(value), from: value);
                    this.evaluator.HoldsValueOf(local: declared, value: value, replaces: true);
                }

                return;
        }

        // Parentheses around a target do not change the target.
        while (target is ParenthesizedExpressionSyntax around)
        {
            target = around.Expression;
        }

        // An assignment to a name with ref, with no ref for the value, is an assignment to the
        // location of the name.
        if (value is not RefExpressionSyntax && target is IdentifierNameSyntax aliased)
        {
            if (this.evaluator.TheLocationOf(aliased) is { } location)
            {
                target = location;
            }
            else if (this.evaluator.TheCollectionOfARef(aliased) is { } collection)
            {
                // The walk cannot read the location again. The value is then in the collection.
                this.evaluator.GetsThroughARef(
                    collection: collection,
                    state: this.evaluator.Eval(value),
                    value: value);
            }
        }

        // An assignment to the result of a method that returns a ref is an assignment to the
        // location that the method returns.
        if (target is InvocationExpressionSyntax call)
        {
            foreach ((ExpressionSyntax location, bool isAnElement) in this.evaluator.LocationsOfARefReturn(call))
            {
                if (isAnElement)
                {
                    this.evaluator.ForgetsThePositionsOf(location);

                    this.evaluator.GetsThroughARef(
                        collection: location,
                        state: this.evaluator.Eval(value),
                        value: value);
                }
                else
                {
                    this.AssignState(
                        target: location,
                        state: this.evaluator.Eval(value),
                        replaces: false,
                        turn: null,
                        from: value);
                }
            }
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
        // An assignment that is sure to run gives the collection the value at that position.
        (MadeObject Made, ImmutableArray<LoopState> All)? positions = null;

        if (target is ElementAccessExpressionSyntax indexed)
        {
            positions =
                this.evaluator.PositionsBeforeAnAssignment(
                    collection: indexed.Expression,
                    replaces: replaces,
                    turn: turn);

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

            if (target is ElementAccessExpressionSyntax assigned)
            {
                this.evaluator.HasThePositionAfter(before: positions, element: assigned, value: value);
            }

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
            // keeps each index also, as the indexer of a dictionary keeps its key. The walk has
            // no position for the value here: an assignment with a value expression gives one
            // after this.
            case ElementAccessExpressionSyntax element:
                this.evaluator.ForgetsThePositionsOf(element.Expression);
                this.evaluator.Absorb(target: element.Expression, state: state);
                this.evaluator.PutsIntoALocation(field: null, element: element, state: state);

                if (this.region.SymbolOf(element) is IPropertySymbol { DeclaringSyntaxReferences.IsEmpty: true })
                {
                    foreach (ArgumentSyntax index in element.ArgumentList.Arguments)
                    {
                        this.evaluator.Absorb(
                            target: element.Expression,
                            state: this.evaluator.Eval(index.Expression));
                    }
                }

                // The collection holds the object of the value. An assignment with a value
                // expression recorded that from the expression.
                if (from == null)
                {
                    this.evaluator.GetsAState(collection: element.Expression, put: state);
                }

                return;
        }

        ISymbol? assigned = this.region.SymbolOf(target);

        // The field keyword of C# 14 is a name of its property, as EvalFieldKeyword reads it.
        bool isTheFieldKeyword = target.RawKind == SodaFlowSymbols.FieldKeywordKind;

        if (isTheFieldKeyword
            && SodaFlowSymbols.PropertyOfTheFieldKeyword(expression: target, symbol: assigned) is { } property)
        {
            assigned = property;
        }

        switch (assigned)
        {
            // A deconstruction into a local with ref, or a delegate that it gets with +=, gives the
            // location a value. Assign sends the value of each other assignment there before this.
            case ILocalSymbol { RefKind: not RefKind.None }
                when from is not RefExpressionSyntax
                     && target is ExpressionSyntax name
                     && this.evaluator.TheLocationOf(name) is { } location:
                // ReSharper disable once TailRecursiveCall - It runs one time more at most: a location is no local with ref.
                this.AssignState(target: location, state: state, replaces: replaces, turn: turn);

                break;
            case ILocalSymbol or IParameterSymbol when target is IdentifierNameSyntax:
                if (replaces || IsNewInEachTurn(local: assigned, turn: turn))
                {
                    this.evaluator.Assign(local: assigned, state: state, from: from, kept: from);
                }
                else if (turn != null)
                {
                    this.AssignInATurn(local: assigned, state: state, turn: turn);
                }
                else
                {
                    this.evaluator.Declare(local: assigned, state: state);
                }

                // A parameter of a primary constructor that a member of the object uses is a field
                // of the object. Thus, each call of a member after this reads the new value.
                if (IsOfAPrimaryConstructor(assigned))
                {
                    this.evaluator.SetMember(member: assigned, state: state, replaces: replaces, from: from);
                }

                break;
            // A member of the object that the code is in: a constructor that sets its members, or
            // a method that does.
            case { IsStatic: false } and (IFieldSymbol or IPropertySymbol or IEventSymbol)
                when (target is IdentifierNameSyntax
                          or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
                      || isTheFieldKeyword)
                     && HasAValue(assigned):
                this.evaluator.SetMember(member: assigned, state: state, replaces: replaces, from: from);
                break;
            case IFieldSymbol or IPropertySymbol or IEventSymbol
                when !this.region.Symbols.IsSodaFlow(assigned) && HasAValue(assigned):
                this.evaluator.SetMemberOf(
                    owner: (target as MemberAccessExpressionSyntax)?.Expression,
                    member: assigned,
                    state: state,
                    replaces: replaces,
                    turn: turn,
                    from: from);

                break;
        }
    }

    // True for a parameter of the primary constructor of a type. The constructor declares on the
    // type.
    private static bool IsOfAPrimaryConstructor(ISymbol name) =>
        name is IParameterSymbol
        {
            ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
        }
        && constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax;

    // An event keeps its handlers as a field keeps its value, when its declaration has no
    // accessors. The add accessor of each other event keeps them, and CheckAccessor reads it.
    private static bool HasAValue(ISymbol member) =>
        member is not IEventSymbol || SodaFlowSymbols.IsFieldLikeEvent(member);

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
            this.Report(this.evaluator.FollowEnumeration(written: spread.Expression, byThePattern: true));
        }

        this.Report(this.evaluator.FollowCollectionExpression(node));

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

    // A with expression calls the copy constructor of the record before the setters of its
    // initializer.
    public override void VisitWithExpression(WithExpressionSyntax node)
    {
        this.Visit(node.Expression);
        this.Report(this.evaluator.FollowCopy(node));
        this.Visit(node.Initializer);
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
            this.CheckNestedInitialized(initializer: initializer, expression: expression, setsMembers: setsMembers);

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

            if (expression is AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax name,
                    Right: not InitializerExpressionSyntax
                } assignment
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

            this.CheckInitializedMember(expression: expression, owner: this.evaluator.Eval(made));

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

    /// <summary>
    ///     An initializer in an initializer. Examples are <c>A = { B = x }</c>
    ///     and <c>A = { x }</c>. It runs the setters or calls <c>Add</c> on the
    ///     value that the member <c>A</c> has, and gives <c>A</c> no new value.
    ///     The walk reads a setter or an <c>Add</c> of this compilation that has a
    ///     body.
    /// </summary>
    private void CheckNestedInitialized(SyntaxNode initializer, ExpressionSyntax expression, bool setsMembers)
    {
        if (this.StateOfTheInitialized(initializer) is not { } owner)
        {
            return;
        }

        if (setsMembers)
        {
            if (expression is AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax name,
                    Right: not InitializerExpressionSyntax
                } assignment
                && this.region.SymbolOf(name) is IPropertySymbol
                {
                    SetMethod: { DeclaringSyntaxReferences.IsEmpty: false } setter
                })
            {
                this.Report(
                    this.evaluator.FollowImplied(
                        call: assignment,
                        member: setter,
                        owner: owner,
                        arguments: [(assignment.Right, this.evaluator.Eval(assignment.Right))]));
            }

            this.CheckInitializedMember(expression: expression, owner: owner);

            return;
        }

        if (!initializer.IsKind(SyntaxKind.CollectionInitializerExpression)
            || !this.region.Symbols.DeclaresAdd
            || this.region.ModelFor(expression).GetCollectionInitializerSymbolInfo(expression).Symbol
                is not IMethodSymbol { IsStatic: false, DeclaringSyntaxReferences.IsEmpty: false } add)
        {
            return;
        }

        IEnumerable<ExpressionSyntax> values =
            expression is InitializerExpressionSyntax several ? several.Expressions : new[] { expression };

        this.Report(
            this.evaluator.FollowImplied(
                call: expression,
                member: add.OriginalDefinition,
                owner: owner,
                arguments: [.. values.Select(value => (value, this.evaluator.Eval(value)))]));
    }

    // A member whose value is an initializer, as in A = { B = x }, gets no new value. The compiler
    // calls only the getter of A, and the walk reads a getter that has a body. It does not read
    // the setter of A.
    private void CheckInitializedMember(ExpressionSyntax expression, LoopState owner)
    {
        if (expression is AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax name,
                Right: InitializerExpressionSyntax
            }
            && this.region.SymbolOf(name) is IPropertySymbol
            {
                GetMethod: { DeclaringSyntaxReferences.IsEmpty: false } getter
            })
        {
            this.Report(this.evaluator.FollowImplied(call: name, member: getter, owner: owner, arguments: []));
        }
    }

    // The state of the object that an initializer is for. A nested initializer is for the value
    // of a member of the object of the initializer around it. Null for each other initializer.
    private LoopState? StateOfTheInitialized(SyntaxNode initializer)
    {
        if (initializer.Parent
            is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or WithExpressionSyntax)
        {
            return this.evaluator.Eval((ExpressionSyntax)initializer.Parent);
        }

        if (initializer.Parent is not AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax name,
                Parent: InitializerExpressionSyntax around
            }
            || !around.IsKind(SyntaxKind.ObjectInitializerExpression)
            || this.StateOfTheInitialized(around) is not { } owner)
        {
            return null;
        }

        return this.region.SymbolOf(name) is { } member && owner.Members is { } members
            ? members.StateThrough(this.evaluator.InitializedThrough(member))
            : LoopState.None;
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (this.region.SymbolOf(node) is not IMethodSymbol method)
        {
            // The operand of nameof only names a member, and no code of it runs. A method with
            // the name nameof has a symbol, thus it does not come here.
            if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" })
            {
                return;
            }

            base.VisitInvocationExpression(node);
            this.CheckDynamicCall(node);

            return;
        }

        if (this.region.Symbols.IsRemovedFrom(tree: node.SyntaxTree, method: method))
        {
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

                if (!OperatorTable.EnumeratesAfterTheClose(method))
                {
                    this.RunGiven(method: method, receiver: receiver, arguments: arguments);
                }
                else
                {
                    // The walk reads a query one time. A query that the code made before the call
                    // can have a second enumeration in the block. Thus, the walk reads it here.
                    foreach (ArgumentSyntax argument in arguments.Where(static argument =>
                                 !IsMadeAtTheCall(argument.Expression)))
                    {
                        this.RunQueries(argument.Expression);
                    }
                }

                // A send in the block puts its value into the sink in this transaction. A switch of
                // the sink selects that value before the close.
                if (receiver != null && method.Name == "Send" && arguments.Count == 1)
                {
                    LoopState sent = this.evaluator.Eval(arguments[0].Expression).AsContent;

                    this.evaluator.Merge(target: receiver, state: sent);
                    this.evaluator.Sends(sink: receiver, content: sent);
                }

                // The function that Transaction.Post gets runs after the transaction. A send in it
                // gives content to the sink after the close.
                if (receiver == null
                    && method is { Name: "Post", ContainingType.Name: "Transaction" }
                    && arguments.FirstOrDefault(argument =>
                            LoopStateEvaluator.ParameterFor(
                                method: method,
                                argument: argument,
                                position: arguments.IndexOf(argument)) is { Ordinal: 0 })
                        is { } action)
                {
                    this.evaluator.RunsAfterTheTransaction(action.Expression);
                }

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
                // The table of the platform says what each of its methods keeps. A package has no
                // such table. Its code can put an object into each collection that it can get to,
                // and can keep a function for a time after the call.
                bool isOfAPackage = LoopStateEvaluator.IsOfAPackage(method);

                if (isOfAPackage)
                {
                    this.evaluator.RanCodeWithNoBody();
                }

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
                    // A call that is sure to run can give the collection new positions.
                    (bool always, SyntaxNode? itsTurn) = this.WhereACallRuns(node);

                    (MadeObject Made, ImmutableArray<LoopState> All)? positions =
                        this.evaluator.PositionsBeforeACall(
                            collection: receiver,
                            method: method,
                            replaces: always,
                            turn: itsTurn);

                    this.evaluator.CanGet(collection: receiver, method: method, arguments: arguments);

                    // The owner of a method of a package holds each object that it gets. The
                    // store has the state of that object at each read. A copy of that state keeps
                    // a cell that the object had at the call.
                    if (isOfAPackage)
                    {
                        this.evaluator.KeepsWhatAPackageGets(owner: receiver, invocation: node, method: method);
                    }
                    else
                    {
                        this.evaluator.Absorb(
                            target: receiver,
                            state: this.evaluator.Moved(invocation: node, method: method, withReceiver: false));
                    }

                    // A search reads its argument, and the collection does not get it. An object
                    // of this compilation that is not a sequence is not a collection. It can call
                    // a method of object, as Equals is, and that method keeps nothing. The owner
                    // of a method of a package can keep each argument.
                    if (isOfAPackage
                        || (!SodaFlowSymbols.IsASearch(method)
                            && this.region.TypeOf(receiver) is { } owner
                            && (SodaFlowSymbols.IsSequence(owner) || SodaFlowSymbols.IsPlatform(owner))))
                    {
                        foreach (ArgumentSyntax argument in arguments)
                        {
                            this.evaluator.Gets(collection: receiver, value: argument.Expression);
                        }
                    }

                    this.evaluator.MovesPositions(
                        before: positions,
                        collection: receiver,
                        method: method,
                        arguments: arguments);

                    if (!always && itsTurn != null)
                    {
                        this.evaluator.AddsInEachTurn(
                            call: node,
                            collection: receiver,
                            method: method,
                            arguments: arguments,
                            turn: itsTurn);
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
                    // After a call that is not sure to run, the location can have that one also.
                    bool? exchanges =
                        exchange.Comparand is { } comparand
                            ? this.evaluator.ExchangesAt(location: exchange.Location, comparand: comparand)
                            : true;

                    // A CompareExchange whose comparand is not the object in the element changes nothing.
                    if (exchanges != false)
                    {
                        bool puts = exchanges == true;
                        (bool always, SyntaxNode? itsTurn) = puts ? this.WhereACallRuns(node) : (false, null);

                        LoopState state = puts ? put : this.evaluator.Eval(exchange.Location).Join(put);

                        SyntaxNode target =
                            this.TargetThroughARef(written: exchange.Location, state: state, gets: true);

                        // An element with a constant index has the value at its position after a put
                        // that is sure to run.
                        (MadeObject Made, ImmutableArray<LoopState> All)? positions =
                            target is ElementAccessExpressionSyntax indexed
                                ? this.evaluator.PositionsBefore(
                                    collection: indexed.Expression,
                                    replaces: always,
                                    turn: itsTurn)
                                : null;

                        this.AssignState(target: target, state: state, replaces: always, turn: itsTurn);

                        if (target is ElementAccessExpressionSyntax assigned)
                        {
                            this.evaluator.HasThePositionAfter(
                                before: positions,
                                element: assigned,
                                value: exchange.Value);
                        }
                    }
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

                    this.evaluator.KeepsWhatItGets(owner: receiver, arguments: arguments);
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

        if (receiver != null)
        {
            string operatorName = LoopStateEvaluator.OperatorName(method);

            // CanExecute is an ICommand member, which a bindable action implements. Thus, the read
            // members count on a callee with no body to read, and not only on a library method. The
            // receiver can be of any type, as ICommand is, and its state tells if it holds a
            // pending value. A fold of a collection reads the collection at construction. Execute
            // and Dispose of a bindable action read the enablement cell, as CanExecute does. The
            // Execute of a library method reads nothing in the call.
            if ((SodaFlowSymbols.ReadMembers.Contains(operatorName) && kind != CalleeKind.Body)
                || (kind == CalleeKind.Library && OperatorTable.ReadsReceiverAtConstruction(operatorName))
                || (operatorName == "Execute" && kind is CalleeKind.Mover or CalleeKind.Unknown)
                || (operatorName == "Dispose"
                    && kind != CalleeKind.Body
                    && symbols.IsBindableAction(this.region.TypeOf(receiver))))
            {
                this.CheckRead(node: node, owner: receiver, name: method.Name);
            }
        }

        return;

        // A lambda written at the call is code that the walk reads here when the callee runs it
        // in the call.
        bool Runs(ArgumentSyntax argument, int position) =>
            LoopStateEvaluator.RunsFunction(
                kind: kind,
                method: method,
                parameter: LoopStateEvaluator.ParameterFor(method: method, argument: argument, position: position))
            && !this.evaluator.WaitsBeforeItsFunctions(call: node, method: method);
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
    ///     True for a sequence that the code makes at the call. No other code
    ///     can enumerate it.
    /// </summary>
    public static bool IsMadeAtTheCall(ExpressionSyntax written) =>
        written is InvocationExpressionSyntax or QueryExpressionSyntax;

    /// <summary>
    ///     Reads what the queries of a sequence run, for each query that the
    ///     code of this walker makes.
    /// </summary>
    public void RunQueriesMadeHere(ExpressionSyntax written)
    {
        foreach (QueryValue query in this.evaluator.Eval(written).Queries
                     .Where(query => ReferenceEquals(objA: query.Definer, objB: this.evaluator)))
        {
            this.Run(query: query, to: this.sink);
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
    // part runs when something enumerates the query. A query whose each clause runs its function in
    // the call runs each part now: see RunsInTheCall.
    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        this.Visit(node.FromClause.Expression);

        if (this.RunsInTheCall(node))
        {
            this.Visit(node.Body);

            return;
        }

        this.evaluator.Wait(node);
    }

    // True for a query expression whose each clause calls a method that cannot know the library and
    // that returns no sequence. Such a method runs its function in the call, as a method of
    // SodaFlow.Functional does for Maybe. A clause that the compiler does not bind waits.
    private bool RunsInTheCall(QueryExpressionSyntax query)
    {
        SemanticModel model = this.region.ModelFor(query);
        List<ISymbol?> methods = [model.GetQueryClauseInfo(query.FromClause).CastInfo.Symbol];

        for (QueryBodySyntax? body = query.Body; body != null; body = body.Continuation?.Body)
        {
            foreach (QueryClauseSyntax clause in body.Clauses)
            {
                if (clause is OrderByClauseSyntax order)
                {
                    methods.AddRange(order.Orderings.Select(ordering => model.GetSymbolInfo(ordering).Symbol));

                    continue;
                }

                QueryClauseInfo info = model.GetQueryClauseInfo(clause);

                if (info.OperationInfo.Symbol == null)
                {
                    return false;
                }

                methods.Add(info.CastInfo.Symbol);
                methods.Add(info.OperationInfo.Symbol);
            }

            // A select clause that gives the range variable as it is calls no method after a
            // second clause.
            methods.Add(model.GetSymbolInfo(body.SelectOrGroup).Symbol);
        }

        return methods.All(method =>
            method is not IMethodSymbol called
            || (!this.region.Symbols.CanKnowTheLibrary(called.ContainingAssembly)
                && !SodaFlowSymbols.IsEnumerated(called.ReturnType)
                && !SodaFlowSymbols.IsAsyncEnumerated(called.ReturnType)));
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
            this.evaluator.GivesAnArgument(argument: receiver, parameter: definition.Parameters[0], followed: followed);
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
                // The array of a params parameter is a new array, and what the callee put into it
                // does not come to an argument. A change through an element is a change to the
                // object of the argument, which the store has.
                if (followed != null
                    && followed.Outs.TryGetValue(key: parameter, value: out LoopState? put)
                    && !(this.evaluator.IsOneOfAParamsArray(parameter: parameter, argument: arguments[i].Expression)
                         && !this.evaluator.Eval(arguments[i].Expression).Objects.IsEmpty))
                {
                    this.evaluator.MergesWhatACalleePut(target: arguments[i].Expression, state: put);

                    this.evaluator.GivesAnArgument(
                        argument: arguments[i].Expression,
                        parameter: parameter,
                        followed: followed);
                }

                continue;
            }

            LoopState state =
                followed == null
                    ? moved
                    : followed.Outs.TryGetValue(key: parameter, value: out LoopState? assigned)
                        ? assigned
                        : LoopState.None;

            // A callee with a body that gave its parameter no value gives an element nothing.
            bool gives = followed == null || followed.Outs.ContainsKey(parameter);

            SyntaxNode target =
                this.TargetThroughARef(written: arguments[i].Expression, state: state, gets: gives);

            // A callee that always gives the parameter its value replaces the element. A second
            // argument with ref or out can be the same element, and the walk does not know which
            // value goes there last.
            (MadeObject Made, ImmutableArray<LoopState> All)? positions =
                target is ElementAccessExpressionSyntax indexed
                && followed != null
                && followed.AlwaysGives.Contains(parameter)
                && arguments[i].Parent?.Parent is { } call
                && arguments.Count(static argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)) == 1
                    ? this.PositionsBeforeAGive(collection: indexed.Expression, call: call)
                    : null;

            if (target is not ElementAccessExpressionSyntax || gives)
            {
                this.AssignState(target: target, state: state, replaces: false, turn: null);
            }

            if (target is ElementAccessExpressionSyntax given)
            {
                this.evaluator.HasTheStateAtThePosition(before: positions, element: given, state: state);
            }
        }
    }

    // The positions of a collection before a call gives one of its elements a value. Null when the
    // call is not sure to run.
    private (MadeObject Made, ImmutableArray<LoopState> All)? PositionsBeforeAGive(
        ExpressionSyntax collection,
        SyntaxNode call)
    {
        (bool always, SyntaxNode? itsTurn) = this.WhereACallRuns(call);

        return this.evaluator.PositionsBefore(collection: collection, replaces: always, turn: itsTurn);
    }

    // The target of a value that a call gives through ref or out. A name with ref is its
    // location, when the walk can read that location again. For each other name with ref for an
    // element, the collection of that element gets the value.
    private SyntaxNode TargetThroughARef(ExpressionSyntax written, LoopState state, bool gets)
    {
        if (this.evaluator.TheLocationOf(written) is { } location)
        {
            return location;
        }

        if (gets && this.evaluator.TheCollectionOfARef(written) is { } collection)
        {
            this.evaluator.GetsThroughARef(collection: collection, state: state, value: null);
        }

        return written;
    }

    // The operator ++ or -- gives its operand a new value, and the walk has no state for that
    // value at the operand. A ref to an element is a second name of that element, and an
    // assignment to the name is an assignment to the element.
    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        base.VisitPrefixUnaryExpression(node);

        if (node.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression)
        {
            this.ForgetsThePositionsAround(node.Operand);
        }
    }

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        base.VisitPostfixUnaryExpression(node);

        if (node.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
        {
            this.ForgetsThePositionsAround(node.Operand);
        }
    }

    public override void VisitRefExpression(RefExpressionSyntax node)
    {
        base.VisitRefExpression(node);

        if (!this.evaluator.KeepsThePositionsAt(node))
        {
            this.ForgetsThePositionsAround(node.Expression);
        }
    }

    private void ForgetsThePositionsAround(ExpressionSyntax element)
    {
        // A local with ref is a second name of its location.
        if (this.evaluator.TheLocationOf(element) is { } location)
        {
            element = location;
        }

        while (element is ParenthesizedExpressionSyntax around)
        {
            element = around.Expression;
        }

        if (element is ElementAccessExpressionSyntax written)
        {
            this.evaluator.ForgetsThePositionsOf(written.Expression);
        }
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // Only the owner is code. The name is the member, which the check below reads.
        this.Visit(node.Expression);
        this.CheckPropertyRead(node: node, owner: node.Expression);
        this.CheckDynamicRead(node);

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
            && !this.IsTheNameOfACalledMethod(node)
            && this.region.SymbolOf(node)
                is IPropertySymbol { IsStatic: false, DeclaringSyntaxReferences.Length: > 0 } property)
        {
            this.ReadGetter(node: node, property: property);
        }
    }

    // The name of a called method is not a property. The compiler keeps the symbol of a call, but
    // not the symbol of the name of its method. For that name, it binds the name and its owners
    // again each time. A call of a property that has a delegate gets the method Invoke.
    private bool IsTheNameOfACalledMethod(SyntaxNode node) =>
        node.Parent is InvocationExpressionSyntax call
        && call.Expression == node
        && this.region.SymbolOf(call) is IMethodSymbol { MethodKind: not MethodKind.DelegateInvoke };

    /// <summary>
    ///     A property of the library that reads the current value, or a property
    ///     with a getter of this compilation, on the given owner.
    /// </summary>
    private void CheckPropertyRead(SyntaxNode node, ExpressionSyntax owner)
    {
        if (this.IsTheNameOfACalledMethod(node) || this.region.SymbolOf(node) is not IPropertySymbol property)
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
        else if (this.region.Symbols.ReadsItsOwner(property))
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
                subject: name,
                readBack: state.ReadBack));
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

    /// <summary>
    ///     A call that the program binds at run time, on a value of the type
    ///     <c>dynamic</c>. The walk can know the type of the object. It then reads
    ///     the method of that type with the name and the number of arguments of
    ///     the call. Otherwise, a pending argument goes to code that the analyzer
    ///     cannot read.
    /// </summary>
    private void CheckDynamicCall(InvocationExpressionSyntax node)
    {
        if (this.region.TypeOf(node) is not { TypeKind: TypeKind.Dynamic })
        {
            return;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = node.ArgumentList.Arguments;

        if (node.Expression is MemberAccessExpressionSyntax access
            && this.evaluator.Eval(access.Expression) is { Members: { } members } owner
            && members.MethodsOf(name: access.Name.Identifier.ValueText, parameters: arguments.Count)
                .ToList() is { Count: 1 } found)
        {
            this.Report(
                this.evaluator.FollowImplied(
                    call: node,
                    member: found[0],
                    owner: owner,
                    arguments: arguments.Select(argument =>
                        (argument.Expression, this.evaluator.Eval(argument.Expression)))));

            return;
        }

        foreach (ArgumentSyntax argument in arguments)
        {
            this.CheckEscape(subject: node.Expression.ToString(), argument: argument.Expression);
        }
    }

    /// <summary>
    ///     A read of a property that the binder finds at run time, on a value of
    ///     the type <c>dynamic</c>. As for a dynamic call, the walk reads the
    ///     getter of the property with that name, of an object that it saw made.
    ///     The walk does not come here for the target of a plain assignment.
    /// </summary>
    /// <remarks>
    ///     Only a dynamic access has no symbol and an object with a property of
    ///     that name. A static type with that property gives the symbol.
    /// </remarks>
    private void CheckDynamicRead(MemberAccessExpressionSyntax node)
    {
        if (this.region.SymbolOf(node) != null
            || this.evaluator.Eval(node.Expression) is not { Members: { } members } owner)
        {
            return;
        }

        foreach (IPropertySymbol property in members.PropertiesNamed(node.Name.Identifier.ValueText))
        {
            if (property.GetMethod is { } read)
            {
                this.Report(this.evaluator.FollowImplied(call: node, member: read, owner: owner, arguments: []));
            }
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
    private void CheckEscape(ISymbol callee, ExpressionSyntax argument) =>
        this.CheckEscape(
            subject: callee.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            argument: argument);

    private void CheckEscape(string subject, ExpressionSyntax argument)
    {
        LoopState state = this.evaluator.Eval(argument);

        // Only a library value escapes. An object of the caller that holds a pending member goes
        // where the caller sends it, as it does with the looped types. The walk checks its reads
        // through its members.
        if (!state.Pending || !this.IsCarrier(argument))
        {
            return;
        }

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

    /// <summary>
    ///     True for a value of the library with no assertion, or for an array or
    ///     a collection expression that has one. A cast to <c>object</c> or an
    ///     array of objects hides the type of the value, but not the type of the
    ///     expression in it.
    /// </summary>
    private bool IsCarrier(ExpressionSyntax argument)
    {
        if (LoopStateEvaluator.IsAsserted(argument))
        {
            return false;
        }

        ExpressionSyntax value = LoopStateEvaluator.Unwrapped(argument);
        ITypeSymbol? type = this.region.TypeOf(value);

        if (this.region.Symbols.IsCarrierType(type))
        {
            return true;
        }

        // The elements of an array or a collection that the code writes here have their own types.
        return value switch
        {
            CollectionExpressionSyntax collection => collection.Elements
                .OfType<ExpressionElementSyntax>()
                .Any(element => this.IsCarrier(element.Expression)),
            ImplicitArrayCreationExpressionSyntax made => made.Initializer.Expressions.Any(this.IsCarrier),
            ArrayCreationExpressionSyntax { Initializer: { } initializer } =>
                initializer.Expressions.Any(this.IsCarrier),
            _ => type is IArrayTypeSymbol array && this.region.Symbols.IsCarrierType(array.ElementType)
        };
    }
}
