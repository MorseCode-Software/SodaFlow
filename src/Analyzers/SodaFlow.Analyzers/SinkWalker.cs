using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
internal sealed partial class SinkWalker(in LoopRegion region, in LoopStateEvaluator evaluator, in Action<Finding> sink)
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

    // The most objects that a serializer goes into, in each other. See Serialize.
    private const int MaxSerializedDepth = 64;

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
    private Action<Finding> sink = sink;

    // The query whose code the walk reads now, when something enumerates that query.
    private QueryValue? running;

    // The position after each await that can stop the call, with the block or the switch section
    // around it. The statements of that part after the position run only when the task completes.
    private readonly List<(SyntaxNode Block, int From)> later = [];

    // For an iterator that the code stops after its first item: the position after each yield
    // return, with the part around it. The code of that part after the position does not run.
    private readonly List<(SyntaxNode Block, int From)> afterTheItem = [];

    private bool takesOneItem;

    // The state that each catch clause of this function gets: the object of each throw that can go
    // to the clause. That throw is in its try block, or in a callee that the block calls.
    private readonly Dictionary<CatchClauseSyntax, LoopState> caught = [];

    private readonly List<(ITypeSymbol? Type, LoopState State)> thrown = [];

    /// <summary>
    ///     Each object that a throw sends out of the function to its caller,
    ///     with the type that the throw names.
    /// </summary>
    public IReadOnlyList<(ITypeSymbol? Type, LoopState State)> Thrown => this.thrown;

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
    internal static bool NeverConverts(SyntaxNode expression) =>
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

        bool awaits =
            disposes && declaration.Parent is LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: not 0 };

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
                this.evaluator.NotesNamesOfOneObject(local: local, declarator: declarator);
                this.evaluator.GetsElementsOf(collection: declarator, literal: declarator.Initializer.Value);
            }

            // A using declaration calls Dispose when the code leaves the block that it is in.
            if (disposes && !awaits && !this.EndsLater(declaration))
            {
                this.DisposesAtTheEnd(declaration: declaration, resource: declarator.Initializer.Value);
            }
        }

        if (awaits && declaration.Parent is { Parent: { } scope } statement && !this.EndsLater(declaration))
        {
            this.DisposeEachAsync(
                resources: declaration.Variables
                    .Select(static declarator => declarator.Initializer?.Value)
                    .OfType<ExpressionSyntax>(),
                scope: scope,
                at: statement.SpanStart);
        }
    }

    // An await using calls DisposeAsync on each resource, from the last to the first, and awaits
    // each task. A task that is not complete stops the call. The code after the scope then runs
    // later, and so do the calls for the resources before it. The position is the start of the
    // await using.
    private void DisposeEachAsync(IEnumerable<ExpressionSyntax> resources, SyntaxNode scope, int at)
    {
        foreach (ExpressionSyntax resource in resources.Reverse())
        {
            (Followed? followed, bool complete) = this.evaluator.FollowDisposeAsync(resource);
            this.Report(followed);

            if (complete)
            {
                continue;
            }

            if (this.PartAround(statement: scope, at: at) is { } block)
            {
                this.later.Add((block, scope.Span.End));
            }

            return;
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
    // read. The Dispose of a bindable action of the library reads its enablement cell. The Dispose
    // of a map of a collection runs its eviction handler. A call of that Dispose in the code does
    // the same.
    private void CheckDisposed(ExpressionSyntax resource)
    {
        if (this.evaluator.FollowDispose(resource) is { } followed)
        {
            this.Report(followed);

            return;
        }

        // A using disposes its resource where the code reaches the statement.
        this.Report(
            this.evaluator.FollowEvictionsOfADispose(
                map: resource,
                dispose: resource,
                runsOnEachPath: () =>
                    resource.FirstAncestorOrSelf<StatementSyntax>() is { } statement
                    && this.SurelyRunsAt(place: statement, forARun: true, after: out _)));

        LoopState state = this.evaluator.Eval(resource);

        if (state.Pending && this.IsABindableAction(value: resource, state: state))
        {
            this.CheckRead(node: resource, state: state, name: "Dispose");
        }
    }

    // True for a value that is a bindable action. A local of IDisposable or of IBindable can have
    // one. Thus, the parts that the action carries tell it also, and not only the type.
    private bool IsABindableAction(ExpressionSyntax value, LoopState state) =>
        this.region.Symbols.IsBindableAction(this.region.TypeOf(value))
        || state.Members?.Members.Any(member => this.region.Symbols.IsBindableAction(member.Key.ContainingType))
        == true;

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

        if (node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
        {
            this.DisposeEachAsync(resources: resources, scope: node, at: node.SpanStart);

            return;
        }

        // The statement always disposes its resource when the code leaves it. Only a clock of the
        // library base keeps a timer.
        bool disposesEachTime =
            this.region.Objects.HasTimers && this.AlwaysRunsAt(place: node, turn: out SyntaxNode? turn) && turn == null;

        foreach (ExpressionSyntax resource in resources)
        {
            if (disposesEachTime)
            {
                this.CancelsATimer(resource);
            }

            this.CheckDisposed(resource);
        }
    }

    // An interpolated string handler of the code runs its constructor and an AppendLiteral for each text.
    public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        this.Report(this.evaluator.FollowHandler(node));
        base.VisitInterpolatedStringExpression(node);
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
        this.Report(this.evaluator.FollowAwaiter(node));
        this.ThrowsTheFaultsOf(task: node.Expression, at: node);

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
                && this.LaterPartOf(awaited) is { } block
                    ? (block, awaited.Span.End)
                    : null,
            CommonForEachStatementSyntax loop when loop.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) =>
                this.LaterAfterEachItem(loop: loop, enumerator: null),
            _ => null
        };

    private void AwaitsEachItem(CommonForEachStatementSyntax node, LoopState? enumerator)
    {
        if (node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)
            && this.LaterAfterEachItem(loop: node, enumerator: enumerator) is { } part)
        {
            this.later.Add(part);
        }
    }

    // An await foreach awaits each MoveNextAsync. The body runs in the call only for an item that
    // the sequence gives in the call. The code after the loop runs in the call only when each item
    // comes in the call, and the task of DisposeAsync is complete.
    private (SyntaxNode Block, int From)? LaterAfterEachItem(CommonForEachStatementSyntax loop, LoopState? enumerator)
    {
        (bool first, bool each) =
            enumerator != null
                ? this.EnumeratorGivesAtOnce(loop: loop, enumerator: enumerator)
                : CompleteTasks.ItemsComeAtOnce(region: this.region, sequence: loop.Expression);

        return !each && this.PartAround(loop) is { } block
            ? (block, first ? loop.Statement.Span.End : loop.Statement.SpanStart)
            : null;
    }

    // An enumerator object of this compilation gives each item in the call when each MoveNextAsync
    // that it can run gives only complete tasks.
    private (bool First, bool Each) EnumeratorGivesAtOnce(CommonForEachStatementSyntax loop, LoopState enumerator)
    {
        (_, IMethodSymbol? next, _, IMethodSymbol? dispose) =
            this.CallsOf(node: loop, sequence: this.SequenceOf(loop));

        bool each = next != null && this.evaluator.GivesOnlyCompleteTasks(owner: enumerator, member: next);

        return (
            each,
            each && (dispose == null || this.evaluator.GivesOnlyCompleteTasks(owner: enumerator, member: dispose)));
    }

    // The largest block or switch section that always runs the statement of the await to its end.
    // Null when a statement, a branch, or a function between the two can skip the await. A goto to
    // a label after the await can skip it also. A constant condition skips no part that the walk
    // reads. In a function whose body is an expression, that expression is the part.
    private SyntaxNode? LaterPartOf(SyntaxNode node) =>
        (WhereItAlwaysRuns(expression: node, constants: this.region)
         ?? HeaderOf(expression: node, constants: this.region)) switch
        {
            StatementSyntax statement => this.PartAround(statement),
            ArrowExpressionClauseSyntax arrow => arrow.Expression,
            ExpressionSyntax body => body,
            _ => null
        };

    // The position is where the await starts. A break or a continue before it can skip the await.
    private SyntaxNode? PartAround(SyntaxNode statement, int? at = null)
    {
        SyntaxNode? block = null;
        SyntaxNode part = statement;
        int start = at ?? statement.SpanStart;

        for (SyntaxNode? place = statement.Parent;
             place != null && this.RunsToItsEnd(statement: place, part: part, at: start);
             part = place, place = place.Parent)
        {
            block = place is BlockSyntax or SwitchSectionSyntax ? place : block;
        }

        return block?.DescendantNodes().OfType<LabeledStatementSyntax>().Any() == false ? block : null;
    }

    // The statement that always runs an expression of its header. That expression is the collection
    // of a foreach, the value of a switch, the resource of a using, or the object of a lock. It is
    // also the first condition of a while or a for, the condition of a do, and each initializer of a
    // for. Null for each other expression.
    private static StatementSyntax? HeaderOf(SyntaxNode expression, LoopRegion? constants)
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
                case ForStatementSyntax loop:
                    return loop.Declaration == child
                           || loop.Condition == child
                           || (child is ExpressionSyntax initializer && loop.Initializers.Contains(initializer))
                        ? loop
                        : null;
                case DoStatementSyntax loop:
                    return loop.Condition == child ? loop : null;
                case UsingStatementSyntax used:
                    return used.Expression == child || used.Declaration == child ? used : null;
                case LockStatementSyntax locked:
                    return locked.Expression == child ? locked : null;
                case StatementSyntax
                    or AnonymousFunctionExpressionSyntax
                    or SwitchExpressionArmSyntax
                    or QueryClauseSyntax
                    or SelectOrGroupClauseSyntax:
                    return null;
            }

            if (CanSkip(parent: parent, child: child, constants: constants))
            {
                return null;
            }
        }

        return null;
    }

    // True when each run of a statement runs a part of it to its end, up to a position in that part.
    // A loop does that when it always runs its first turn with no break or continue of the loop
    // before the position. A try runs its block. With a catch clause, the code after the try
    // can run when the block stops before the await. Thus, only a try with no catch clause counts. A
    // try always runs its finally block, also with a catch clause. A switch section runs its
    // statements in their order, as a block does. An if on a constant runs the branch that the
    // constant selects, and the walk reads no other branch of it.
    private bool RunsToItsEnd(SyntaxNode statement, SyntaxNode part, int at) =>
        statement switch
        {
            ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax =>
                this.EndsOnlyAfter(loop: statement, body: part, at: at),
            BlockSyntax
                or SwitchSectionSyntax
                or UsingStatementSyntax
                or LockStatementSyntax
                or CheckedStatementSyntax
                or UnsafeStatementSyntax
                or FixedStatementSyntax
                or ElseClauseSyntax
                or CatchClauseSyntax
                or FinallyClauseSyntax => true,
            IfStatementSyntax choice => this.ConstantOf(choice.Condition) != null,
            TryStatementSyntax attempt =>
                (attempt.Block == part && attempt.Catches.Count == 0)
                || attempt.Finally == part
                || (part is CatchClauseSyntax && EndsInItsCatch(attempt)),
            _ => false
        };

    // True when the code after a try runs only after its one catch clause. The block of the try
    // starts with a throw, and never runs to its end. The object goes to the clause, or it leaves
    // the try statement. In the second case, the code after the try does not run.
    private static bool EndsInItsCatch(TryStatementSyntax attempt) =>
        attempt.Catches.Count == 1 && attempt.Block.Statements.FirstOrDefault() is ThrowStatementSyntax;

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

    // Records the code that does not run after the yield return of the first item, or after a
    // statement that stops in each branch. A try statement with a finally block splits that code:
    // the finally block runs, and the code after the try statement does not. A goto to a label after
    // the yield return can skip it. A break or a continue before it can end a loop around it, and
    // the code after that loop then runs.
    private void StopsAfter(SyntaxNode yielded)
    {
        List<(SyntaxNode Block, int From)> parts = [];
        SyntaxNode? block = null;
        int from = yielded.Span.End;
        SyntaxNode part = yielded;

        for (SyntaxNode? place = yielded.Parent;
             place != null && this.RunsToItsEnd(statement: place, part: part, at: yielded.SpanStart);
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

        if (parts.Count > 0
            && !parts[parts.Count - 1]
                .Block.DescendantNodes()
                .OfType<LabeledStatementSyntax>()
                .Any(label => label.SpanStart > yielded.SpanStart))
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
        this.RunQueries(this.SequenceOf(node));

        // The collection has the state of all its elements, thus each element has that state. An
        // enumerator of this compilation gives what its Current returns also.
        ITypeSymbol? type = this.region.ModelFor(node).GetForEachStatementInfo(node).ElementType;
        LoopState collection = this.evaluator.Eval(node.Expression);
        LoopState ofTheCollection = LoopStateEvaluator.ElementOf(collection: collection, type: type);

        LoopState enumerated = this.Enumerate(node: node, enumeratorState: out LoopState? enumeratorState);
        LoopState elements = ofTheCollection.Join(enumerated);
        this.AwaitsEachItem(node: node, enumerator: enumeratorState);

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
        this.RunQueries(this.SequenceOf(node));

        LoopState ofTheCollection = this.evaluator.Eval(node.Expression).AsElement;
        LoopState enumerated = this.Enumerate(node: node, enumeratorState: out LoopState? enumeratorState);
        LoopState elements = ofTheCollection.Join(enumerated);
        this.AwaitsEachItem(node: node, enumerator: enumeratorState);

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

        // A tuple element converts to the type of its variable with no node that names the operator.
        if (method == null && this.region.Symbols.DeclaresConversions)
        {
            this.Report(
                this.evaluator.FollowPartConversions(
                    deconstruction: node,
                    value: node.Expression,
                    tuple: elements,
                    type: this.region.ModelFor(node).GetForEachStatementInfo(node).ElementType));
        }

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

        // A loop whose condition is the constant false runs no turn.
        if (node.Condition != null && this.ConstantOf(node.Condition) == false)
        {
            this.Visit(node.Condition);

            return;
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
            this.StopsInEachBranchOf(node);

            return;
        }

        this.alwaysTaken.Add(node);
        this.Visit(node.Condition);
        this.Visit(always ? node.Statement : node.Else);
    }

    // An arm that a constant condition rules out never runs, as a branch of an if does not.
    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        if (this.ConstantOf(node.Condition) is not { } always)
        {
            base.VisitConditionalExpression(node);

            return;
        }

        this.Visit(node.Condition);
        this.Visit(always ? node.WhenTrue : node.WhenFalse);
    }

    private bool? ConstantOf(SyntaxNode condition) => ConstantOf(region: this.region, condition: condition);

    /// <summary>
    ///     The value of a condition that is a constant. Null for each other
    ///     condition. Only names, literals, operators, casts, conditional
    ///     expressions, <c>default</c>, <c>sizeof</c>, and <c>nameof</c> can
    ///     make a constant. Thus, the walk asks the compiler only about such a
    ///     condition.
    /// </summary>
    /// <param name="region">The region, for the semantic model.</param>
    /// <param name="condition">The condition.</param>
    public static bool? ConstantOf(LoopRegion region, SyntaxNode condition) =>
        CanBeAConstant(condition) && region.ModelFor(condition).GetConstantValue(condition) is { Value: bool value }
            ? value
            : null;

    private static bool CanBeAConstant(SyntaxNode expression) =>
        expression.DescendantNodesAndSelf(descendIntoChildren: static part => !IsAConstantForm(part))
            .All(static part =>
                part
                    is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax
                    or LiteralExpressionSyntax
                    or ParenthesizedExpressionSyntax
                    or PrefixUnaryExpressionSyntax
                    or BinaryExpressionSyntax
                    or CastExpressionSyntax
                    or PredefinedTypeSyntax
                    or ConditionalExpressionSyntax
                || IsAConstantForm(part));

    // An expression that can be a constant with any type or name in it. The walk does not read the
    // nodes in it.
    private static bool IsAConstantForm(SyntaxNode part) =>
        part is DefaultExpressionSyntax
            or SizeOfExpressionSyntax
            or InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } };

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
        bool[]? cannotRun = this.StatementsThatCannotRun(statements);

        if (!statements.Any(static statement => statement is LabeledStatementSyntax))
        {
            if (cannotRun == null)
            {
                base.VisitBlock(node);
                this.DisposesEachAtTheEndOf(node);

                return;
            }

            for (int at = 0; at < statements.Count; at++)
            {
                this.VisitIfItCanRun(statement: statements[at], cannotRun: cannotRun[at]);
            }

            this.DisposesEachAtTheEndOf(node);

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
                this.VisitIfItCanRun(statement: statements[first], cannotRun: cannotRun?[first] == true);
                first++;

                continue;
            }

            StatementSyntax[] turn = [.. statements.Skip(first).Take(last - first + 1)];
            int before = this.evaluator.ShapeOfWhatALoopLeaves(statements[first]);

            for (int at = first; at <= last; at++)
            {
                this.VisitIfItCanRun(statement: statements[at], cannotRun: cannotRun?[at] == true);
            }

            this.VisitTheSecondTurn(loop: node, body: statements[first], before: before, parts: turn);
            first = last + 1;
        }

        this.DisposesEachAtTheEndOf(node);
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

        // A do whose condition is the constant false runs one turn.
        bool hasOneTurn =
            EndsTheLoopOnEachPath(loop: loop, body: body)
            || (loop is DoStatementSyntax { Condition: var last } && this.ConstantOf(last) == false);

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
        this.EndsTheTurnsOf(body);
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
    // break, a return, a throw, or a goto. A break in a block or in an if is for that loop. A goto
    // leaves the body, or goes to a label in it. In the two cases, control does not come to the
    // end of the body, and only a continue starts the next turn.
    private static bool EndsTheLoop(StatementSyntax statement) =>
        statement switch
        {
            BreakStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax or GotoStatementSyntax => true,
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
        ExpressionSyntax sequence = this.SequenceOf(node);

        if (!(node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)
                ? this.region.Symbols.DeclaresAsyncEnumerators
                : this.region.Symbols.DeclaresEnumerators)
            || this.CallsOf(node: node, sequence: sequence) is not { Start: { } start } calls)
        {
            return LoopState.None;
        }

        // The collection itself, and not one of its elements, has the enumerator.
        LoopState collection = this.evaluator.Self(sequence);

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

        // The enumerator of an iterator runs the code of the iterator at MoveNext. A GetEnumerator of
        // IEnumerable that calls such a GetEnumerator gives it, as a foreach over a dynamic value does.
        if (!enumerator.State.Queries.IsEmpty)
        {
            this.Run(state: enumerator.State, enumerated: null);
        }

        // An iterator has no enumerator object of this compilation, and a method can give the
        // enumerator of a collection of the platform. The result then has the state of the
        // elements.
        if (enumerator.State.Members?.Is(start.ReturnType) != true)
        {
            return enumerator.State;
        }

        enumeratorState = enumerator.State;

        if (calls.Next is { } next)
        {
            this.Report(
                this.evaluator.FollowImplied(
                    call: node.Expression,
                    member: next,
                    owner: enumerator.State,
                    arguments: []));
        }

        if (calls.Current?.GetMethod is not { } getter)
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
            || !(node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)
                ? this.region.Symbols.DeclaresDisposeAsync
                : this.region.Symbols.DeclaresDispose)
            || this.CallsOf(node: node, sequence: this.SequenceOf(node)).Dispose is not { } dispose)
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
        this.RulesOutSectionsOf(node);
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
            this.AwaitsInEachSection(node);
            this.StopsInEachSectionOf(node);

            return;
        }

        int before = this.evaluator.ShapeOfWhatALoopLeaves(node.Sections[first]);
        base.VisitSwitchStatement(node);

        this.VisitTheSecondTurn(
            loop: node,
            body: node.Sections[first],
            before: before,
            parts: [.. node.Sections.Skip(first).Take(last - first + 1)]);

        this.AwaitsInEachSection(node);
        this.StopsInEachSectionOf(node);
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

        this.VisitTheArmsThatCanRun(node);
    }

    /// <summary>
    ///     Gives each variable of a pattern the state of the value that the
    ///     pattern is for. A variable for a member of an object gets the state of
    ///     that member, when the walk knows the members.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="state">The state of the value that the pattern is for.</param>
    /// <param name="isAPosition">
    ///     True when the value is the element at a position of a list pattern,
    ///     and the state is the state of that one position.
    /// </param>
    private void DeclarePattern(PatternSyntax pattern, LoopState state, bool isAPosition = false)
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
                    this.DeclarePattern(pattern: binary.Left, state: state, isAPosition: isAPosition);
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
                    this.DeclareElements(list: list, state: state, isAPosition: isAPosition);

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
    ///     Gives each pattern of a list pattern the state of its element. A slice
    ///     is a part of the list. Each other pattern is for one element.
    /// </summary>
    /// <remarks>
    ///     A list pattern for the value of an <c>is</c> expression or a
    ///     <c>switch</c> can have a collection whose positions the walk knows.
    ///     The pattern matches only a collection with a sufficient number of
    ///     elements. Then, a pattern before the slice is for the element at its
    ///     position from the start. A pattern after the slice is for the element
    ///     at its position from the end. The slice is for the elements between.
    ///     A list pattern for the element at such a position can have the
    ///     positions that the store has for that element.
    /// </remarks>
    private void DeclareElements(ListPatternSyntax list, LoopState state, bool isAPosition)
    {
        SeparatedSyntaxList<PatternSyntax> patterns = list.Patterns;
        int slice = patterns.IndexOf(static pattern => pattern is SlicePatternSyntax);
        IReadOnlyList<LoopState>? positions = this.PositionsFor(list: list, position: isAPosition ? state : null);

        for (int i = 0; i < patterns.Count; i++)
        {
            int last = positions == null ? 0 : positions.Count - (patterns.Count - i);

            this.DeclarePattern(
                pattern: patterns[i],
                state: (positions, i == slice) switch
                {
                    (null, true) => state,
                    (null, false) => state.AsElement,
                    ({ } known, true) => known
                        .Skip(i)
                        .Take(last - i + 1)
                        .Aggregate(seed: LoopState.None, func: static (joined, one) => joined.Join(one))
                        .AsContainer,
                    ({ } known, false) => this.evaluator.NowInTheStore(known[slice < 0 || i < slice ? i : last])
                },
                isAPosition: positions != null && i != slice);
        }
    }

    // The state at each position of the collection that a list pattern is for, when the walk knows
    // them. An and pattern and parentheses keep the value that they test. The position is the state
    // of one position of a list pattern around this one.
    private IReadOnlyList<LoopState>? PositionsFor(ListPatternSyntax list, LoopState? position)
    {
        SyntaxNode? above = list.Parent;

        while (above is ParenthesizedPatternSyntax or BinaryPatternSyntax { RawKind: (int)SyntaxKind.AndPattern })
        {
            above = above.Parent;
        }

        IReadOnlyList<LoopState>? positions =
            position != null
                ? this.evaluator.StatesAtThePositionsIn(position)
                : above is IsPatternExpressionSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax
                  && MatchedValueOf(list) is { } matched
                    ? this.evaluator.StatesAtThePositionsOf(matched)
                    : null;

        SeparatedSyntaxList<PatternSyntax> patterns = list.Patterns;
        int slice = patterns.IndexOf(static pattern => pattern is SlicePatternSyntax);

        return positions != null
               && (slice < 0 ? positions.Count == patterns.Count : positions.Count >= patterns.Count - 1)
            ? positions
            : null;
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

        return LoopStateEvaluator.StateOfAMember(
                owner: owner,
                member: member,
                implementations: this.region.Symbols.Implementations)
            .Join(this.evaluator.ResultOfATask(name: name, member: member, task: owner));
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

        IReadOnlyList<LoopState>? states =
            method == null
                ? this.ItemsOfATuple(clause: clause, state: state)
                : PartsOfAPair(pair: state, deconstruct: method);

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

    // The state of each item of a tuple that a positional pattern is for, in order. Null for a value
    // that is not a tuple with a state for each item.
    private IReadOnlyList<LoopState>? ItemsOfATuple(SyntaxNode clause, LoopState state) =>
        state.Members is { IsTuple: true } members
        && clause.Parent is { } pattern
        && (this.region.ModelFor(pattern).GetOperation(pattern) as IRecursivePatternOperation)?.MatchedType
            is INamedTypeSymbol { IsTupleType: true } tuple
        && tuple.TupleElements.All(members.Has)
            ? [.. tuple.TupleElements.Select(item => members.StateOf(item).FromIfUnknown(state.Origin))]
            : null;

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

        // An out parameter converts to the type of its variable with no node that names the operator.
        if (this.region.Symbols.DeclaresConversions)
        {
            this.Report(this.evaluator.FollowOutConversions(deconstruction: call, value: value, states: states));
        }

        ImmutableArray<IParameterSymbol> outs = OutsOf(method);

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

            if (i < outs.Length)
            {
                this.evaluator.NotesAFieldGivenOut(
                    target: parts[i],
                    parameter: outs[i],
                    method: method,
                    receiver: value,
                    call: call,
                    replaces: replaces);
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

        // The binder finds the setter of a dynamic value at run time. Only such a target, and a
        // target that does not bind, has no symbol.
        if (accessor == null)
        {
            if (this.region.SymbolOf(node.Left) == null)
            {
                this.Report(this.evaluator.FollowDynamicWrite(node));
            }

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
                this.evaluator.PostsAWrite(bindable: written.Expression, value: node.Right, write: node);

                this.Report(
                    this.evaluator.FollowComparerOfAWrite(
                        bindable: written.Expression,
                        value: node.Right,
                        write: node));
            }

            // A bindable raises its events in an action that it posts to its scheduler, which runs
            // it after the transaction. Thus, a send in a handler gives content to the sink after
            // the close.
            if (node.IsKind(SyntaxKind.AddAssignmentExpression)
                && node.Left is MemberAccessExpressionSyntax { Expression: var bindable }
                && (this.region.Symbols.IsBindable(this.region.TypeOf(bindable))
                    || this.IsABindableAction(value: bindable, state: this.evaluator.Eval(bindable))))
            {
                this.evaluator.RunsAfterTheTransaction(node.Right);
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
                // The owner of a null-conditional assignment is the value before the mark.
                MemberBindingExpressionSyntax binding => (LoopStateEvaluator.ConditionalReceiver(binding), null),
                ElementBindingExpressionSyntax binding =>
                    (LoopStateEvaluator.ConditionalReceiver(binding), binding.ArgumentList),
                // An index in an object initializer is an element of the new object.
                ImplicitElementAccessSyntax
                {
                    Parent.Parent.Parent: (BaseObjectCreationExpressionSyntax or WithExpressionSyntax)
                    and ExpressionSyntax made
                } element => (made, element.ArgumentList),
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
        this.ForgetsTheRunsThrough(node.Left);

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
                && this.RemovesOnEachPath(node)
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
            // The owner of a null-conditional assignment is before the mark, and the walk reads it
            // there.
            case MemberBindingExpressionSyntax:
                break;
            case ElementBindingExpressionSyntax binding:
                this.Visit(binding.ArgumentList);
                break;
            default:
                this.Visit(node.Left);
                break;
        }

        this.Visit(node.Right);

        // A write to a static member runs the static constructor of its type, after the value.
        this.Report(
            node.Left switch
            {
                MemberAccessExpressionSyntax access => this.evaluator.FollowTheStaticConstructor(access),
                IdentifierNameSyntax name => this.evaluator.FollowTheStaticConstructor(name),
                _ => null
            });

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

        // The same for a deconstruction of a tuple that is not a tuple literal. Each element of a
        // literal has a node of its own, and the walk asks there.
        if (deconstruct == null
            && this.region.Symbols.DeclaresConversions
            && node.Right is not TupleExpressionSyntax
            && PartsOf(node.Left) != null)
        {
            this.Report(
                this.evaluator.FollowPartConversions(
                    deconstruction: node,
                    value: node.Right,
                    tuple: right,
                    type: this.region.TypeOf(node.Right)));
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
            // A null-conditional assignment runs when its owner is not null. Each object that the
            // owner can name then gets the write. A chain of such accesses is the same.
            ConditionalAccessExpressionSyntax conditional
                when conditional.WhenNotNull == assignment
                     && AroundTheChain(conditional) is ExpressionStatementSyntax statement =>
                this.AlwaysRunsAt(place: statement, turn: out turn),
            // The expression that is the body of a function is the one statement of that function.
            ArrowExpressionClauseSyntax clause => this.AlwaysRunsAt(place: clause, turn: out turn),
            AnonymousFunctionExpressionSyntax lambda when lambda.ExpressionBody == assignment =>
                this.AlwaysRunsAt(place: assignment, turn: out turn),
            _ => false
        };
    }

    // The node around a chain of null-conditional accesses, where each access is the part after the
    // mark of the access around it.
    private static SyntaxNode? AroundTheChain(SyntaxNode conditional)
    {
        SyntaxNode node = conditional;

        while (node.Parent is ConditionalAccessExpressionSyntax outer && outer.WhenNotNull == node)
        {
            node = outer;
        }

        return node.Parent;
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
        if (WhereItAlwaysRuns(expression: call, constants: null) is not { } place)
        {
            return (false, null);
        }

        bool inItsFunction = this.AlwaysRunsAt(place: place, turn: out SyntaxNode? turn);

        return (inItsFunction, turn);
    }

    // The statement that always evaluates an expression when the statement runs. For the
    // expression that is the body of a lambda or of a local function, that expression or its
    // clause. Null when an operator, a conditional expression, or a function between the two can
    // skip the expression. With the region, a constant condition skips no part that the walk reads.
    private static SyntaxNode? WhereItAlwaysRuns(SyntaxNode expression, LoopRegion? constants)
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

            if (CanSkip(parent: parent, child: child, constants: constants))
            {
                return null;
            }
        }

        return null;
    }

    // True when an expression can skip one of its parts. Such a part is an arm of a conditional
    // expression, or the part after a conditional access. The right side of an operator that stops
    // at its left side is one also. With the region, a conditional expression on a constant skips
    // nothing, because the walk reads only the arm that the constant selects.
    private static bool CanSkip(SyntaxNode parent, SyntaxNode child, LoopRegion? constants) =>
        parent switch
        {
            ConditionalExpressionSyntax conditional =>
                conditional.Condition != child
                && (constants == null || ConstantOf(region: constants, condition: conditional.Condition) == null),
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
    // For a first read, also see MarksOfAFirstRead.
    // A call asks with forACall. For it, a goto to a label before the call is no exit, and the block
    // of a try with no catch clause runs the call before the code after that try. Also, the arguments
    // of the code can give a condition a known value: see ValueOf. A run asks with forARun.
    private bool AlwaysRunsAt(
        SyntaxNode place,
        bool forAFirstRead,
        bool forACall,
        bool forARun,
        out SyntaxNode? turn,
        out TryStatementSyntax? after)
    {
        turn = null;
        after = null;

        bool inALambda = false;
        SyntaxNode from = place;

        foreach (SyntaxNode ancestor in place.Ancestors())
        {
            SyntaxNode child = from;
            from = ancestor;

            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda when this.entered.Contains(lambda):
                    if (this.ExitsBefore(
                            function: lambda,
                            place: place.SpanStart,
                            forACall: forACall,
                            forARun: forARun))
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
                    if (forAFirstRead && this.HasAFirstTurn(ancestor))
                    {
                        continue;
                    }

                    turn = body;

                    return false;
                // An if runs the part that RunsThePartOf finds, and each other construct here runs the
                // code that AlwaysRunsItsCode finds. The finally block runs each time that the try
                // statement runs.
                case IfStatementSyntax branch
                    when this.RunsThePartOf(branch: branch, part: child, forACall: forACall, forARun: forARun):
                case ElseClauseSyntax
                    or SwitchSectionSyntax
                    or SwitchStatementSyntax
                    or LabeledStatementSyntax
                    when this.AlwaysRunsItsCode(construct: ancestor, place: place):
                case FinallyClauseSyntax:
                case TryStatementSyntax when child is FinallyClauseSyntax:
                    continue;
                // The code after a try with no catch clause runs only when the block runs to its end.
                // The finally block can run before that.
                case TryStatementSyntax { Catches.Count: 0 } attempt when forAFirstRead || forACall:
                    after = attempt;

                    continue;
                case SwitchSectionSyntax section
                    when this.IsTheSectionOfEachValue(section: section, at: place.SpanStart):
                case SwitchStatementSyntax when child is SwitchSectionSyntax:
                    continue;
                // A call of the function that ends at a statement before the assignment does not
                // run the assignment.
                case AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or MemberDeclarationSyntax
                    or AccessorDeclarationSyntax:
                    // A return statement is no statement before itself.
                    return !this.ExitsBefore(
                        function: ancestor,
                        place: place.SpanStart,
                        forACall: forACall,
                        forARun: forARun);
                // A block, and a statement that always runs what is in it. Also the call that
                // runs a lambda, and the statement that the call is in. The if around an else clause
                // tells if the clause runs.
                case BlockSyntax
                    or ElseClauseSyntax
                    or LabeledStatementSyntax
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

        // A conditional ref is the location of one of its two branches, and the walk does not know which.
        if (target is ConditionalExpressionSyntax
            {
                WhenTrue: RefExpressionSyntax whenTrue, WhenFalse: RefExpressionSyntax whenFalse
            })
        {
            ExpressionSyntax[] locations = [whenTrue.Expression, whenFalse.Expression];

            foreach (ExpressionSyntax location in locations)
            {
                this.Assign(target: location, value: value, replaces: false, turn: null);
            }

            return;
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

        // A ref field of a ref struct in a local refers to a local: see TheLocalOfARefField.
        if (value is not RefExpressionSyntax
            && target is MemberAccessExpressionSyntax throughAField
            && this.evaluator.TheLocalOfARefField(throughAField) is { } referred)
        {
            target = referred;
        }

        // An indexer or a method of this compilation can return a ref to a member of its object. An
        // assignment through it puts the value there, and not into the object.
        if (this.PutsThroughARefToAMember(target: target, value: value))
        {
            return;
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

        // A local of a value with no object is a name of the element or the field that gets it.
        this.evaluator.NotesALocalOfALocation(location: target, value: value, replaces: replaces);
        this.evaluator.NotesNamesOfOneObject(target: target, value: value, replaces: replaces);
        this.evaluator.NotesALinkOfALocation(location: target, value: value, replaces: replaces);

        if (target is ElementAccessExpressionSyntax or MemberAccessExpressionSyntax or IdentifierNameSyntax)
        {
            this.evaluator.NotesANameOfThis(
                local: null,
                location: (ExpressionSyntax)target,
                value: value,
                replaces: replaces);
        }

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

        if (ElementTarget(target) is (var indexed, _))
        {
            positions = this.evaluator.PositionsBeforeAnAssignment(collection: indexed, replaces: replaces, turn: turn);
            this.evaluator.Gets(collection: indexed, value: value);
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

            if (ElementTarget(target) is (_, var indexes))
            {
                this.evaluator.HasThePositionAfter(before: positions, indexes: indexes, value: value);
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
            // A method of a struct can give this a new value, which replaces the variable of the caller.
            case ThisExpressionSyntax when state.Members is { } written:
                this.evaluator.SetEachMemberOfThis(written: written, replaces: replaces);

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
            // An element of a span over a local by ref is the local.
            case ElementAccessExpressionSyntax over when this.evaluator.TheLocalOfASpanOver(over) is { } local:
                // ReSharper disable once TailRecursiveCall - It runs one time more at most: a local is no element.
                this.AssignState(target: local, state: state, replaces: replaces, turn: turn, from: from);

                return;
            case ElementAccessExpressionSyntax or ElementBindingExpressionSyntax
                when ElementTarget(target) is (var collection, var indexes):
                this.evaluator.ForgetsThePositionsOf(collection);
                this.evaluator.Absorb(target: collection, state: state);
                this.evaluator.PutsThroughAFieldThatACallReturns(collection: collection, state: state);

                if (target is ElementAccessExpressionSyntax put)
                {
                    this.evaluator.PutsThroughALocalOfAField(element: put, state: state, replaces: replaces);
                }

                this.evaluator.PutsIntoALocation(
                    field: null,
                    element: target as ElementAccessExpressionSyntax,
                    state: state,
                    owner: collection);

                if (this.region.SymbolOf(target) is IPropertySymbol { DeclaringSyntaxReferences.IsEmpty: true })
                {
                    foreach (ArgumentSyntax index in indexes.Arguments)
                    {
                        this.evaluator.Absorb(target: collection, state: this.evaluator.Eval(index.Expression));
                    }
                }

                // The collection holds the object of the value. An assignment with a value
                // expression recorded that from the expression.
                if (from == null)
                {
                    this.evaluator.GetsAState(collection: collection, put: state);
                }

                // An element of a view of a local array is an element of that array.
                if (target is ElementAccessExpressionSyntax element
                    && this.evaluator.TheArrayOfAViewOf(element) is { } viewed)
                {
                    (MadeObject Made, ImmutableArray<LoopState> All, int First, int Count)? before =
                        element.ArgumentList.Arguments is { Count: 1 } arguments
                            ? this.evaluator.PositionsThroughAView(
                                array: viewed,
                                view: element.Expression,
                                index: arguments[0],
                                replaces: replaces,
                                turn: turn)
                            : null;

                    this.evaluator.PutsThroughAView(array: viewed, value: from, state: state);
                    this.evaluator.HasTheValueThroughAView(before: before, value: from, state: state);
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
                    owner: target switch
                    {
                        MemberAccessExpressionSyntax access => access.Expression,
                        MemberBindingExpressionSyntax binding => LoopStateEvaluator.ConditionalReceiver(binding),
                        _ => null
                    },
                    member: assigned,
                    state: state,
                    replaces: replaces,
                    turn: turn,
                    from: from);

                break;
        }
    }

    // The members that an indexer, a property, or a method returns a ref to, for an assignment through that ref.
    // True when the target is such a ref. A write through a ref to a member of this is not one.
    private bool PutsThroughARefToAMember(SyntaxNode target, ExpressionSyntax value)
    {
        (IMethodSymbol? definition, ExpressionSyntax? receiver) =
            target switch
            {
                ElementAccessExpressionSyntax element
                    when this.region.Symbols.DeclaresIndexers
                         && this.region.SymbolOf(element) is IPropertySymbol
                         {
                             ReturnsByRef: true, GetMethod: { } getter
                         } => (getter.OriginalDefinition, element.Expression),
                InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call
                    when this.region.SymbolOf(call) is IMethodSymbol { ReturnsByRef: true, ReducedFrom: null } method =>
                    (method.OriginalDefinition, access.Expression),
                MemberAccessExpressionSyntax access
                    when this.region.SymbolOf(access)
                        is IPropertySymbol { ReturnsByRef: true, GetMethod: { } getter } =>
                    (getter.OriginalDefinition, access.Expression),
                _ => ((IMethodSymbol?)null, (ExpressionSyntax?)null)
            };

        if (definition == null
            || receiver is null or ThisExpressionSyntax
            || this.evaluator.MembersOfARefReturn(definition) is not { Count: > 0 } members)
        {
            return false;
        }

        LoopState state = this.evaluator.Eval(value);

        foreach ((ISymbol member, bool isAnElement) in members)
        {
            if (isAnElement)
            {
                this.evaluator.PutsIntoAnElementOf(owner: receiver, member: member, state: state);
            }
            else
            {
                this.evaluator.SetMemberOf(owner: receiver, member: member, state: state, replaces: false, turn: null);
            }
        }

        return true;
    }

    // The collection and the indexes of an element that an assignment writes. The collection of a
    // null-conditional assignment is the value before the mark.
    private static (ExpressionSyntax Collection, BracketedArgumentListSyntax Indexes)? ElementTarget(
        SyntaxNode target) =>
        target switch
        {
            ElementAccessExpressionSyntax element => (element.Expression, element.ArgumentList),
            ElementBindingExpressionSyntax binding when LoopStateEvaluator.ConditionalReceiver(binding) is { } owner =>
                (owner, binding.ArgumentList),
            _ => null
        };

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
            this.Report(this.evaluator.FollowSpreadConversion(collection: node, spread: spread.Expression));
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
            ? members.StateThrough(
                member: this.evaluator.InitializedThrough(member),
                implementations: this.region.Symbols.Implementations)
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
            // A fold of a collection runs its functions over the items, whose states are the content
            // of the collection.
            case CalleeKind.Library:
                this.VisitArguments(
                    arguments: arguments,
                    runs: Runs,
                    given: OperatorTable.ReadsReceiverAtConstruction(LoopStateEvaluator.OperatorName(method))
                        ? this.evaluator.Eval(receiver).Content
                        : LoopState.None,
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

                switch (method.Name)
                {
                    // A map through a task sends the result of its operation into its sink of
                    // results after the transaction.
                    case "MapAsync":
                        this.evaluator.SendsTheResults(call: node, method: method, arguments: arguments);

                        break;
                    // Debounce reads the time of its timer system, and calls its At, in the call.
                    case "Debounce":
                        this.Report(
                            this.evaluator.FollowTheTimersOfADebounce(
                                call: node,
                                method: method,
                                arguments: arguments));

                        break;
                    // Execute of the status of a map through a task gives the operation a value
                    // after the transaction.
                    case "Execute" when receiver != null && arguments.Count == 1:
                        this.evaluator.SendsTheResultsOf(
                            execute: method,
                            status: receiver,
                            value: arguments[0].Expression);

                        break;
                    // A scheduler of a bindable runs a posted action after the transaction, as
                    // Transaction.Post does. The contract of IBindingScheduler.Post forbids a run
                    // while a transaction is open. Each Post of the library with a receiver is that
                    // Post. A scheduler of this code can run the action in the call, against that
                    // contract. Thus, the walk also reads the Post of the class of an object that
                    // it knows.
                    case "Post" when receiver != null && arguments.Count == 1:
                        this.Report(
                            this.evaluator.FollowImplied(
                                call: node,
                                member: method,
                                owner: this.evaluator.Eval(receiver),
                                arguments: [(arguments[0].Expression, this.evaluator.Eval(arguments[0].Expression))]));

                        this.evaluator.RunsAfterTheTransaction(arguments[0].Expression);

                        break;
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
                // that it gets as an argument. Array.Fill puts its value into the elements of its
                // array, and changes nothing else.
                if (LoopStateEvaluator.IsArrayFill(method))
                {
                    (bool always, SyntaxNode? itsTurn) = this.WhereACallRuns(node);

                    this.evaluator.Fills(method: method, arguments: arguments, replaces: always, turn: itsTurn);
                }
                else if (LoopStateEvaluator.IsArrayReverse(method: method, arguments: arguments))
                {
                    (bool always, SyntaxNode? itsTurn) = this.WhereACallRuns(node);

                    this.evaluator.Reverses(arguments: arguments, replaces: always, turn: itsTurn);
                }
                else
                {
                    // Only Fill of a view can give positions, thus only it needs where the call runs.
                    (bool always, SyntaxNode? itsTurn) =
                        method.Name == "Fill" ? this.WhereACallRuns(node) : (false, null);

                    this.evaluator.CanChangeItsArguments(method: method, arguments: arguments, receiver: receiver);

                    this.evaluator.PutsThroughViews(
                        method: method,
                        receiver: receiver,
                        arguments: arguments,
                        replaces: always,
                        turn: itsTurn);
                }

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
                        LoopState added = this.evaluator.Moved(invocation: node, method: method, withReceiver: false);

                        this.evaluator.Absorb(target: receiver, state: added);

                        this.evaluator.AddsThroughALocalOfAField(
                            list: receiver,
                            call: node,
                            method: method,
                            state: added);
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
                                indexes: assigned.ArgumentList,
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
                    && this.IsABindableAction(value: receiver, state: this.evaluator.Eval(receiver))))
            {
                this.CheckRead(node: node, owner: receiver, name: method.Name);
            }

            // An extension method with the name of a member of the library is not that member.
            switch (operatorName)
            {
                // The Dispose of a map of a collection runs its eviction handler in the call. The
                // code can call it through IDisposable. The Dispose of a timer system calls Stop of
                // its implementation after the transaction releases its lock, thus not in the block.
                case "Dispose" when method.ReducedFrom == null:
                    this.CancelsATimerAt(call: node, timer: receiver);

                    this.Report(
                        this.evaluator.FollowEvictionsOfADispose(
                            map: receiver,
                            dispose: node,
                            runsOnEachPath: () => this.SurelyRuns(call: node, forARun: true, after: out _)));

                    break;
                // A clock of the library base keeps the callback of SetTimer. Its RunTimersTo runs
                // each callback that is due, in the call.
                case "SetTimer" or "RunTimersTo"
                    when method.ReducedFrom == null && this.evaluator.IsAClockOfTheLibraryBase(receiver):
                    this.RunsTimers(
                        clocks: this.evaluator.Eval(receiver).Objects,
                        method: method,
                        arguments: arguments,
                        call: node);

                    break;
                // Cancel of a timer of a clock of the library base takes that timer from its clock.
                case "Cancel" when method.ReducedFrom == null && arguments.Count == 0:
                    this.CancelsATimerAt(call: node, timer: receiver);

                    break;
                // Execute of a bindable action posts a send of its parameter into the sink of its
                // firings.
                case "Execute" when method.ReducedFrom == null && arguments.Count == 1:
                    this.evaluator.PostsAWrite(bindable: receiver, value: arguments[0].Expression, write: node);

                    break;
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

    // SetTimer of a clock of the library base keeps a timer. RunTimersTo runs the callback of each
    // timer whose time is not after the time of the run, one time. A time that is not a constant
    // can be due. The store keeps the timers of each clock that it has, thus a field, a property, a
    // callee, and a second name each find them. The callback keeps the state that it had at the
    // call of SetTimer. A callee that runs it cannot read the code that gave it.
    private void RunsTimers(
        ImmutableArray<MadeObject> clocks,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SyntaxNode call)
    {
        if (method.Name == "SetTimer")
        {
            if (Argument("time") is { } time && Argument("callback") is { } callback)
            {
                this.region.Objects.KeepsATimer(
                    clocks: this.region.Objects.Now(clocks),
                    timer: new KeptTimer(
                        set: call,
                        time: time,
                        callback: callback,
                        functions: this.evaluator.Eval(callback),
                        keeper: this));

                foreach (MadeObject made in this.evaluator.Eval(call as ExpressionSyntax).Objects)
                {
                    this.region.Objects.IsATimerOf(made: made, set: call);
                }
            }

            return;
        }

        if (Argument("now") is not { } now)
        {
            return;
        }

        // A callee runs a timer of a different code only where the call runs on all paths, and so
        // does each call between that code and the callee. The walk does not know the path that the
        // arguments of a callee select. The code that called the method that set the timer is not a
        // different code.
        bool always = this.SurelyRuns(call: call, forARun: true, after: out _);

        foreach (KeptTimer timer in this.region.Objects.TimersOf(this.region.Objects.Now(clocks))
                     .Where(timer =>
                         (timer.Keeper.evaluator.Frame.IsWithin(this.evaluator.Frame)
                          || (always
                              && this.evaluator.CallsRunFor(
                                  owns: timer.Keeper.evaluator.Frame.IsWithin,
                                  forARun: true)))
                         && !this.IsAfter(time: timer.Time, now: now))
                     .ToList())
        {
            this.region.Objects.TakesTheTimer(timer.Set);

            this.Report(
                this.evaluator.FollowCallback(call: call, callback: timer.Callback, functions: timer.Functions));
        }

        return;

        ExpressionSyntax? Argument(string name) =>
            arguments
                .Where((argument, position) =>
                    LoopStateEvaluator.ParameterFor(method: method, argument: argument, position: position)?.Name
                    == name)
                .Select(static argument => argument.Expression)
                .FirstOrDefault();
    }

    // A call that the code can skip keeps the timer. A loop that always has a first turn runs the
    // call. In the block of a try, the call takes the timer at the end of the try. The finally
    // block can run without the call. In a lambda, a local function, or a callee, each call of the
    // code up to the block must run on all paths also.
    private void CancelsATimerAt(SyntaxNode call, ExpressionSyntax timer)
    {
        if (!this.region.Objects.HasTimers
            || !this.SurelyRuns(call: call, forARun: false, after: out TryStatementSyntax? after)
            || !this.evaluator.CallsRunFor(owns: static _ => false, forARun: false))
        {
            return;
        }

        if (after == null)
        {
            this.CancelsATimer(timer);

            return;
        }

        (this.afterTheTry ??= []).Add((after, () => this.CancelsATimer(timer)));
    }

    // A Cancel or a Dispose of a timer takes it from its clock, which then does not run its
    // callback. The timer is the result of a call of SetTimer, or a local that no code writes and
    // that has that result. A value that is one timer of the store is that timer also, as a
    // parameter or an element can be. A timer of a turn before is two or more timers.
    private void CancelsATimer(ExpressionSyntax timer)
    {
        if ((this.evaluator.CallOfSetTimer(timer) ?? SetOfTheOneTimer()) is { } set)
        {
            this.region.Objects.TakesTheTimer(set);
        }

        return;

        SyntaxNode? SetOfTheOneTimer()
        {
            ImmutableArray<MadeObject> objects = this.region.Objects.Now(this.evaluator.Eval(timer).Objects);

            return (objects.Length == 1 ? objects[0] : null) is { IsOfTurnsBefore: false } one
                ? this.region.Objects.SetOfATimer(one)
                : null;
        }
    }

    // True when two times are integer constants, and the first is after the second.
    private bool IsAfter(SyntaxNode time, SyntaxNode now) =>
        this.IntegerOf(time) is { } later && this.IntegerOf(now) is { } at && later > at;

    private decimal? IntegerOf(SyntaxNode expression) =>
        this.region.ModelFor(expression).GetConstantValue(expression) is
        {
            HasValue: true, Value: sbyte or byte or short or ushort or int or uint or long or ulong
        } constant
            ? Convert.ToDecimal(value: constant.Value, provider: CultureInfo.InvariantCulture)
            : null;

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
        // The creation of a type parameter has no constructor symbol. It runs the constructor of
        // the type argument, when the call of this code says that type.
        if ((this.region.SymbolOf(node) as IMethodSymbol ?? this.evaluator.ConstructorOfATypeArgument(node))
            is not { } constructor)
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

        // The static constructors run before the constructor of the object.
        if (node is BaseObjectCreationExpressionSyntax)
        {
            this.Report(this.evaluator.FollowTheStaticConstructors(creation: node, constructor: constructor));
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
            // The evaluator followed the body.
            case CalleeKind.Body:
                break;
            // The library has no constructor to read. The constructor of a timer system calls
            // Start and Now of its implementation.
            case CalleeKind.Library:
                this.Report(
                    this.evaluator.FollowClock(system: constructor, arguments: arguments.Arguments, call: node));

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
            SerializerOptions options = SerializerOptions.Of(region: this.region, method: method, arguments: arguments);
            ITypeSymbol? named = SodaFlowSymbols.WritesTheDeclaredType(callee) ? this.NamedType(arguments) : null;

            foreach (ArgumentSyntax argument in arguments)
            {
                this.Report(this.evaluator.FollowEnumeration(argument.Expression));

                this.Serialize(
                    written: argument.Expression,
                    state: this.evaluator.Eval(argument.Expression),
                    serializer: callee,
                    options: options,
                    member: null,
                    declared: SodaFlowSymbols.WritesTheDeclaredType(callee)
                              && LoopStateEvaluator.ParameterFor(
                                      method: method,
                                      argument: argument,
                                      position: arguments.IndexOf(argument))
                                  is { } parameter
                        ? named ?? method.Parameters[parameter.Ordinal].Type
                        : null,
                    depth: 0,
                    seen: new HashSet<(Construction, INamedTypeSymbol?)>(SerializedObjects.Instance));
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

        if (receiver != null
            && this.WritesANode(method: method, callee: callee, receiver: receiver, arguments: arguments))
        {
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

    // A Type argument of System.Text.Json names the type that it writes for the value. Each other
    // argument of such a call is an object of the platform, which has no getter of the code. The
    // walk knows the type only from a typeof.
    private ITypeSymbol? NamedType(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        arguments.Select(static argument => argument.Expression).OfType<TypeOfExpressionSyntax>().FirstOrDefault()
            is { } type
            ? this.region.ModelFor(type).GetTypeInfo(type.Type).Type
            : null;

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
    ///     compilation, it calls the getter of each property that it writes, and
    ///     reads each field that it writes. It writes the value of that member in
    ///     the same way. A converter can write a value for the serializer.
    /// </summary>
    /// <remarks>
    ///     An object of this compilation can keep a query that no property gives.
    ///     The serializer does not get that query. Thus, the walk reads the members
    ///     of such an object only through its getters.
    /// </remarks>
    /// <param name="written">The argument. A report about the value is at this node.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="serializer">The method of the serializer.</param>
    /// <param name="options">The options of the call.</param>
    /// <param name="member">The property or the field that has the value. Null for an argument.</param>
    /// <param name="declared">
    ///     The type that the call or the property gives the value, for a
    ///     serializer that writes it. Null for each other serializer.
    /// </param>
    /// <param name="depth">The number of objects that the value is in.</param>
    /// <param name="seen">
    ///     Each object of this compilation whose members the walk read for the
    ///     argument, with the type that the serializer wrote it as.
    /// </param>
    private void Serialize(
        ExpressionSyntax written,
        LoopState state,
        IMethodSymbol serializer,
        SerializerOptions options,
        ISymbol? member,
        ITypeSymbol? declared,
        int depth,
        ISet<(Construction, INamedTypeSymbol?)> seen)
    {
        // The elements of a tuple are fields, and not each serializer writes a field.
        if (state.Members is { IsTuple: true } && !options.WritesFields)
        {
            return;
        }

        this.Run(state: state, enumerated: null);

        // System.Text.Json stops with an error at this depth by default. Json.NET has no such
        // limit, and the walk stops there also.
        if (state.Members is not { } members || depth > MaxSerializedDepth)
        {
            return;
        }

        // An anonymous object or a tuple: the serializer writes each value in it.
        if (!members.HasType)
        {
            foreach (KeyValuePair<ISymbol, LoopState> part in members.Members)
            {
                this.Serialize(
                    written: written,
                    state: part.Value,
                    serializer: serializer,
                    options: options,
                    member: null,
                    declared: null,
                    depth: depth + 1,
                    seen: seen);
            }

            return;
        }

        INamedTypeSymbol? own = WrittenTypeOf(declared: declared, members: members);

        if (this.WritesThroughAConverter(
                written: written,
                state: state,
                serializer: serializer,
                options: options,
                member: member,
                own: own))
        {
            return;
        }

        // A second read of the same object as the same type finds the same reads, at the same
        // node. A getter of the object can give the object again, thus the walk stops there.
        if (!seen.Add((members, own)))
        {
            return;
        }

        foreach (IPropertySymbol property in own == null ? members.Properties : Construction.PropertiesOf(own))
        {
            if (property.GetMethod is not { } read
                || !SodaFlowSymbols.IsSerialized(property: property, serializer: serializer, options: options))
            {
                continue;
            }

            this.ReadTheTestsOf(written: written, state: state, serializer: serializer, member: property);

            if (this.evaluator.FollowImpliedOfTheObject(call: written, member: read, owner: state, arguments: [])
                is { } getter)
            {
                this.Report(getter);

                this.Serialize(
                    written: written,
                    state: getter.State,
                    serializer: serializer,
                    options: options,
                    member: property,
                    declared: declared == null ? null : property.Type,
                    depth: depth + 1,
                    seen: seen);
            }
        }

        IEnumerable<IFieldSymbol> fields =
            (own == null ? members.Fields : Construction.FieldsOf(own))
            .Where(field => SodaFlowSymbols.IsSerialized(field: field, serializer: serializer, options: options));

        foreach (IFieldSymbol field in fields)
        {
            this.ReadTheTestsOf(written: written, state: state, serializer: serializer, member: field);

            this.Serialize(
                written: written,
                state: LoopStateEvaluator.StateOfAMember(
                    owner: state,
                    member: field,
                    implementations: this.region.Symbols.Implementations),
                serializer: serializer,
                options: options,
                member: field,
                declared: declared == null ? null : field.Type,
                depth: depth + 1,
                seen: seen);
        }
    }

    // The serializer reads the ShouldSerialize method and the Specified getter of a member before it
    // writes the member.
    private void ReadTheTestsOf(SyntaxNode written, LoopState state, IMethodSymbol serializer, ISymbol member)
    {
        if (SodaFlowSymbols.ShouldSerializeOf(member: member, serializer: serializer) is { } test)
        {
            this.Report(
                this.evaluator.FollowImpliedOfTheObject(call: written, member: test, owner: state, arguments: []));
        }

        if (SodaFlowSymbols.SpecifiedOf(member: member, serializer: serializer) is { } specified)
        {
            this.Report(
                this.evaluator.FollowImpliedOfTheObject(call: written, member: specified, owner: state, arguments: []));
        }
    }

    /// <summary>
    ///     A converter that writes a value for the serializer. The first
    ///     is the one that the property or the field names. Then comes one of the
    ///     options for the type, and then the one that the type names. The walk
    ///     reads its write method with the value. The result is false with no
    ///     such converter.
    /// </summary>
    /// <remarks>
    ///     The serializer makes the converter of an attribute, which thus has no
    ///     state. A converter of the options has the state of its creation.
    /// </remarks>
    private bool WritesThroughAConverter(
        ExpressionSyntax written,
        LoopState state,
        IMethodSymbol serializer,
        SerializerOptions options,
        ISymbol? member,
        ISymbol? own)
    {
        if (state.Members is not { } members)
        {
            return false;
        }

        if ((Named(member)
             ?? Listed()
             ?? (own == null ? members.Types.Select(Named).FirstOrDefault(static found => found != null) : Named(own)))
            is not { } converter)
        {
            return false;
        }

        this.Report(
            this.evaluator.FollowImpliedOfTheObject(
                call: written,
                member: converter.Write,
                owner: converter.State,
                arguments: [(written, LoopState.None), (written, state), (written, LoopState.None)]));

        return true;

        (LoopState State, IMethodSymbol Write)? Named(ISymbol? holder) =>
            holder != null
            && SodaFlowSymbols.ConverterOf(holder) is { } type
            && SodaFlowSymbols.WriteOf(converter: type, serializer: serializer) is { } write
                ? (LoopState.None.WithMembers(Construction.Of(type)), write)
                : null;

        // A converter of System.Text.Json writes the type that it names, and only that type.
        (LoopState State, IMethodSymbol Write)? Listed() =>
            options.Converters
                .Select(made =>
                    this.region.TypeOf(made) is INamedTypeSymbol type
                    && SodaFlowSymbols.HandledBy(type) is { } handled
                    && (own == null ? members.Is(handled) : SymbolEqualityComparer.Default.Equals(x: handled, y: own))
                    && SodaFlowSymbols.WriteOf(converter: type, serializer: serializer) is { } write
                        ? (this.evaluator.Eval(made), write)
                        : ((LoopState State, IMethodSymbol Write)?)null)
                .FirstOrDefault(static found => found != null);
    }

    // The type whose properties the serializer writes for an object. It is the type that the
    // call or the property names, or the element type of a sequence of that type. The type of
    // the object counts where that type says nothing, or where it lets a derived type write its
    // own properties. Null then.
    private static INamedTypeSymbol? WrittenTypeOf(ITypeSymbol? declared, Construction members)
    {
        while (declared != null && !members.Is(declared) && SodaFlowSymbols.ElementTypeOf(declared) is { } element)
        {
            declared = element;
        }

        return declared is INamedTypeSymbol named
               && !Construction.SaysNothing(named)
               && members.Is(named)
               && !SodaFlowSymbols.IsPolymorphic(named)
            ? named
            : null;
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
            SyntaxNode? outer = this.enumeratedAt;
            this.enumeratedAt = written;
            this.Run(state: state, enumerated: this.region.TypeOf(written));
            this.enumeratedAt = outer;
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
        List<(ITypeSymbol? Type, LoopState State)>? outerObjects = walker.ofTheQuery;
        SyntaxNode? at = this.enumeratedAt;
        walker.running = query;
        walker.ofTheQuery = at != null ? [] : null;
        walker.RunHere(query.Call);
        List<(ITypeSymbol? Type, LoopState State)>? objects = walker.ofTheQuery;
        walker.running = outer;
        walker.ofTheQuery = outerObjects;

        if (objects == null || at == null)
        {
            return;
        }

        // The objects of the code of the query go from the expression that enumerates it.
        foreach ((ITypeSymbol? type, LoopState state) in objects)
        {
            this.Sends(from: at, type: type, state: state);
        }
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

        if (this.RunsInTheCall(query: node, orHasCode: false))
        {
            this.Visit(node.Body);

            return;
        }

        // The clause methods of a type of this code have a body, and the walk reads them as calls. Such
        // a method can keep its function, thus the walk does not read the clauses here.
        if (this.RunsInTheCall(query: node, orHasCode: true))
        {
            this.Report(this.evaluator.FollowClauseMethods(node));
        }

        this.evaluator.Wait(node);
    }

    // True for a query expression whose each clause calls a method that cannot know the library and
    // that returns no sequence. Such a method runs its function in the call, as a method of
    // SodaFlow.Functional does for Maybe. With orHasCode, a method of this compilation with a body
    // also counts. A clause that the compiler does not bind waits.
    private bool RunsInTheCall(QueryExpressionSyntax query, bool orHasCode)
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
            || ((!this.region.Symbols.CanKnowTheLibrary(called.ContainingAssembly)
                 || (orHasCode && LoopStateEvaluator.HasCodeToRead(called)))
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

        this.evaluator.GivesTheReceiverOfABlockMember(definition: definition, followed: followed, receiver: receiver);

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

            // A callee that always gives the parameter its value replaces the element, and each other
            // location, when the call always runs. A second argument with ref or out can be the same
            // location, and the walk does not know which value goes there last.
            SyntaxNode? call =
                followed != null
                && followed.AlwaysGives.Contains(parameter)
                && arguments.Count(static argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)) == 1
                    ? arguments[i].Parent?.Parent
                    : null;

            (MadeObject Made, ImmutableArray<LoopState> All)? positions =
                target is ElementAccessExpressionSyntax indexed && call != null
                    ? this.PositionsBeforeAGive(collection: indexed.Expression, call: call)
                    : null;

            bool always = call != null && this.WhereACallRuns(call).InItsFunction;

            if (target is not ElementAccessExpressionSyntax || gives)
            {
                this.AssignState(target: target, state: state, replaces: always, turn: null);
            }

            // A method that gives the parameter a field of its object makes the local a name of that field.
            // The method gives it in each run, thus the call needs to run only. The walk can follow no such
            // method, because nothing from the loop goes in.
            if (arguments[i].RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
                && arguments[i].Parent?.Parent is InvocationExpressionSyntax site
                && arguments.Count(static argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)) == 1)
            {
                this.evaluator.NotesAFieldGivenOut(
                    target: arguments[i].Expression,
                    parameter: parameter,
                    method: definition,
                    receiver: receiver,
                    call: site,
                    replaces: this.WhereACallRuns(site).InItsFunction);
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
        this.Report(this.evaluator.FollowTheStaticConstructor(node));
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
        else if (SodaFlowSymbols.IsLazyValue(property))
        {
            this.ReadLazyValue(node);
        }
        else
        {
            this.Report(this.evaluator.Follow(node));
        }
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        this.Report(this.evaluator.FollowTheStaticConstructor(node));

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

    public override void VisitGenericName(GenericNameSyntax node)
    {
        base.VisitGenericName(node);
        this.Report(this.evaluator.FollowTheStaticConstructor(node));
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
            else if (property.Name == "Result")
            {
                this.Report(this.evaluator.FaultOfAResult(read: node, owner: owner, property: property));
            }
        }
        else if (this.region.Symbols.ReadsItsOwner(property))
        {
            this.CheckRead(node: node, owner: owner, name: property.Name);
        }
    }

    /// <summary>A member that reads the current value of its owner.</summary>
    private void CheckRead(SyntaxNode node, ExpressionSyntax owner, string name) =>
        this.CheckRead(
            node: node,
            state: this.evaluator.WithTheBuildOfAMember(heldIn: owner, state: this.evaluator.Eval(owner)),
            name: name,
            heldIn: owner);

    /// <summary>A member that reads the current value of an owner with the given state.</summary>
    private void CheckRead(SyntaxNode node, LoopState state, string name, ExpressionSyntax? heldIn = null)
    {
        // The first read of a cell runs the functions that calculate its value.
        if (!state.Computed.IsEmpty)
        {
            state =
                this.evaluator.AtTheFirstRead(
                    read: node,
                    state: state,
                    marks: this.MarksOfAFirstRead(node),
                    heldIn: heldIn,
                    ofACaller: this.sink);
        }

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

        // An object that a callee throws to this code comes from the call.
        foreach ((SyntaxNode call, ITypeSymbol? type, LoopState state) in followed.Thrown)
        {
            this.Sends(from: call, type: type, state: state);
        }
    }

    public override void VisitThrowStatement(ThrowStatementSyntax node)
    {
        base.VisitThrowStatement(node);
        this.Throws(throwing: node, value: node.Expression);
    }

    public override void VisitThrowExpression(ThrowExpressionSyntax node)
    {
        base.VisitThrowExpression(node);
        this.Throws(throwing: node, value: node.Expression);
    }

    // A throw with no value throws the object of its catch clause again.
    private void Throws(SyntaxNode throwing, ExpressionSyntax? value)
    {
        LoopState state =
            value != null
                ? this.evaluator.Eval(value)
                : throwing.FirstAncestorOrSelf<CatchClauseSyntax>() is { } clause
                  && this.caught.TryGetValue(key: clause, value: out LoopState? again)
                    ? again
                    : LoopState.None;

        if (!state.IsNone)
        {
            this.Sends(from: throwing, type: value != null ? this.region.TypeOf(value) : null, state: state);
        }
    }

    // A thrown object goes to each catch clause around the throw or the call whose type the object
    // can have. It stops at the first clause with no filter that gets each object of its type.
    // Otherwise, it leaves the function. A type that the walk does not know can be each type. A
    // lambda that the walk entered runs in the call that gets it, so the object goes on to that call.
    // A query runs its lambdas where something enumerates it, and not where its code is. An async
    // function puts the object into its task.
    private void Sends(SyntaxNode from, ITypeSymbol? type, LoopState state)
    {
        SyntaxNode child = from;
        SyntaxNode? parent = from.Parent;

        for (;
             parent is not (null or LocalFunctionStatementSyntax or MemberDeclarationSyntax)
             && (parent is not AnonymousFunctionExpressionSyntax lambda
                 || (this.running == null && this.entered.Contains(lambda) && !IsAsync(lambda))
                 || this.IsOfTheQueryThatRuns(lambda));
             child = parent, parent = parent.Parent)
        {
            if (this.LeavesTheQueryThatRuns(code: child, type: type, state: state))
            {
                return;
            }

            // The runtime ignores an exception in a filter, and the object goes nowhere.
            if (parent is CatchFilterClauseSyntax)
            {
                return;
            }

            if (parent is not TryStatementSyntax attempt)
            {
                continue;
            }

            bool replaced = child != attempt.Finally && ThrowsAnObjectOfItsOwn(attempt.Finally);

            if (attempt.Block != child)
            {
                if (replaced)
                {
                    return;
                }

                continue;
            }

            foreach (CatchClauseSyntax clause in attempt.Catches)
            {
                ITypeSymbol? takes =
                    clause.Declaration is { } declaration ? this.region.TypeOf(declaration.Type) : null;

                if (type != null && takes != null && !this.region.CanBe(from: type, to: takes))
                {
                    continue;
                }

                this.caught[clause] =
                    this.caught.TryGetValue(key: clause, value: out LoopState? before) ? before.Join(state) : state;

                if (clause.Filter == null
                    && (takes == null || (type != null && this.region.Converts(from: type, to: takes))))
                {
                    return;
                }
            }

            if (replaced)
            {
                return;
            }
        }

        if (parent != null && IsAsync(parent))
        {
            this.IntoTheTask(function: parent, type: type, state: state);

            return;
        }

        this.thrown.Add((type, state));
    }

    // The variable of a catch clause gets the object of each throw that goes to the clause. The
    // filter gets it too. The runtime ignores an exception in a filter, and the filter is then
    // false. Thus, no read in a filter throws out of the try, and the walk reports none there. A
    // value that the filter gives to a local stays in the local.
    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        if (node.Declaration is { } declaration
            && this.caught.TryGetValue(key: node, value: out LoopState? state)
            && this.region.ModelFor(declaration).GetDeclaredSymbol(declaration) is { } local)
        {
            this.evaluator.Declare(local: local, state: state);
        }

        if (node.Filter != null)
        {
            Action<Finding> reports = this.sink;
            this.sink = static _ => { };

            try
            {
                this.Visit(node.Filter);
            }
            finally
            {
                this.sink = reports;
            }
        }

        this.Visit(node.Block);
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
            && this.evaluator.DynamicMethodOf(
                    members: members,
                    name: access.Name.Identifier.ValueText,
                    arguments: arguments)
                is ({ } found, var inOrder))
        {
            this.Report(
                this.evaluator.FollowImplied(
                    call: node,
                    member: found,
                    owner: owner,
                    arguments: inOrder.Select(argument =>
                        argument == null
                            ? (node, LoopState.None)
                            : (argument.Expression, this.evaluator.Eval(argument.Expression)))));

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
        // The compiler does not keep the symbol of the name of a called method. Each time that the
        // walk asks for it, the compiler binds the full chain of the receiver again. The compiler
        // keeps the symbol of the call, and a dynamic call has none.
        if (this.IsTheNameOfACalledMethod(node)
            || this.region.SymbolOf(node) != null
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
    ///     expression in it. A local can hide it also: see
    ///     <see cref="HoldsACarrier" />.
    /// </summary>
    private bool IsCarrier(ExpressionSyntax argument) =>
        this.IsCarrier(
            argument: argument,
            seen: ImmutableHashSet<(ISymbol Name, LoopStateEvaluator At)>.Empty,
            at: this.evaluator);

    // The same, with the names that the check read before, each with the evaluator of its code. A
    // local can get its own value. The evaluator reads the code of the argument. A value of the
    // library counts only if the walk can give it a pending state. Thus, a plain cell in an array of
    // objects does not count.
    private bool IsCarrier(
        ExpressionSyntax argument,
        ImmutableHashSet<(ISymbol Name, LoopStateEvaluator At)> seen,
        LoopStateEvaluator at)
    {
        if (at.IsAsserted(argument))
        {
            return false;
        }

        ExpressionSyntax value = LoopStateEvaluator.Unwrapped(argument);
        ITypeSymbol? type = this.region.TypeOf(value);

        if (this.region.Symbols.IsCarrierType(type))
        {
            return at.CanBePending(value);
        }

        // The elements of an array or a collection that the code writes here have their own types.
        return value switch
        {
            CollectionExpressionSyntax collection => collection.Elements
                .OfType<ExpressionElementSyntax>()
                .Any(element => this.IsCarrier(argument: element.Expression, seen: seen, at: at)),
            ImplicitArrayCreationExpressionSyntax made =>
                made.Initializer.Expressions.Any(element => this.IsCarrier(argument: element, seen: seen, at: at)),
            ArrayCreationExpressionSyntax { Initializer: { } initializer } =>
                initializer.Expressions.Any(element => this.IsCarrier(argument: element, seen: seen, at: at)),
            _ when type is IArrayTypeSymbol array && this.region.Symbols.IsCarrierType(array.ElementType) =>
                at.CanBePending(value),
            // ToArray and ToList copy the elements of their receiver.
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToArray" or "ToList" } copied
            } copy when this.region.SymbolOf(copy) is IMethodSymbol method && CopiesItsReceiver(method) =>
                this.IsCarrier(argument: copied.Expression, seen: seen, at: at),
            IdentifierNameSyntax name => this.HoldsACarrier(use: name, seen: seen, at: at),
            _ => false
        };
    }

    /// <summary>
    ///     True when the code gives a local or a parameter a value of the
    ///     library that its type hides. The type <c>object</c> and an array of
    ///     objects hide it.
    /// </summary>
    /// <remarks>
    ///     The check reads each value that the block of the local gives it, in
    ///     the order of the code. These are the initializer, an assignment, an
    ///     assignment to an element, and a call of <c>Array.Fill</c>. For a
    ///     list, they are also an <c>Add</c>, an <c>Insert</c>, and an
    ///     <c>AddRange</c>. A statement of the block that runs before the use
    ///     replaces the values that it writes over. An assignment replaces each
    ///     value, and an assignment to an element at a constant index replaces
    ///     the value of that element. Each other value stays. A call that gives
    ///     the name to a method of the code adds the values that
    ///     <see cref="PutsOf" /> finds. A parameter starts with the arguments
    ///     that the call of its code gives it. The evaluator of the caller
    ///     reads them. Then, the body of the code is the block of the
    ///     parameter.
    /// </remarks>
    /// <param name="use">The name whose value the check reads.</param>
    /// <param name="seen">The names that the check read before.</param>
    /// <param name="at">The evaluator of the code of the name.</param>
    private bool HoldsACarrier(
        SyntaxNode use,
        ImmutableHashSet<(ISymbol Name, LoopStateEvaluator At)> seen,
        LoopStateEvaluator at)
    {
        if (this.region.SymbolOf(use) is not ({ } name and (ILocalSymbol or IParameterSymbol))
            || seen.Contains((name, at))
            || StartOf(name: name, at: at) is not (var scope, var initial))
        {
            return false;
        }

        ImmutableHashSet<(ISymbol Name, LoopStateEvaluator At)> read = seen.Add((name, at));

        // A local function can run before a statement that comes before it in the code.
        bool inOrder =
            !use.Ancestors().TakeWhile(node => node != scope).Any(static node => node is LocalFunctionStatementSyntax);

        Dictionary<int, (ExpressionSyntax Value, LoopStateEvaluator At)> elements = [];
        List<(ExpressionSyntax Value, LoopStateEvaluator At)> others = [];

        // The names of the local that a put writes through. Each other use of the local can move
        // its elements, as Array.Reverse does, or copy them. After such a use, a put at an index
        // replaces nothing.
        HashSet<SyntaxNode> targets = [];
        bool moved = false;

        if (initial.Count == 1)
        {
            Replace(initial[0]);
        }
        else
        {
            others.AddRange(initial);
        }

        foreach (SyntaxNode node in scope.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment when IsTheName(assignment.Left):
                    targets.Add(LoopStateEvaluator.Unwrapped(assignment.Left));

                    if (RunsBeforeTheUse(assignment))
                    {
                        Replace((assignment.Right, at));
                        moved = false;
                    }
                    else
                    {
                        others.Add((assignment.Right, at));
                    }

                    break;
                case AssignmentExpressionSyntax assignment
                    when LoopStateEvaluator.Unwrapped(assignment.Left) is ElementAccessExpressionSyntax element
                         && IsTheName(element.Expression):
                    targets.Add(LoopStateEvaluator.Unwrapped(element.Expression));

                    if (!moved && RunsBeforeTheUse(assignment) && this.ConstantIndexOf(element) is { } index)
                    {
                        elements[index] = (assignment.Right, at);
                    }
                    else
                    {
                        others.Add((assignment.Right, at));
                    }

                    break;
                // Only the value of Array.Fill can be a value of the library.
                case InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Fill" },
                        ArgumentList.Arguments: var arguments
                    } call
                    when arguments.Any(argument => IsTheName(argument.Expression))
                         && this.region.SymbolOf(call) is IMethodSymbol fill
                         && LoopStateEvaluator.IsArrayFill(fill):
                    foreach (ArgumentSyntax argument in arguments)
                    {
                        if (IsTheName(argument.Expression))
                        {
                            targets.Add(LoopStateEvaluator.Unwrapped(argument.Expression));
                        }
                        else
                        {
                            others.Add((argument.Expression, at));
                        }
                    }

                    break;
                // Insert moves the elements after its index. Thus, its name of the local counts as a use.
                case InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax
                        {
                            Name.Identifier.ValueText: "Add" or "Insert" or "AddRange", Expression: var list
                        },
                        ArgumentList.Arguments: var added
                    } call
                    when IsTheName(list)
                         && this.region.SymbolOf(call) is IMethodSymbol method
                         && PutsIntoAList(method):
                    if (method.Name != "Insert")
                    {
                        targets.Add(LoopStateEvaluator.Unwrapped(list));
                    }

                    others.AddRange(added.Select(argument => (argument.Expression, at)));

                    break;
                // A method of the code that gets the name can put the value of a parameter into an
                // element. The call is also a use that can move the elements.
                case InvocationExpressionSyntax call
                    when GivesTheName(call) && this.region.SymbolOf(call) is IMethodSymbol method:
                    others.AddRange(
                        this.PutsOf(call: call, method: method, isTheName: IsTheName).Select(value => (value, at)));

                    break;
                case IdentifierNameSyntax other when !targets.Contains(other) && IsTheName(other):
                    moved = true;

                    break;
            }
        }

        return elements.Values
            .Concat(others)
            .Any(value => this.IsCarrier(argument: value.Value, seen: read, at: value.At));

        // An array that the code writes gives each element by its index.
        void Replace((ExpressionSyntax Value, LoopStateEvaluator At) value)
        {
            elements.Clear();
            others.Clear();

            if (LoopStateEvaluator.ElementsWritten(value.Value) is { } each)
            {
                for (int index = 0; index < each.Length; index++)
                {
                    elements[index] = (each[index], value.At);
                }
            }
            else
            {
                others.Add(value);
            }
        }

        // A call can give the name as an argument, or as the receiver of an extension method.
        bool GivesTheName(InvocationExpressionSyntax call) =>
            call.ArgumentList.Arguments.Any(argument => IsTheName(argument.Expression))
            || (call.Expression is MemberAccessExpressionSyntax { Expression: var receiver } && IsTheName(receiver));

        // A plain assignment in a statement of the block runs before the use each time, if it
        // comes first in the code.
        bool RunsBeforeTheUse(SyntaxNode assignment) =>
            inOrder
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Parent is ExpressionStatementSyntax { Parent: var block }
            && block == scope
            && assignment.Span.End <= use.SpanStart;

        bool IsTheName(ExpressionSyntax written) =>
            LoopStateEvaluator.Unwrapped(written) is IdentifierNameSyntax other
            && other.Identifier.ValueText == name.Name
            && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(other), y: name);
    }

    // The values that a method of the code puts into an element of a parameter that gets the name
    // at the call. A value counts only if it is a parameter of the method. Then, it is each
    // argument that the call gives that parameter. The check does not follow a call in the method.
    private IEnumerable<ExpressionSyntax> PutsOf(
        SyntaxNode call,
        IMethodSymbol method,
        Func<ExpressionSyntax, bool> isTheName)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (definition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } code)
        {
            return [];
        }

        IParameterSymbol[] holders =
        [
            .. definition.Parameters.Where(parameter =>
                LoopStateEvaluator.ArgumentsFor(method: method, site: call, parameter: parameter).Any(isTheName))
        ];

        return code.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                LoopStateEvaluator.Unwrapped(assignment.Left) is ElementAccessExpressionSyntax element
                && IsAHolder(element.Expression))
            .SelectMany(assignment =>
                LoopStateEvaluator.Unwrapped(assignment.Right) is IdentifierNameSyntax value
                && this.region.SymbolOf(value) is IParameterSymbol parameter
                && SymbolEqualityComparer.Default.Equals(x: parameter.ContainingSymbol, y: definition)
                    ? LoopStateEvaluator.ArgumentsFor(method: method, site: call, parameter: parameter)
                    : []);

        bool IsAHolder(ExpressionSyntax target) =>
            LoopStateEvaluator.Unwrapped(target) is IdentifierNameSyntax name
            && holders.Any(holder => holder.Name == name.Identifier.ValueText)
            && this.region.SymbolOf(name) is IParameterSymbol found
            && holders.Contains(value: found, comparer: SymbolEqualityComparer.Default);
    }

    // The code that can put into a local or a parameter, with the values that the name starts
    // with. Null for a parameter that no call of this evaluator gives an argument.
    private static (SyntaxNode Scope, IReadOnlyList<(ExpressionSyntax Value, LoopStateEvaluator At)> Initial)?
        StartOf(ISymbol name, LoopStateEvaluator at) =>
        name switch
        {
            ILocalSymbol local
                when local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                         is VariableDeclaratorSyntax declarator
                     && declarator.FirstAncestorOrSelf<BlockSyntax>() is { } block =>
                (block, declarator.Initializer?.Value is { } initial ? [(initial, at)] : []),
            IParameterSymbol parameter
                when at.ArgumentsInTheCaller(parameter) is (var caller, var arguments)
                     && parameter.ContainingSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                         is { } code =>
                (BodyOf(code), [.. arguments.Select(argument => (argument, caller))]),
            _ => null
        };

    // The body of a method, of a constructor, or of a local function. The declaration of a primary
    // constructor is its type, whose initializers read the parameters.
    private static SyntaxNode BodyOf(SyntaxNode code) =>
        code switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody ?? code,
            LocalFunctionStatementSyntax function => (SyntaxNode?)function.Body ?? function.ExpressionBody ?? code,
            _ => code
        };

    // List<T>.ToArray, and Enumerable.ToArray and ToList.
    private static bool CopiesItsReceiver(IMethodSymbol method) =>
        method.Name is "ToArray" or "ToList"
        && (method.ReducedFrom ?? method).ContainingType.OriginalDefinition.ToDisplayString()
            is "System.Collections.Generic.List<T>" or "System.Linq.Enumerable";

    // Add, Insert, and AddRange of List<T>.
    internal static bool PutsIntoAList(IMethodSymbol method) =>
        method is { Name: "Add" or "Insert" or "AddRange" }
        && method.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>";

    private int? ConstantIndexOf(ElementAccessExpressionSyntax element) =>
        element.ArgumentList.Arguments is { Count: 1 } arguments
        && this.region.ModelFor(element).GetConstantValue(arguments[0].Expression) is { Value: int index }
            ? index
            : null;

    // Two reads of a serializer are of the same object when they have one record of its members and
    // one type.
    private sealed class SerializedObjects : IEqualityComparer<(Construction Members, INamedTypeSymbol? Own)>
    {
        public static readonly SerializedObjects Instance = new();

        public bool Equals(
            (Construction Members, INamedTypeSymbol? Own) x,
            (Construction Members, INamedTypeSymbol? Own) y) =>
            ReferenceEquals(objA: x.Members, objB: y.Members)
            && SymbolEqualityComparer.Default.Equals(x: x.Own, y: y.Own);

        public int GetHashCode((Construction Members, INamedTypeSymbol? Own) read) =>
            (read.Members.GetHashCode() * 397)
            ^ (read.Own == null ? 0 : SymbolEqualityComparer.Default.GetHashCode(read.Own));
    }
}
