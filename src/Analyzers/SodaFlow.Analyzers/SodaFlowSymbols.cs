using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Questions about symbols that the analyzer asks of the SodaFlow
///     assemblies. One instance is for one compilation.
/// </summary>
internal sealed class SodaFlowSymbols
{
    /// <summary>
    ///     The names of the assemblies of the library: the ones that build the
    ///     graph. With <see cref="PackagesWithNoGraph" />, these are the projects of
    ///     the solution that make a package, and a test holds the two lists to the
    ///     solution.
    /// </summary>
    /// <remarks>
    ///     A name must be equal to one in the list. An assembly of a different
    ///     owner can have a name that starts with <c>SodaFlow.</c>, and that
    ///     assembly is not the library. The analyzer follows its code, or reports
    ///     that it cannot.
    /// </remarks>
    public static readonly ImmutableHashSet<string> LibraryAssemblyNames =
        ImmutableHashSet.Create(
            equalityComparer: StringComparer.Ordinal,
            "SodaFlow",
            "SodaFlow.Async",
            "SodaFlow.Bindable.ObjectModel",
            "SodaFlow.Collections",
            "SodaFlow.Core",
            "SodaFlow.Core.Async",
            "SodaFlow.Core.Bindable.ObjectModel",
            "SodaFlow.Core.Collections",
            "SodaFlow.FSharp",
            "SodaFlow.FSharp.Async",
            "SodaFlow.FSharp.Bindable.ObjectModel",
            "SodaFlow.FSharp.Collections");

    /// <summary>
    ///     The packages of the solution that build no graph and do not reference
    ///     one that does. <c>SodaFlow.Functional</c> has <c>Maybe</c>, <c>Either</c>,
    ///     and helpers for sequences. It runs each function in the call, as a query
    ///     of the platform does. The analyzer reads a call to it as such a call.
    /// </summary>
    public static readonly ImmutableHashSet<string> PackagesWithNoGraph =
        ImmutableHashSet.Create(equalityComparer: StringComparer.Ordinal, item: "SodaFlow.Functional");

    private static readonly ImmutableHashSet<string> PendingPlaceholderTypes =
        ImmutableHashSet.Create("LoopedCell", "LoopedBehavior");

    private static readonly ImmutableHashSet<string> StreamPlaceholderTypes =
        ImmutableHashSet.Create("LoopedStream");

    private static readonly ImmutableHashSet<string> LooperTypes =
        ImmutableHashSet.Create("CellLooper", "BehaviorLooper", "StreamLooper", "ForwardReference");

    /// <summary>
    ///     The members that read the current value of a cell, a behavior, or a
    ///     bindable built from one.
    /// </summary>
    public static readonly ImmutableHashSet<string> ReadMembers =
        ImmutableHashSet.Create("Sample", "Value", "CanExecute");

    // The node actions of one compilation run at the same time, and each asks this question.
    // The methods that have a parameter whose values a callee enumerates. The walk asks at each
    // call of a method with no body to read.
    private readonly ConcurrentDictionary<IMethodSymbol, bool> takesASequence = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, bool> takesAFunction = new(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<IAssemblySymbol, bool> knowsTheLibrary =
        new(SymbolEqualityComparer.Default);

    private readonly Lazy<bool> declaresAdd;
    private readonly Lazy<bool> declaresDeconstruct;
    private readonly Lazy<bool> declaresDispose;
    private readonly Lazy<bool> declaresEnumerators;
    private readonly Lazy<bool> declaresIndexers;
    private readonly Lazy<bool> declaresOperators;
    private readonly Lazy<PlatformCallbacks> declaredCallbacks;
    private readonly Lazy<bool> declaresCompare;
    private readonly Lazy<bool> declaresPlatformMembers;
    private readonly Lazy<bool> declaresToString;
    private readonly Lazy<bool> declaresTruth;
    private readonly ImmutableHashSet<IAssemblySymbol> library;

    /// <summary>
    ///     Finds the assemblies of the library in a compilation: the compilation
    ///     itself when it is one of them, and each one that it references.
    /// </summary>
    /// <param name="compilation">The compilation.</param>
    public SodaFlowSymbols(Compilation compilation)
    {
        this.library =
            compilation.SourceModule.ReferencedAssemblySymbols
                .Add(compilation.Assembly)
                .Where(static assembly => LibraryAssemblyNames.Contains(assembly.Name))
                .ToImmutableHashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        this.declaresAdd = Declares(compilation: compilation, name: "Add");
        this.declaresDeconstruct = Declares(compilation: compilation, name: "Deconstruct");
        this.declaresDispose = Declares(compilation: compilation, name: "Dispose");
        this.declaresEnumerators = Declares(compilation: compilation, name: "GetEnumerator");
        this.declaresIndexers = Declares(compilation: compilation, name: "this[]");
        this.declaresToString = Declares(compilation: compilation, name: "ToString");
        this.declaresTruth = Declares(compilation: compilation, name: "op_True");

        Lazy<bool> declaresCompareTo = Declares(compilation: compilation, name: "CompareTo");
        Lazy<bool> declaresEquals = Declares(compilation: compilation, name: "Equals");
        Lazy<bool> declaresGetHashCode = Declares(compilation: compilation, name: "GetHashCode");

        this.declaredCallbacks =
            new Lazy<PlatformCallbacks>(() =>
                (declaresCompareTo.Value ? PlatformCallbacks.Compare : PlatformCallbacks.None)
                | (declaresEquals.Value ? PlatformCallbacks.Equals : PlatformCallbacks.None)
                | (declaresGetHashCode.Value ? PlatformCallbacks.Hash : PlatformCallbacks.None)
                | (this.declaresToString.Value ? PlatformCallbacks.Text : PlatformCallbacks.None));

        // Compare of a comparer, which a collection of the platform that keeps the comparer calls.
        this.declaresCompare = Declares(compilation: compilation, name: "Compare");

        this.declaresPlatformMembers =
            new Lazy<bool>(() =>
                this.DeclaresEnumerators
                || this.DeclaredCallbacks != PlatformCallbacks.None
                || this.declaresCompare.Value);

        this.declaresOperators =
            new Lazy<bool>(() =>
                compilation.ContainsSymbolsWithName(
                    predicate: static name => name.StartsWith(value: "op_", comparisonType: StringComparison.Ordinal),
                    filter: SymbolFilter.Member));

        INamedTypeSymbol root = compilation.GetSpecialType(SpecialType.System_Object);

        this.ObjectToString =
            root.GetMembers("ToString")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method => method.Parameters.IsEmpty);

        this.ObjectEquals =
            root.GetMembers("Equals")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method => method is { IsStatic: false, Parameters.Length: 1 });

        this.ObjectGetHashCode =
            root.GetMembers("GetHashCode")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method => method.Parameters.IsEmpty);

        this.DisposableDispose =
            compilation.GetSpecialType(SpecialType.System_IDisposable)
                .GetMembers("Dispose")
                .OfType<IMethodSymbol>()
                .FirstOrDefault();
    }

    /// <summary>
    ///     True when the compilation declares a method with the name <c>Add</c>.
    ///     A collection initializer can call it.
    /// </summary>
    /// <remarks>
    ///     Each property with a name that starts with <c>Declares</c> is the same
    ///     test for a different name. The walk asks one before it asks the compiler
    ///     what a node calls. Most compilations declare few of these members, and
    ///     the question to the compiler is most of the cost of the analyzer. The
    ///     answer comes from the names that the compilation declares, with no
    ///     binding.
    /// </remarks>
    public bool DeclaresAdd => this.declaresAdd.Value;

    /// <summary>True when the compilation declares a <c>Deconstruct</c> method.</summary>
    public bool DeclaresDeconstruct => this.declaresDeconstruct.Value;

    /// <summary>True when the compilation declares a <c>Dispose</c> method.</summary>
    public bool DeclaresDispose => this.declaresDispose.Value;

    /// <summary>True when the compilation declares a <c>GetEnumerator</c> method.</summary>
    public bool DeclaresEnumerators => this.declaresEnumerators.Value;

    /// <summary>True when the compilation declares an indexer.</summary>
    public bool DeclaresIndexers => this.declaresIndexers.Value;

    /// <summary>True when the compilation declares an operator or a conversion.</summary>
    public bool DeclaresOperators => this.declaresOperators.Value;

    /// <summary>
    ///     True when the compilation declares a member that code of the platform
    ///     calls on an object which it gets. Most compilations declare one, and a
    ///     compilation that declares none has no such call to follow.
    /// </summary>
    public bool DeclaresPlatformMembers => this.declaresPlatformMembers.Value;

    /// <summary>True when the compilation declares a <c>ToString</c> method.</summary>
    public bool DeclaresToString => this.declaresToString.Value;

    /// <summary>
    ///     True when the compilation declares <c>operator true</c>. A condition
    ///     on a value of that type calls it.
    /// </summary>
    public bool DeclaresTruth => this.declaresTruth.Value;

    /// <summary>
    ///     The members that the compilation declares, of those that code of the
    ///     platform calls on an object which it gets. They are <c>CompareTo</c>,
    ///     <c>Equals</c>, <c>GetHashCode</c>, and <c>ToString</c>.
    /// </summary>
    public PlatformCallbacks DeclaredCallbacks => this.declaredCallbacks.Value;

    /// <summary>
    ///     True when the compilation declares a member that a collection of the
    ///     platform calls. The collection calls it on an element, on a key, or on
    ///     a comparer that it keeps.
    /// </summary>
    public bool DeclaresKeyMembers =>
        (this.DeclaredCallbacks & ~PlatformCallbacks.Text) != PlatformCallbacks.None || this.declaresCompare.Value;

    /// <summary><c>ToString</c> of <c>object</c>, which an interpolation calls.</summary>
    public IMethodSymbol? ObjectToString { get; }

    /// <summary><c>Equals</c> of <c>object</c>, with one parameter.</summary>
    public IMethodSymbol? ObjectEquals { get; }

    /// <summary><c>GetHashCode</c> of <c>object</c>.</summary>
    public IMethodSymbol? ObjectGetHashCode { get; }

    /// <summary><c>Dispose</c> of <c>IDisposable</c>, which a <c>using</c> calls.</summary>
    public IMethodSymbol? DisposableDispose { get; }

    // An explicit member of an interface has the name of the interface before its own name.
    private static Lazy<bool> Declares(Compilation compilation, string name) =>
        new(() =>
            compilation.ContainsSymbolsWithName(
                predicate: declared =>
                    declared == name || declared.EndsWith(value: "." + name, comparisonType: StringComparison.Ordinal),
                filter: SymbolFilter.Member));

    /// <summary>
    ///     False for a compilation that is not the library and does not reference
    ///     it. That code has no loop block.
    /// </summary>
    public bool HasLibrary => !this.library.IsEmpty;

    /// <summary>A member of one of the assemblies of the library.</summary>
    public bool IsSodaFlow(ISymbol? symbol) =>
        symbol?.ContainingAssembly is { } assembly && this.library.Contains(assembly);

    /// <summary>
    ///     True for an assembly that is the library or references it. Code of
    ///     such an assembly can know that a value is a cell, and can sample it. Code
    ///     of each other assembly cannot. A list or a query of the platform only
    ///     keeps a cell, gives it back, or gives it to a function of the caller.
    /// </summary>
    public bool CanKnowTheLibrary(IAssemblySymbol? assembly) =>
        assembly == null
        || this.knowsTheLibrary.GetOrAdd(
            key: assembly,
            valueFactory: candidate =>
                this.library.Contains(candidate)
                || candidate.Modules.Any(module => module.ReferencedAssemblySymbols.Any(this.library.Contains)));

    /// <summary>
    ///     True for a type whose values can have a state. Such a type is a type of
    ///     the library, a type of this compilation, a delegate, or a type made from
    ///     one of those. A <c>Lazy</c> and a comparer are such types also, because
    ///     each can have a function. A number, a string, and a type of the platform
    ///     with no such part cannot.
    /// </summary>
    public bool CanHoldState(ITypeSymbol? type, IAssemblySymbol compilation) =>
        this.CanHoldState(type: type, compilation: compilation, depth: 0);

    private bool CanHoldState(ITypeSymbol? type, ISymbol compilation, int depth) =>
        type switch
        {
            null => false,
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array =>
                this.CanHoldState(type: array.ElementType, compilation: compilation, depth: depth),
            // An object can be a value of each type, thus it can be one that has a state.
            INamedTypeSymbol { SpecialType: SpecialType.System_Object } => true,
            // A collection that is not generic has objects as its elements, and its enumerator
            // gives them.
            INamedTypeSymbol { IsGenericType: false } collection when IsEnumerated(collection) => true,
            INamedTypeSymbol { SpecialType: not SpecialType.None } or INamedTypeSymbol { TypeKind: TypeKind.Enum } =>
                false,
            INamedTypeSymbol named =>
                named.TypeKind == TypeKind.Delegate
                || IsLazy(named)
                || IsComparer(named)
                || this.IsSodaFlow(named)
                || SymbolEqualityComparer.Default.Equals(x: named.ContainingAssembly, y: compilation)
                // A type in a generic type, such as the values of a dictionary, has the arguments
                // of that type.
                || (depth < 3
                    && (named.TypeArguments.Any(argument =>
                            this.CanHoldState(type: argument, compilation: compilation, depth: depth + 1))
                        || this.CanHoldState(
                            type: named.ContainingType,
                            compilation: compilation,
                            depth: depth + 1))),
            _ => false
        };

    /// <summary>
    ///     True for <c>System.Lazy</c>. It keeps the function that makes its
    ///     value, and runs that function at the first read of <c>Value</c>.
    /// </summary>
    public static bool IsLazy(ITypeSymbol? type) =>
        type?.OriginalDefinition is INamedTypeSymbol
        {
            Name: "Lazy",
            ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
        };

    /// <summary>
    ///     True for a comparer: <c>IComparer</c> or <c>IEqualityComparer</c> of the
    ///     platform, or a type that is one of them. The code that gets a comparer
    ///     calls it, as it calls a function.
    /// </summary>
    public static bool IsComparer(ITypeSymbol? type) =>
        type is INamedTypeSymbol named && (IsComparerInterface(named) || named.AllInterfaces.Any(IsComparerInterface));

    /// <summary>True for <c>IComparer</c> or <c>IEqualityComparer</c> of the platform.</summary>
    public static bool IsComparerInterface(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Interface, Name: "IComparer" or "IEqualityComparer" }
        && type.ContainingNamespace?.ToDisplayString() is "System.Collections.Generic" or "System.Collections";

    /// <summary>True for an <c>IComparable</c> interface of the platform.</summary>
    public static bool IsComparableInterface(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Interface, Name: "IComparable" } && IsSystem(type.ContainingNamespace);

    /// <summary>True for <c>IEquatable</c> of the platform.</summary>
    public static bool IsEquatableInterface(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Interface, Name: "IEquatable" } && IsSystem(type.ContainingNamespace);

    /// <summary>True for an <c>IEnumerable</c> interface of the platform.</summary>
    public static bool IsEnumerableInterface(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Interface, Name: "IEnumerable" } && IsCollections(type.ContainingNamespace);

    /// <summary>True for an <c>IEnumerator</c> interface of the platform.</summary>
    public static bool IsEnumeratorInterface(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Interface, Name: "IEnumerator" } && IsCollections(type.ContainingNamespace);

    /// <summary>
    ///     The type of the elements of a sequence: an array, or a type that is
    ///     <c>IEnumerable</c> of some type. Null for each other type, and for a
    ///     string.
    /// </summary>
    public static ITypeSymbol? ElementTypeOf(ITypeSymbol? type) =>
        type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { SpecialType: SpecialType.System_String } => null,
            INamedTypeSymbol named =>
                named.AllInterfaces
                    .Insert(index: 0, item: named)
                    .FirstOrDefault(static candidate => candidate.IsGenericType && IsEnumerableInterface(candidate))
                    ?.TypeArguments[0],
            _ => null
        };

    /// <summary>
    ///     True for a type whose values a callee enumerates or reads as elements:
    ///     an array, a span, or a type that is <c>IEnumerable</c>. A string is not
    ///     one here.
    /// </summary>
    public static bool IsSequence(ITypeSymbol? type) =>
        type switch
        {
            IArrayTypeSymbol => true,
            INamedTypeSymbol { SpecialType: SpecialType.System_String } => false,
            INamedTypeSymbol named =>
                IsEnumerableInterface(named)
                || named.AllInterfaces.Any(IsEnumerableInterface)
                || (named.Name is "Span" or "ReadOnlySpan" && IsSystem(named.ContainingNamespace)),
            _ => false
        };

    /// <summary>True for a type that is an <c>IEnumerator</c> of the platform.</summary>
    public static bool IsEnumerator(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && (IsEnumeratorInterface(named) || named.AllInterfaces.Any(IsEnumeratorInterface));

    /// <summary>
    ///     True for a type whose values a callee with no body to read enumerates:
    ///     a sequence, or an enumerator that the callee moves.
    /// </summary>
    public static bool IsEnumerated(ITypeSymbol? type) => IsSequence(type) || IsEnumerator(type);

    /// <summary>
    ///     True for <c>Exchange</c> and <c>CompareExchange</c> of
    ///     <c>Interlocked</c>. The result is the value that the location had, and
    ///     not one of the values that the callee got.
    /// </summary>
    public static bool IsExchange(IMethodSymbol definition) =>
        definition is { Name: "Exchange" or "CompareExchange", ContainingType: { Name: "Interlocked" } owner }
        && owner.ContainingNamespace?.ToDisplayString() == "System.Threading";

    /// <summary>
    ///     True for a method of a serializer that writes a value. Such a method
    ///     enumerates each sequence in the value, and reads the public properties
    ///     of each object in it. The rows are <c>JsonSerializer</c> of
    ///     <c>System.Text.Json</c>, <c>JsonConvert</c> and <c>JsonSerializer</c>
    ///     of <c>Newtonsoft.Json</c>, and <c>XmlSerializer</c>.
    /// </summary>
    public static bool IsSerializer(IMethodSymbol definition) =>
        definition.Name.StartsWith(value: "Serialize", comparisonType: StringComparison.Ordinal)
        && definition.ContainingType is { } owner
        && (owner.Name, owner.ContainingNamespace?.ToDisplayString())
            is ("JsonSerializer", "System.Text.Json")
            or ("JsonConvert" or "JsonSerializer", "Newtonsoft.Json")
            or ("XmlSerializer", "System.Xml.Serialization");

    /// <summary>
    ///     True for a serializer that writes the public fields of an object, and
    ///     thus the elements of a tuple. <c>System.Text.Json</c> writes only
    ///     properties, unless an option says differently.
    /// </summary>
    public static bool WritesFields(IMethodSymbol serializer) =>
        serializer.ContainingType?.ContainingNamespace?.ToDisplayString() != "System.Text.Json";

    /// <summary>
    ///     True for a property that the serializer reads. It is public, it is
    ///     not an indexer, and it has no attribute that tells a serializer to
    ///     ignore it. <c>XmlSerializer</c> reads only a property that it can also
    ///     set.
    /// </summary>
    public static bool IsSerialized(IPropertySymbol property, IMethodSymbol serializer) =>
        property is
        {
            IsStatic: false,
            IsIndexer: false,
            DeclaredAccessibility: Accessibility.Public,
            GetMethod.DeclaredAccessibility: Accessibility.Public
        }
        && (serializer.ContainingType?.Name != "XmlSerializer"
            || property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
        && !property.GetAttributes()
            .Any(static attribute =>
                attribute.AttributeClass?.Name is "JsonIgnoreAttribute" or "XmlIgnoreAttribute"
                // A condition on JsonIgnore reads the value before it decides.
                && attribute.NamedArguments.IsEmpty);

    /// <summary>
    ///     True for a member of <c>System.Activator</c>, or of a type in
    ///     <c>System.Reflection</c>.
    /// </summary>
    public static bool IsReflection(ISymbol member) =>
        member.ContainingType is { } owner
        && (owner.ContainingNamespace?.ToDisplayString() == "System.Reflection"
            || (owner.Name == "Activator" && IsSystem(owner.ContainingNamespace)));

    /// <summary>
    ///     True for a method that has a parameter whose type is a delegate. Most
    ///     methods have none, and the answer for a method is the same at each call.
    /// </summary>
    public bool TakesAFunction(IMethodSymbol definition) =>
        this.takesAFunction.GetOrAdd(
            key: definition,
            valueFactory: static method =>
                method.Parameters.Any(static parameter => parameter.Type.TypeKind == TypeKind.Delegate));

    /// <summary>
    ///     True for a method that has a parameter whose values a callee
    ///     enumerates. For an extension method, the receiver is the first one.
    /// </summary>
    public bool TakesASequence(IMethodSymbol definition) =>
        this.takesASequence.GetOrAdd(
            key: definition,
            valueFactory: static method => method.Parameters.Any(static parameter => IsEnumerated(parameter.Type)));

    /// <summary>
    ///     True for a type whose values can be a query, or can contain one. It is
    ///     a sequence, an enumerator, or a type made from one of those. A sequence
    ///     of numbers has no other state, but it can be a query that runs a
    ///     function.
    /// </summary>
    public static bool CanHoldQuery(ITypeSymbol? type) => CanHoldQuery(type: type, depth: 0);

    private static bool CanHoldQuery(ITypeSymbol? type, int depth) =>
        IsEnumerated(type)
        || (depth < 3
            && type is INamedTypeSymbol named
            && named.TypeArguments.Any(argument => CanHoldQuery(type: argument, depth: depth + 1)));

    /// <summary>
    ///     True for a method of <c>System.Linq.Enumerable</c> that makes a query
    ///     and enumerates nothing. Such a method returns <c>IEnumerable</c> or
    ///     <c>IOrderedEnumerable</c>. Each other method of that class that reads
    ///     its source enumerates it in the call. The same rule is correct for the
    ///     methods of <c>SodaFlow.Functional</c>, such as <c>Choose</c>.
    /// </summary>
    /// <remarks>
    ///     The rule is not for code of a different owner: a method there that
    ///     returns a sequence can enumerate its source in the call.
    /// </remarks>
    public static bool MakesQuery(IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        return definition.ReturnType is INamedTypeSymbol
               {
                   TypeKind: TypeKind.Interface,
                   Name: "IEnumerable" or "IOrderedEnumerable"
               }
               && (IsLinqEnumerable(definition.ContainingType)
                   || PackagesWithNoGraph.Contains(definition.ContainingAssembly?.Name ?? string.Empty));
    }

    /// <summary>
    ///     True for a type whose object has the elements of a collection
    ///     expression in the order of the code. The types are an array,
    ///     <c>List</c>, and each interface that the compiler gives a list or an
    ///     array for. A set or a sorted collection has a different order.
    /// </summary>
    public static bool KeepsTheOrder(ITypeSymbol? type) =>
        type is IArrayTypeSymbol
        || (type is INamedTypeSymbol
            {
                Name: "List" or "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection"
            } named
            && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic");

    public static bool IsLinqEnumerable(ISymbol? type) =>
        type is { Name: "Enumerable" } && type.ContainingNamespace?.ToDisplayString() == "System.Linq";

    /// <summary>
    ///     True for a symbol of the platform: one in the <c>System</c> namespace,
    ///     or in a namespace in it.
    /// </summary>
    public static bool IsPlatform(ISymbol symbol)
    {
        INamespaceSymbol? space = symbol.ContainingNamespace;

        while (space is { ContainingNamespace.IsGlobalNamespace: false })
        {
            space = space.ContainingNamespace;
        }

        return IsSystem(space);
    }

    private static bool IsSystem(INamespaceSymbol? space) =>
        space is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };

    /// <summary>
    ///     True for a type of the platform that only holds the values that its
    ///     constructor gets: <c>KeyValuePair</c>, <c>Tuple</c>, and
    ///     <c>DictionaryEntry</c>. Each value is in the property that has the name
    ///     of the parameter. Thus, the evaluator can keep a state for each part.
    /// </summary>
    public static bool HoldsParts(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "KeyValuePair" or "Tuple" or "DictionaryEntry" } named
        && (named.Name, named.ContainingNamespace?.ToDisplayString())
            is ("KeyValuePair", "System.Collections.Generic")
            or ("Tuple", "System")
            or ("DictionaryEntry", "System.Collections");

    /// <summary>
    ///     The property of a type from <see cref="HoldsParts" /> that holds the
    ///     value of a parameter. The parameter is one of the constructor, of
    ///     <c>Create</c>, or an <c>out</c> parameter of <c>Deconstruct</c>. The
    ///     names are the same, but for the case of the first letter.
    /// </summary>
    public static IPropertySymbol? PartFor(ITypeSymbol holder, IParameterSymbol parameter) =>
        holder.OriginalDefinition.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property =>
                string.Equals(a: property.Name, b: parameter.Name, comparisonType: StringComparison.OrdinalIgnoreCase));

    private static bool IsCollections(ISymbol? space) =>
        space?.ToDisplayString() is "System.Collections.Generic" or "System.Collections";

    /// <summary>True for the <c>Value</c> property of <c>System.Lazy</c>.</summary>
    public static bool IsLazyValue(IPropertySymbol property) =>
        property.Name == "Value" && IsLazy(property.ContainingType);

    /// <summary>
    ///     A method that runs a loop block. The public blocks are
    ///     <c>WithCaptures</c> and <c>WithoutCaptures</c> on a looper type. The
    ///     assemblies that build on Core use <c>WithCapturesImpl</c> and
    ///     <c>WithoutCapturesImpl</c> on <c>LoopedCell</c>, which are internal.
    /// </summary>
    public bool IsLoopBlock(IMethodSymbol method) =>
        IsLoopBlockName(method.Name)
        && this.IsSodaFlow(method.ContainingType)
        && method.Name switch
        {
            "WithCaptures" or "WithoutCaptures" =>
                LooperTypes.Contains(method.ContainingType.OriginalDefinition.Name),
            "WithCapturesImpl" or "WithoutCapturesImpl" =>
                method.ContainingType.OriginalDefinition.Name == "LoopedCell",
            _ => false
        };

    /// <summary>
    ///     True for each name that <see cref="IsLoopBlock" /> accepts. The name of
    ///     an invocation is in its syntax, thus this check needs no symbol.
    /// </summary>
    public static bool IsLoopBlockName(string name) =>
        name is "WithCaptures" or "WithoutCaptures" or "WithCapturesImpl" or "WithoutCapturesImpl";

    /// <summary>
    ///     The state that an expression has from its type alone. Only a
    ///     placeholder has a placeholder type, so the type says that the value is
    ///     pending.
    /// </summary>
    public LoopState StateOfType(ITypeSymbol? type) => this.StateOfType(type: type, depth: 0);

    private LoopState StateOfType(ITypeSymbol? type, int depth)
    {
        // An array of placeholders is pending as one value, as a list of them is.
        while (type is IArrayTypeSymbol array)
        {
            type = array.ElementType;
            depth++;
        }

        if (type is not INamedTypeSymbol named)
        {
            return LoopState.None;
        }

        if (this.IsSodaFlow(named))
        {
            string name = named.OriginalDefinition.Name;

            if (PendingPlaceholderTypes.Contains(name))
            {
                return LoopState.PendingPlaceholder;
            }

            return StreamPlaceholderTypes.Contains(name) ? LoopState.StreamPlaceholder : LoopState.None;
        }

        // One level into a collection type: a list of placeholders is pending as one value, which
        // is what a list Lift over it reads.
        return depth == 0
            ? named.TypeArguments.Aggregate(
                seed: LoopState.None,
                func: (state, argument) => state.Join(this.StateOfType(type: argument, depth: 1)))
            : LoopState.None;
    }

    /// <summary>The parameter that an argument binds to, by name or by position.</summary>
    public static IParameterSymbol? ParameterOf(IMethodSymbol callee, ArgumentSyntax argument, int position)
    {
        if (argument.NameColon != null)
        {
            string name = argument.NameColon.Name.Identifier.ValueText;

            return callee.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        }

        if (position < callee.Parameters.Length)
        {
            return callee.Parameters[position];
        }

        return callee.Parameters.Length > 0 && callee.Parameters[callee.Parameters.Length - 1].IsParams
            ? callee.Parameters[callee.Parameters.Length - 1]
            : null;
    }

    public bool IsStreamType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named && this.IsSodaFlow(named) && IsStreamName(named.OriginalDefinition.Name);

    public bool IsCellOrBehaviorType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && this.IsSodaFlow(named)
        && IsCellOrBehaviorName(named.OriginalDefinition.Name);

    public bool IsLazySampleType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.Name: "LazySample" } named && this.IsSodaFlow(named);

    /// <summary>
    ///     A type whose values can hold a pending read: a cell, a behavior, a
    ///     lazy sample, or a bindable.
    /// </summary>
    public bool IsCarrierType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && this.IsSodaFlow(named)
        && !IsStreamName(named.OriginalDefinition.Name)
        && named.SpecialType == SpecialType.None
        && !IsListenerName(named.OriginalDefinition.Name);

    private static bool IsStreamName(string name) =>
        name is "Stream" or "StreamSink" or "LoopedStream" or "CellStreamSink";

    private static bool IsCellOrBehaviorName(string name) =>
        name is "Cell"
            or "CellSink"
            or "LoopedCell"
            or "Behavior"
            or "BehaviorSink"
            or "LoopedBehavior";

    private static bool IsListenerName(string name) => name is "IListener" or "IStrongListener" or "IWeakListener";
}
