using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SodaFlow.Async;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using TUnit.Assertions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Holds <see cref="OperatorTable" /> to the public operators of the
///     library.
/// </summary>
/// <remarks>
///     An operator is a public method that returns a cell, a behavior, a
///     stream, a lazy sample, a sink, a collection, or a bindable. An operator
///     with no row falls to the default rule, which keeps the state of its
///     inputs. That is correct for most operators. It is incorrect for one that
///     crosses a transaction or reads a value at construction. Thus, a new
///     operator has to say which it is, with a row in the table or an entry in
///     <see cref="OperatorTable.DefaultIsSound" />. This test fails until it
///     does.
/// </remarks>
internal sealed class OperatorTableTests
{
    private static readonly string[] ResultTypes =
    [
        "Cell`1",
        "Behavior`1",
        "Stream`1",
        "LazySample`1",
        "CellSink`1",
        "BehaviorSink`1",
        "StreamSink`1",
        "CellStreamSink`1",
        "ReactiveCollection`3",
        "MappedItems`1",
        "IOneWayBindableValue`1",
        "ITwoWayBindableValue`1",
        "IBindableAction",
        "IBindableAction`1"
    ];

    // The assemblies of the library that have C# operators. The F# assemblies give the same
    // operators different names, and the analyzer reads C# only.
    private static readonly Assembly[] Libraries =
    [
        typeof(Cell<>).Assembly,
        typeof(CellExtensionMethods).Assembly,
        typeof(AsyncConcurrencyStrategy).Assembly,
        typeof(AsyncStreamExtensions).Assembly,
        typeof(IIdentity<>).Assembly,
        typeof(CollectionExtensionMethods).Assembly,
        typeof(IBindableFactory).Assembly,
        typeof(BindableFactory).Assembly
    ];

    [Test]
    public async Task EveryPublicOperatorHasARowOrIsListedAsSound()
    {
        IReadOnlyList<string> operators =
        [
            .. Libraries
                .SelectMany(static assembly => assembly.GetExportedTypes())
                .SelectMany(static type =>
                    type.GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                // A property getter is not an operator: it has no inputs of its own.
                .Where(static method => !method.IsSpecialName)
                .Where(static method =>
                    ResultTypes.Contains(
                        method.ReturnType.IsGenericType
                            ? method.ReturnType.GetGenericTypeDefinition().Name
                            : method.ReturnType.Name))
                .Select(static method => method.Name)
                .Distinct()
                .OrderBy(keySelector: static name => name, comparer: StringComparer.Ordinal)
        ];

        await Assert.That(operators).IsNotEmpty();

        IReadOnlyList<string> unknown =
        [
            .. operators.Where(static name =>
                !OperatorTable.Names.Contains(name) && !OperatorTable.DefaultIsSound.Contains(name))
        ];

        await Assert.That(unknown)
            .IsEmpty()
            .Because("each public operator needs a row in OperatorTable, or a place in DefaultIsSound");
    }

    [Test]
    public async Task NoNameIsBothARowAndListedAsSound() =>
        await Assert.That(OperatorTable.Names.Intersect(OperatorTable.DefaultIsSound)).IsEmpty();

    /// <summary>
    ///     A method with a function parameter runs the function in the call, or
    ///     keeps it for a time after the call. A read in a function that runs in the
    ///     call is a read in the block. Thus, each such method has to say which it
    ///     does, and this test fails for a new one until it does.
    /// </summary>
    [Test]
    public async Task EveryPublicMethodThatTakesAFunctionSaysWhenItRunsIt()
    {
        IReadOnlyList<string> takers =
        [
            .. Libraries
                .SelectMany(static assembly => assembly.GetExportedTypes())
                // A delegate type has methods with a callback parameter. They are not methods of
                // the library.
                .Where(static type => !typeof(Delegate).IsAssignableFrom(type))
                .SelectMany(static type =>
                    type.GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(static method => !method.IsSpecialName)
                .Where(static method =>
                    method.GetParameters()
                        .Any(static parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType)))
                .Select(static method => method.Name)
                .Distinct()
                .OrderBy(keySelector: static name => name, comparer: StringComparer.Ordinal)
        ];

        await Assert.That(takers).IsNotEmpty();

        IReadOnlyList<string> unknown =
        [
            .. takers.Where(static name =>
                !OperatorTable.FunctionsInTheCall.Contains(name)
                && !OperatorTable.FunctionsAfterTheCall.Contains(name))
        ];

        await Assert.That(unknown)
            .IsEmpty()
            .Because("each method that takes a function is in FunctionsInTheCall or in FunctionsAfterTheCall");

        await Assert.That(OperatorTable.FunctionsInTheCall.Intersect(OperatorTable.FunctionsAfterTheCall)).IsEmpty();

        await Assert.That(
                OperatorTable.FunctionsInTheCall.Union(OperatorTable.FunctionsAfterTheCall).Except(takers))
            .IsEmpty()
            .Because("each name in the two lists is a public method that takes a function");
    }
}
