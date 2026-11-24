using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The code that runs while one loop is open, which is the body of a loop
///     block.
/// </summary>
internal sealed class LoopRegion(
    in Compilation compilation,
    in SodaFlowSymbols symbols,
    in ISymbol placeholder,
    in LoopState placeholderState,
    in SyntaxNode root,
    in Action<Diagnostic> report,
    in CancellationToken cancellation,
    in LoopRegion? readBefore)
{
    // The walk reads at most this many callee bodies for one block. A block that needs more is
    // code that calls itself through many paths, and the limit is what ends the work.
    private const int MaxFrames = 512;

    private readonly CancellationToken cancellation = cancellation;
    private readonly Compilation compilation = compilation;

    // The fields that the code sets while the loop is open, where the walk does not know the
    // object. Such a field is a static field, or a field of an object that the block did not
    // make.
    private readonly Dictionary<ISymbol, LoopState> fields = new(SymbolEqualityComparer.Default);

    // For each key of the fields: the number of the writes to a member at the last state that it got.
    private readonly Dictionary<ISymbol, int> lastWrites = new(SymbolEqualityComparer.Default);

    // For each member: each write through a name that gave the field its state. The record has the
    // name, the objects of the store that the owner can be, and the number of the write.
    private readonly Dictionary<ISymbol, List<NamedWrite>> writesThroughNames =
        new(SymbolEqualityComparer.Default);

    // Each two names that an assignment made two names of one value, from Links.
    private readonly List<NameLink> links = [];

    // For a field that holds an array: each put into an element through a local that got the array,
    // with the constant index or -1. See PutsThroughLocalsOf.
    private readonly Dictionary<ISymbol, List<(int Index, LoopState Held)>> putsThroughLocals =
        new(SymbolEqualityComparer.Default);

    // True when a key of the fields is a property of an interface: see FieldState.
    private bool hasInterfaceFields;

    // The results of calls whose callee the walk read, and which no creation gave.
    private readonly HashSet<MadeObject> olderObjects = [];
    private readonly Dictionary<SyntaxTree, SemanticModel> models = [];
    private readonly Dictionary<ISymbol, ExpressionSyntax?> initializerOf = new(SymbolEqualityComparer.Default);

    private readonly Dictionary<ISymbol, ImmutableArray<ISymbol>> namedByTheInitializer =
        new(SymbolEqualityComparer.Default);

    private readonly Action<Diagnostic> report = report;

    // A read of the block after the first one reports only what the reads before did not.
    private readonly HashSet<(string Id, Location Location)> reported = readBefore?.reported ?? [];

    // The reads of a run in the branch of a condition whose value the walk does not know. Each read of
    // the block adds to them. The last read reports each one whose location has no other report: see
    // ReportTheUndecidedReads.
    private readonly Dictionary<Location, Diagnostic> undecided = readBefore?.undecided ?? [];

    // The walk and the evaluator get the symbol and the type of one node many times. The compiler
    // computes them again at each call, thus the region keeps them.
    private readonly Dictionary<ExpressionSyntax, Conversion> conversionOf = [];
    private readonly Dictionary<SyntaxNode, ISymbol?> symbolOf = [];
    private readonly Dictionary<ExpressionSyntax, ITypeSymbol?> typeOf = [];

    // The compiler makes a new array of the declarations of a local each time. The walk takes a loop
    // shape before and after each function that runs for each element, and each shape tests each local.
    private readonly Dictionary<ISymbol, ImmutableArray<SyntaxReference>> declarationsOf =
        new(SymbolEqualityComparer.Default);

    private int frames;
    private int objectFrames;

    // The members of the object of a forward reference that the code samples at the end of the
    // transaction, before a switch links to them.
    private readonly HashSet<ISymbol> forced = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> startedTypes = new(SymbolEqualityComparer.Default);

    // The types whose static constructor the walk reads now. A use of such a type in its own static constructor
    // runs it no second time.
    private readonly HashSet<INamedTypeSymbol> startingTypes = new(SymbolEqualityComparer.Default);

    // True for a type declaration that has a static initializer. Each name in its code asks.
    private readonly Dictionary<TypeDeclarationSyntax, bool> hasStaticInitializers = [];

    // The lambdas and the local functions that the walk reads for a call now.
    private readonly HashSet<SyntaxNode> activeFunctions = [];

    // The callee bodies that the walk reads now, from the first call. A body that the walk reads
    // in a read of the same body has the shape of its call also.
    private readonly List<(SyntaxNode Body, int? Shape)> walks = [];

    // The key is a lambda or a local function that the walk read for a call. The value has the
    // locals and the parameters that a function in it reads or writes.
    private readonly Dictionary<SyntaxNode, ImmutableHashSet<ISymbol>?> captured = [];

    /// <summary>The one state of each object that the code of the region makes.</summary>
    public ObjectStore Objects { get; } = new();

    /// <summary>The content that the sends of the read before gave to the sinks.</summary>
    public SentContent SentBefore { get; } = readBefore?.Sent ?? new SentContent();

    /// <summary>The content that the sends of this read give to the sinks.</summary>
    public SentContent Sent { get; } = new();

    /// <summary>
    ///     The field behind each property that the walk asked about, or null
    ///     for a property with no such field. The evaluator reads the
    ///     declaration of a property one time.
    /// </summary>
    public Dictionary<ISymbol, IFieldSymbol?> FieldsBehind { get; } = new(SymbolEqualityComparer.Default);

    // The functions that run after the transaction of the block, while the walk reads them.
    private readonly HashSet<SyntaxNode> readAfterTheTransaction = [];

    /// <summary>
    ///     True while the walk reads a function that runs after the transaction
    ///     of the block. A send in that function gives content to the sink after
    ///     the close.
    /// </summary>
    public bool ReadsCodeAfterTheTransaction => this.readAfterTheTransaction.Count > 0;

    /// <summary>
    ///     True after the walk read a continuation whose task keeps its
    ///     findings. Only then can a wait of a task report findings.
    /// </summary>
    public bool KeepsTheReadsOfAContinuation { get; set; }

    /// <summary>Reads a function that runs after the transaction of the block.</summary>
    /// <remarks>
    ///     A posted function can post a function also. The walk does not read a
    ///     function again while it reads that function. Thus, a function that
    ///     posts itself does not make the walk continue without end.
    /// </remarks>
    /// <param name="function">The declaration of the function.</param>
    /// <param name="read">The read of the function.</param>
    public void ReadAfterTheTransaction(SyntaxNode function, Action read)
    {
        if (!this.readAfterTheTransaction.Add(function))
        {
            return;
        }

        try
        {
            read();
        }
        finally
        {
            this.readAfterTheTransaction.Remove(function);
        }
    }

    /// <summary>The body of the block.</summary>
    public SyntaxNode Root { get; } = root;

    /// <summary>The assemblies of the library, as this compilation sees them.</summary>
    public SodaFlowSymbols Symbols { get; } = symbols;

    public ISymbol Placeholder { get; } = placeholder;

    public LoopState PlaceholderState { get; } = placeholderState;

    /// <summary>
    ///     True for the block of a forward reference, whose placeholder holds the
    ///     object that the block returns.
    /// </summary>
    public bool ReadsBack => !this.PlaceholderState.Content.ReadBack.IsEmpty;

    /// <summary>
    ///     Records the members of the object of a forward reference that the code
    ///     samples at the end of the transaction.
    /// </summary>
    public void Forces(ImmutableArray<ISymbol> members) => this.forced.UnionWith(members);

    public string PlaceholderName => this.Placeholder.Name;

    /// <summary>The assembly that the block is in.</summary>
    public IAssemblySymbol Assembly => this.compilation.Assembly;

    /// <summary>
    ///     The type of the platform with the metadata name. Null when the
    ///     compilation has no such type.
    /// </summary>
    /// <param name="name">The metadata name, such as <c>System.Exception</c>.</param>
    public INamedTypeSymbol? TypeOfThePlatform(string name) => this.compilation.GetTypeByMetadataName(name);

    /// <summary>True when the code set a field to a value with a state.</summary>
    public bool HasFields => this.fields.Count > 0;

    /// <summary>
    ///     Records that the walk reads the static constructor of a type. False
    ///     when a use that is sure to run read it before, or when the walk reads
    ///     it now. Call <see cref="EndsTheStaticConstructorOf" /> after the read.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="sure">
    ///     True when the use always runs. Only such a use runs the constructor
    ///     for each use after it.
    /// </param>
    public bool StartsTheType(INamedTypeSymbol type, bool sure)
    {
        if (this.startedTypes.Contains(type) || !this.startingTypes.Add(type))
        {
            return false;
        }

        if (sure)
        {
            this.startedTypes.Add(type);
        }

        return true;
    }

    /// <summary>Records the end of the read of the static constructor of a type.</summary>
    public void EndsTheStaticConstructorOf(INamedTypeSymbol type) => this.startingTypes.Remove(type);

    /// <summary>
    ///     True when a type declaration has an initializer of a static field or
    ///     of a static property. Each part of a partial type counts.
    /// </summary>
    public bool HasStaticInitializers(TypeDeclarationSyntax type)
    {
        if (!this.hasStaticInitializers.TryGetValue(key: type, value: out bool has))
        {
            // A read in one part of a type runs the initializers of the type. Only a partial type
            // needs the compiler to find its other parts.
            has = type.Modifiers.Any(SyntaxKind.PartialKeyword)
                  && this.ModelFor(type).GetDeclaredSymbol(type) is { } declared
                ? declared.DeclaringSyntaxReferences.Any(static reference =>
                    reference.GetSyntax() is TypeDeclarationSyntax part && DeclaresStaticInitializers(part))
                : DeclaresStaticInitializers(type);

            this.hasStaticInitializers[type] = has;
        }

        return has;
    }

    private static bool DeclaresStaticInitializers(TypeDeclarationSyntax type) =>
        type.Members.Any(static member =>
            member.Modifiers.Any(SyntaxKind.StaticKeyword)
            && member switch
            {
                FieldDeclarationSyntax field => field.Declaration.Variables.Any(static one => one.Initializer != null),
                PropertyDeclarationSyntax property => property.Initializer != null,
                _ => false
            });

    /// <summary>
    ///     Keeps the objects as results of calls that a second call can give
    ///     also. See <see cref="IsAnOlderObject" />.
    /// </summary>
    public void NotesOlderObjects(ImmutableArray<MadeObject> objects) => this.olderObjects.UnionWith(objects);

    /// <summary>
    ///     True for the result of a call whose callee the walk read and which no
    ///     creation in that callee gave.
    /// </summary>
    public bool IsAnOlderObject(MadeObject one) => this.olderObjects.Contains(one);

    /// <summary>A number for what the fields say, from <see cref="LoopState.Shape" />.</summary>
    public int ShapeOfFields =>
        this.fields.Aggregate(
            seed: 0,
            func: static (sum, one) => unchecked(
                sum + ShapeNumber.Of(location: ShapeNumber.Of(one.Key.Name), state: one.Value.Shape)));

    /// <summary>
    ///     The state of a field that the code set while the loop is open. The
    ///     field is the origin of that state, in each frame that reads it.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="canHave">
    ///     False for a key that the object of the read cannot have. See
    ///     <see cref="Construction.StateWithTheAliases" /> and
    ///     <see cref="Construction.StateImplementedTogether" />.
    /// </param>
    public LoopState FieldState(ISymbol field, Func<ISymbol, bool>? canHave = null)
    {
        LoopState own = this.OwnFieldState(field: field, canHave: canHave);
        ImmutableArray<ISymbol> named = this.FieldsNamedByTheInitializerOf(field);

        return named.IsEmpty ? own : this.WithTheFieldsNamed(state: own, named: named, canHave: canHave);
    }

    // Most fields have no named field. Thus, only this method makes the function that it gives Aggregate.
    private LoopState WithTheFieldsNamed(
        LoopState state,
        ImmutableArray<ISymbol> named,
        Func<ISymbol, bool>? canHave) =>
        named.Aggregate(seed: state, func: (all, one) => all.Join(this.OwnFieldState(field: one, canHave: canHave)));

    private LoopState OwnFieldState(ISymbol field, Func<ISymbol, bool>? canHave) =>
        Construction.StateWithTheAliases(
                states: this.fields,
                key: field.OriginalDefinition,
                stateOf: static (key, state) => state.From(key),
                canHave: canHave)
            .Join(
                Construction.StateImplementedTogether(
                    states: this.fields,
                    key: field.OriginalDefinition,
                    stateOf: static (key, state) => state.From(key),
                    implementations: this.Symbols.Implementations,
                    hasInterfaceMembers: this.hasInterfaceFields,
                    canHave: canHave));

    /// <summary>
    ///     Records a value that the code gives to a field. The walk does not know
    ///     which object has the field, thus each object of the type has that state
    ///     to the end of the block.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">
    ///     True for a field with one location, such as a static field, and a
    ///     write that always runs. The value then replaces the value before it.
    /// </param>
    public void SetField(ISymbol field, LoopState state, bool replaces)
    {
        ISymbol key = field.OriginalDefinition;

        // A state that only holds objects of the store says what the owner of the field holds. A map of a
        // collection with a handler stays, because a Dispose through the field runs that handler.
        if (state is { IsNone: true, HoldsAnObject: false }
            && !state.Objects.Any(made => this.Objects.HandlersOfAMap(made).Count > 0))
        {
            if (replaces)
            {
                this.fields.Remove(key);
            }

            return;
        }

        this.fields[key] =
            !replaces && this.fields.TryGetValue(key: key, value: out LoopState? existing)
                ? existing.Join(state)
                : state;

        this.lastWrites[key] = this.MemberWrites;

        // A second field that holds the same array gets each put into it also. A read of this field reads that
        // field too, thus the two directions are the same.
        foreach (ISymbol named in this.FieldsNamedByTheInitializerOf(key))
        {
            this.fields[named] =
                this.fields.TryGetValue(key: named, value: out LoopState? before) ? before.Join(state) : state;

            this.lastWrites[named] = this.MemberWrites;
        }

        this.hasInterfaceFields |= key is IPropertySymbol { ContainingType.TypeKind: TypeKind.Interface };
    }

    /// <summary>
    ///     The static fields with readonly that hold the same object as a static
    ///     field with readonly, from its initializer. That is the field that the
    ///     initializer names, the field that the initializer of that field names,
    ///     and so on. Empty for each other field.
    /// </summary>
    /// <remarks>
    ///     No code can give such a field a different value after its
    ///     initializer. Thus, the two fields hold one object at each read.
    /// </remarks>
    private ImmutableArray<ISymbol> FieldsNamedByTheInitializerOf(ISymbol field)
    {
        if (field.OriginalDefinition is not IFieldSymbol { IsStatic: true, IsReadOnly: true } original)
        {
            return ImmutableArray<ISymbol>.Empty;
        }

        field = original;

        if (this.namedByTheInitializer.TryGetValue(key: field, value: out ImmutableArray<ISymbol> known))
        {
            return known;
        }

        List<ISymbol> named = [];

        for (ISymbol at = field; named.Count < 8;)
        {
            if (this.InitializerOf(at) is not ((IdentifierNameSyntax or MemberAccessExpressionSyntax) and var value)
                || this.ModelFor(value).GetSymbolInfo(value).Symbol?.OriginalDefinition
                    is not IFieldSymbol { IsStatic: true, IsReadOnly: true } next
                || SymbolEqualityComparer.Default.Equals(x: next, y: field)
                || named.Contains(value: next, comparer: SymbolEqualityComparer.Default))
            {
                break;
            }

            named.Add(next);
            at = next;
        }

        ImmutableArray<ISymbol> all = named.ToImmutableArray();
        this.namedByTheInitializer[field] = all;

        return all;
    }

    private SemanticModel ModelFor(SyntaxTree tree)
    {
        if (!this.models.TryGetValue(key: tree, value: out SemanticModel? model))
        {
            model = this.compilation.GetSemanticModel(tree);
            this.models[tree] = model;
        }

        return model;
    }

    public SemanticModel ModelFor(SyntaxNode node) => this.ModelFor(node.SyntaxTree);

    /// <summary>
    ///     The initializer of a field or of a property, when no code can give the
    ///     member a different value after it. Null for each other member.
    /// </summary>
    /// <remarks>
    ///     The member is a field with readonly, or a property with only a getter
    ///     that has no body. Only a constructor of the type can give such a
    ///     member a value. Thus, a constructor that has the name of the member in
    ///     its code can give it a different value.
    /// </remarks>
    public ExpressionSyntax? InitializerOf(ISymbol member)
    {
        if (!this.initializerOf.TryGetValue(key: member, value: out ExpressionSyntax? found))
        {
            found = FindTheInitializerOf(member);
            this.initializerOf[member] = found;
        }

        return found;
    }

    private static ExpressionSyntax? FindTheInitializerOf(ISymbol member)
    {
        ExpressionSyntax? value =
            member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
            {
                VariableDeclaratorSyntax { Initializer.Value: { } ofAField } => ofAField,
                PropertyDeclarationSyntax { Initializer.Value: { } ofAProperty, AccessorList: { } accessors }
                    when accessors.Accessors.All(static accessor =>
                        accessor is { Body: null, ExpressionBody: null }) => ofAProperty,
                _ => null
            };

        if (value == null)
        {
            return null;
        }

        bool aConstructorNamesIt =
            member.ContainingType.DeclaringSyntaxReferences
                .SelectMany(static declared => declared.GetSyntax().ChildNodes())
                .OfType<ConstructorDeclarationSyntax>()
                .Where(constructor => constructor.Modifiers.Any(SyntaxKind.StaticKeyword) == member.IsStatic)
                .Any(constructor => constructor.DescendantTokens()
                    .Any(token => token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == member.Name));

        return aConstructorNamesIt ? null : value;
    }

    /// <summary>The symbol that a node names, or null when it names none.</summary>
    public ISymbol? SymbolOf(SyntaxNode node)
    {
        if (!this.symbolOf.TryGetValue(key: node, value: out ISymbol? symbol))
        {
            symbol = this.ModelFor(node).GetSymbolInfo(node).Symbol;
            this.symbolOf[node] = symbol;
        }

        return symbol;
    }

    /// <summary>
    ///     True for a local that the given code declares. The region keeps the
    ///     declarations of each local.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="code">The body of a loop, or a function.</param>
    public bool IsDeclaredIn(ISymbol local, SyntaxNode code)
    {
        if (!this.declarationsOf.TryGetValue(key: local, value: out ImmutableArray<SyntaxReference> declarations))
        {
            declarations = local.DeclaringSyntaxReferences;
            this.declarationsOf[local] = declarations;
        }

        return LoopStateEvaluator.IsDeclaredIn(declarations: declarations, code: code);
    }

    /// <summary>
    ///     The operator that makes the conversion which the compiler adds to an
    ///     expression. Null when it adds none, or one that no operator makes.
    /// </summary>
    public IMethodSymbol? ConversionOf(ExpressionSyntax expression) =>
        this.ConversionAt(expression) is { IsUserDefined: true, MethodSymbol: { } method } ? method : null;

    /// <summary>
    ///     True when the compiler converts the value of the expression, a tuple
    ///     that is not a tuple literal, to a different tuple type. Each element
    ///     then gets a conversion that no node shows. A nullable conversion can
    ///     contain such a conversion, and the caller looks at the types.
    /// </summary>
    public bool ConvertsATuple(ExpressionSyntax expression) =>
        this.ConversionAt(expression) is { IsTupleConversion: true } or { IsNullable: true };

    /// <summary>
    ///     True when the compiler converts the value of the expression, of the
    ///     type dynamic, with a conversion that the binder finds at run time.
    /// </summary>
    public bool ConvertsADynamic(ExpressionSyntax expression) => this.ConversionAt(expression).IsDynamic;

    private Conversion ConversionAt(ExpressionSyntax expression)
    {
        if (!this.conversionOf.TryGetValue(key: expression, value: out Conversion conversion))
        {
            conversion = this.ModelFor(expression).GetConversion(expression);
            this.conversionOf[expression] = conversion;
        }

        return conversion;
    }

    /// <summary>
    ///     The operator of the code that converts a value of the type to bool
    ///     with no cast. Null when no such operator makes that conversion.
    /// </summary>
    public IMethodSymbol? ConversionToBoolean(ITypeSymbol from) =>
        this.compilation.ClassifyConversion(
                source: from,
                destination: this.compilation.GetSpecialType(SpecialType.System_Boolean))
            is { IsImplicit: true, IsUserDefined: true, MethodSymbol: { } method }
            ? method
            : null;

    /// <summary>
    ///     The operator of the code that converts a value of the first type to
    ///     the second one. Null when no such operator makes that conversion.
    /// </summary>
    public IMethodSymbol? ConversionOperator(ITypeSymbol from, ITypeSymbol to) =>
        this.compilation.ClassifyConversion(source: from, destination: to).MethodSymbol;

    /// <summary>
    ///     True when a value of the first type can be a value of the second
    ///     type, also through a cast. No operator of the code converts it.
    /// </summary>
    public bool CanBe(ITypeSymbol from, ITypeSymbol to) =>
        this.compilation.ClassifyConversion(source: from, destination: to) is { Exists: true, IsUserDefined: false };

    /// <summary>
    ///     True when the compiler converts the first type to the second one with
    ///     no cast. No operator converts an interface. Thus, each value of a first
    ///     type that is an interface is then a value of the second type.
    /// </summary>
    public bool Converts(ITypeSymbol from, ITypeSymbol to) =>
        this.compilation.ClassifyConversion(source: from, destination: to).IsImplicit;

    /// <summary>
    ///     The type of an expression. A collection expression has no type of its
    ///     own, and has the type that the compiler converts it to. A conditional
    ///     or a switch expression whose results are such expressions is the same.
    /// </summary>
    public ITypeSymbol? TypeOf(ExpressionSyntax expression)
    {
        if (!this.typeOf.TryGetValue(key: expression, value: out ITypeSymbol? type))
        {
            TypeInfo info = this.ModelFor(expression).GetTypeInfo(expression);

            type =
                info.Type == null
                && expression
                    is CollectionExpressionSyntax
                    or ConditionalExpressionSyntax
                    or SwitchExpressionSyntax
                    or ParenthesizedExpressionSyntax
                    ? info.ConvertedType
                    : info.Type;

            this.typeOf[expression] = type;
        }

        return type;
    }

    /// <summary>
    ///     Reports a diagnostic one time. The walk can read the same lambda or
    ///     local function from more than one call.
    /// </summary>
    private void Report(Diagnostic diagnostic)
    {
        if (this.reported.Add((diagnostic.Id, diagnostic.Location)))
        {
            this.report(diagnostic);
        }
    }

    // A read of such a run becomes an SF1007 at the location of its report.
    // A value that goes to unseen code from such a run gets no report.
    private void Report(Finding finding)
    {
        if (!finding.Undecided)
        {
            this.Report(finding.Diagnostic);

            return;
        }

        if (!finding.Escape)
        {
            Location location = finding.Diagnostic.Location;

            this.undecided[location] =
                Diagnostic.Create(
                    descriptor: Diagnostics.UndecidedRun,
                    location: location,
                    location.SourceTree?.GetText().ToString(location.SourceSpan) ?? string.Empty,
                    this.PlaceholderName);
        }
    }

    // A report at the location of the same read wins. The walk can find a warning there in a read of
    // the block after this one, thus only the last read reports.
    private void ReportTheUndecidedReads()
    {
        foreach (Diagnostic diagnostic in this.undecided.Values
                     .Where(diagnostic => this.reported.All(reported => reported.Location != diagnostic.Location))
                     .ToList())
        {
            this.Report(diagnostic);
        }
    }

    /// <summary>
    ///     True while the walk tests a run of the timers or a Dispose in a
    ///     callee. The test then counts each condition whose value the walk does
    ///     not know as true.
    /// </summary>
    public bool CountsAnUndecidedCondition { get; set; }

    /// <summary>
    ///     Counts one more callee body that the walk reads. False when the block
    ///     is at its limit, and the callee is then code that the analyzer does not
    ///     read.
    /// </summary>
    public bool TryEnter() => ++this.frames <= MaxFrames;

    /// <summary>
    ///     Ends the analysis of the block when the compiler canceled it. An editor
    ///     cancels an analysis at each change to the file. The compiler stops
    ///     between two blocks, and only this call stops the walk of one block.
    /// </summary>
    public void StopWhenCanceled() => this.cancellation.ThrowIfCancellationRequested();

    /// <summary>
    ///     The number of writes to a member of an object that the walk read in
    ///     this region. A member can have a new value after each of them.
    /// </summary>
    public int MemberWrites { get; private set; }

    /// <summary>Records a write to a member of an object.</summary>
    public void AddAMemberWrite() => this.MemberWrites++;

    /// <summary>
    ///     Records a write to a member through a name, when the write gave the
    ///     field its state now. See <see cref="WritesThroughNamesOf" />.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="name">
    ///     The name of the owner, or null when the walk cannot read it again.
    ///     Such a write can be through each name.
    /// </param>
    /// <param name="objects">The objects of the store that the owner can be.</param>
    public void NotesAWriteThroughAName(ISymbol member, NameKey? name, ImmutableArray<MadeObject> objects)
    {
        ISymbol key = member.OriginalDefinition;

        if (this.LastWriteOf(key) != this.MemberWrites)
        {
            return;
        }

        if (!this.writesThroughNames.TryGetValue(key: key, value: out List<NamedWrite>? writes))
        {
            writes = [];
            this.writesThroughNames[key] = writes;
        }

        writes.Add(new NamedWrite(name: name, objects: objects, at: this.MemberWrites));
    }

    /// <summary>
    ///     Each write to a member through a name that went to the field, from
    ///     <see cref="NotesAWriteThroughAName" />.
    /// </summary>
    public IReadOnlyList<NamedWrite> WritesThroughNamesOf(ISymbol member) =>
        this.writesThroughNames.TryGetValue(key: member.OriginalDefinition, value: out List<NamedWrite>? writes)
            ? writes
            : [];

    /// <summary>Records that an assignment made two names of one value.</summary>
    public void Links(NameLink link) => this.links.Add(link);

    /// <summary>
    ///     Takes away each link of a name that gets a different value. An element
    ///     or a field of a name that gets a different value is in a different
    ///     object then.
    /// </summary>
    public void TakesAwayTheLinksOf(NameKey name)
    {
        if (this.links.Count > 0)
        {
            this.links.RemoveAll(link => link.One.IsIn(name) || link.Other.IsIn(name));
        }
    }

    /// <summary>Each link from <see cref="Links" /> that the record has now.</summary>
    public IReadOnlyList<NameLink> AllLinks => this.links;

    /// <summary>
    ///     Records a put into an element of the array of a field, through a local
    ///     that got the array.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="index">The constant index, or -1.</param>
    /// <param name="held">The state of the array with the value in it.</param>
    public void AddAPutThroughALocal(ISymbol field, int index, LoopState held)
    {
        ISymbol key = field.OriginalDefinition;

        if (!this.putsThroughLocals.TryGetValue(key: key, value: out List<(int Index, LoopState Held)>? puts))
        {
            puts = [];
            this.putsThroughLocals[key] = puts;
        }

        puts.Add((index, held));
    }

    /// <summary>
    ///     Takes away each put through a local at the given index of the array of
    ///     a field. A sure put through the field there replaces them.
    /// </summary>
    public void ReplacesThePutsThroughLocals(ISymbol field, int index)
    {
        if (this.putsThroughLocals.TryGetValue(
                key: field.OriginalDefinition,
                value: out List<(int Index, LoopState Held)>? puts))
        {
            puts.RemoveAll(put => put.Index == index);
        }
    }

    /// <summary>
    ///     The state of the array of a field from the puts through a local that
    ///     got it, from <see cref="AddAPutThroughALocal" />.
    /// </summary>
    public LoopState PutsThroughLocalsOf(ISymbol field) =>
        this.putsThroughLocals.Count > 0
        && this.putsThroughLocals.TryGetValue(
            key: field.OriginalDefinition,
            value: out List<(int Index, LoopState Held)>? puts)
            ? puts.Aggregate(seed: LoopState.None, func: static (all, put) => all.Join(put.Held))
            : LoopState.None;

    /// <summary>
    ///     The value of <see cref="MemberWrites" /> when a field last got a state
    ///     from <see cref="SetField" />. -1 for a field with no state.
    /// </summary>
    public int LastWriteOf(ISymbol field) =>
        this.lastWrites.TryGetValue(key: field.OriginalDefinition, value: out int at) ? at : -1;

    /// <summary>
    ///     Counts one more callee body that the walk reads only because the callee
    ///     gets an object of the store. No state from the loop goes into such a
    ///     callee. These bodies have their own limit, thus they do not use the
    ///     frames that a pending value needs.
    /// </summary>
    public bool TryEnterForObjects() => ++this.objectFrames <= MaxFrames;

    /// <summary>
    ///     Records the start of the read of a lambda or a local function for a
    ///     call. False when the walk is in a read of that function now, which is
    ///     a call of the function in its own code.
    /// </summary>
    public bool StartsACallOf(SyntaxNode function) => this.activeFunctions.Add(function);

    /// <summary>Records the end of the read that <see cref="StartsACallOf" /> started.</summary>
    public void EndsACallOf(SyntaxNode function) => this.activeFunctions.Remove(function);

    /// <summary>
    ///     True when the walk reads the given body now. A call of that body is
    ///     then a call of a method in its own code.
    /// </summary>
    public bool Walks(SyntaxNode body)
    {
        foreach ((SyntaxNode walked, int? _) in this.walks)
        {
            if (walked == body)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     True when the walk reads the given body now for a call in its own
    ///     code, and that call has the given shape.
    /// </summary>
    public bool WalksAgainWith(SyntaxNode body, int shape)
    {
        foreach ((SyntaxNode walked, int? itsShape) in this.walks)
        {
            if (walked == body && itsShape == shape)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Records the start of the read of a callee body. The shape is that of
    ///     a call in the code of the same body, and null for a different call.
    /// </summary>
    public void StartsAWalkOf(SyntaxNode body, int? shape) => this.walks.Add((body, shape));

    /// <summary>Records the end of the last read that <see cref="StartsAWalkOf" /> started.</summary>
    public void EndsAWalk() => this.walks.RemoveAt(this.walks.Count - 1);

    /// <summary>
    ///     The locals and the parameters that a function in the given function
    ///     reads or writes. Null when the compiler cannot tell.
    /// </summary>
    public ImmutableHashSet<ISymbol>? CapturedIn(SyntaxNode function)
    {
        if (!this.captured.TryGetValue(key: function, value: out ImmutableHashSet<ISymbol>? found))
        {
            SyntaxNode? body =
                function switch
                {
                    AnonymousFunctionExpressionSyntax lambda => lambda.Body,
                    LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody?.Expression,
                    _ => null
                };

            DataFlowAnalysis? flow =
                body switch
                {
                    StatementSyntax statement => this.ModelFor(function).AnalyzeDataFlow(statement),
                    ExpressionSyntax expression => this.ModelFor(function).AnalyzeDataFlow(expression),
                    _ => null
                };

            found = flow is { Succeeded: true }
                ? flow.Captured.ToImmutableHashSet(SymbolEqualityComparer.Default)
                : null;

            this.captured[function] = found;
        }

        return found;
    }

    /// <summary>
    ///     Walks the region for reads that run before the close, then checks each
    ///     expression that closes the loop for a link back to the placeholder.
    /// </summary>
    /// <param name="closings">
    ///     The expressions that the block returns, or the first element of a
    ///     tuple literal that it returns.
    /// </param>
    /// <param name="withCaptures">
    ///     True for a block that returns a tuple. The first element of the tuple
    ///     closes the loop.
    /// </param>
    /// <param name="canReadAgain">True when the block can have a read after this one.</param>
    /// <returns>
    ///     False when the block needs a read after this one. A send then gave a
    ///     sink content that it did not have in this read. This read then checks
    ///     no close, because the next read gives the close more content.
    /// </returns>
    public bool Analyze(ImmutableArray<ExpressionSyntax> closings, bool withCaptures, bool canReadAgain)
    {
        LoopStateEvaluator evaluator = new(this);

        evaluator.ReadBy(
                new SinkWalker(region: this, evaluator: evaluator, sink: this.Report))
            .Visit(this.Root);

        foreach (Finding unread in evaluator.Frame.TakeUnread())
        {
            this.Report(unread);
        }

        if (canReadAgain && this.Sent.SaysMoreThan(this.SentBefore))
        {
            return false;
        }

        foreach (ExpressionSyntax closing in closings)
        {
            LoopState state = evaluator.Eval(closing);

            // A tuple that is not a literal, as from a local or a helper, can have a state for each
            // part. The state of the whole is then of each part.
            if (withCaptures
                && this.TypeOf(closing) is { IsTupleType: true }
                && state.Members?.PartsAt(ofATuple: true).ToList() is { Count: > 0 } parts)
            {
                state = parts.Where(static part => part.Position == 0)
                    .Select(static part => part.State)
                    .FirstOrDefault() ?? LoopState.None;
            }

            if (this.ReadsBack)
            {
                this.CheckReadBack(closing: closing, state: state);
            }
            else if (state.Linked)
            {
                this.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.LinkedClose,
                        location: closing.GetLocation(),
                        this.PlaceholderName));
            }
            // The close reads the value. A function that calculates the value can read a pending value.
            else if (state.Pending || evaluator.AtTheFirstRead(read: closing, state: state, marks: null).Pending)
            {
                // A pending close with no link is a lazy sample of the loop in its own initial
                // value. That sample forces itself at the end of the transaction.
                this.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.PendingClose,
                        location: closing.GetLocation(),
                        this.PlaceholderName));
            }
        }

        this.ReportTheUndecidedReads();

        return true;
    }

    /// <summary>
    ///     Checks the value that closes a forward reference, and each of its
    ///     members. The close gives each of them the value that the code reads
    ///     back through the reference. A value that reads back itself gets its
    ///     links or its initial value from itself.
    /// </summary>
    /// <remarks>
    ///     A cycle through two members or more needs a switch for each. The first
    ///     switch links to a value that has no link back yet, and then samples it.
    ///     A sample that is pending forces itself before the second link. Thus,
    ///     such a cycle is a link only when its values are not pending.
    /// </remarks>
    private void CheckReadBack(CSharpSyntaxNode closing, LoopState state)
    {
        Dictionary<ISymbol, LoopState> parts =
            new(SymbolEqualityComparer.Default) { [this.PlaceholderState.Content.ReadBack[0]] = state };

        foreach (KeyValuePair<ISymbol, LoopState> member in state.Members?.Members ?? [])
        {
            parts[member.Key] = member.Value;
        }

        foreach (Diagnostic diagnostic in parts
                     .Where(part => part.Value is not { Linked: false, Pending: false } && ReachesItself(part.Key))
                     .Select(part =>
                         Diagnostic.Create(
                             descriptor: IsALink(member: part.Key, value: part.Value)
                                 ? Diagnostics.LinkedClose
                                 : Diagnostics.PendingClose,
                             location: closing.GetLocation(),
                             this.PlaceholderName)))
        {
            this.Report(diagnostic);
        }

        return;

        // A sample at the end of the transaction forces the value before the switch links to it.
        bool IsALink(ISymbol member, LoopState value) =>
            value.ReadBack.Contains(value: member, comparer: SymbolEqualityComparer.Default)
                ? value.Linked && !(value.Pending && this.forced.Contains(member))
                : !value.Pending;

        bool ReachesItself(ISymbol start)
        {
            HashSet<ISymbol> seen = new(SymbolEqualityComparer.Default);
            Stack<ISymbol> next = new([start]);

            while (next.Count > 0)
            {
                if (!parts.TryGetValue(key: next.Pop(), value: out LoopState? from)
                    || from is { Linked: false, Pending: false })
                {
                    continue;
                }

                foreach (ISymbol member in from.ReadBack)
                {
                    if (SymbolEqualityComparer.Default.Equals(x: member, y: start))
                    {
                        return true;
                    }

                    if (seen.Add(member))
                    {
                        next.Push(member);
                    }
                }
            }

            return false;
        }
    }
}
