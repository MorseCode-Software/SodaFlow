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
            StringComparer.Ordinal,
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
        ImmutableHashSet.Create(StringComparer.Ordinal, "SodaFlow.Functional");

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
    private readonly ConcurrentDictionary<IAssemblySymbol, bool> knowsTheLibrary =
        new(SymbolEqualityComparer.Default);

    private readonly ImmutableHashSet<IAssemblySymbol> library;

    /// <summary>
    ///     Finds the assemblies of the library in a compilation: the compilation
    ///     itself when it is one of them, and each one that it references.
    /// </summary>
    /// <param name="compilation">The compilation.</param>
    public SodaFlowSymbols(Compilation compilation) =>
        this.library =
            compilation.SourceModule.ReferencedAssemblySymbols
                .Add(compilation.Assembly)
                .Where(static assembly => LibraryAssemblyNames.Contains(assembly.Name))
                .ToImmutableHashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

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
    ///     one of those. A number, a string, and a type of the platform with no such
    ///     part cannot.
    /// </summary>
    public bool CanHoldState(ITypeSymbol? type, IAssemblySymbol compilation) =>
        this.CanHoldState(type: type, compilation: compilation, depth: 0);

    private bool CanHoldState(ITypeSymbol? type, IAssemblySymbol compilation, int depth) =>
        type switch
        {
            null => false,
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array =>
                this.CanHoldState(type: array.ElementType, compilation: compilation, depth: depth),
            INamedTypeSymbol { SpecialType: not SpecialType.None } or INamedTypeSymbol { TypeKind: TypeKind.Enum } =>
                false,
            INamedTypeSymbol named =>
                named.TypeKind == TypeKind.Delegate
                || this.IsSodaFlow(named)
                || SymbolEqualityComparer.Default.Equals(x: named.ContainingAssembly, y: compilation)
                || (depth < 3
                    && named.TypeArguments.Any(argument =>
                        this.CanHoldState(type: argument, compilation: compilation, depth: depth + 1))),
            _ => false
        };

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
