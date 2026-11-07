using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SodaFlow.Bindable.ObjectModel;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Holds <see cref="OperatorTable" /> to the public operators of the library.
/// </summary>
/// <remarks>
///     An operator with no row falls to the default rule, which keeps the state of its
///     inputs. That is correct for most operators. It is incorrect for one that crosses
///     a transaction or reads a value at construction. Thus, a new operator has to say
///     which it is, with a row in the table or an entry in
///     <see cref="OperatorTable.DefaultIsSound" />. This test fails until it does.
/// </remarks>
internal sealed class OperatorTableTests
{
    private static readonly string[] ResultTypes =
    [
        "Cell`1", "Behavior`1", "Stream`1", "LazySample`1", "CellSink`1", "BehaviorSink`1", "StreamSink`1",
        "CellStreamSink`1"
    ];

    [Test]
    public async Task EveryPublicOperatorHasARowOrIsListedAsSound()
    {
        Assembly[] libraries =
        [
            typeof(Cell<>).Assembly,
            typeof(CellExtensionMethods).Assembly,
            typeof(Functional.Maybe<>).Assembly,
            typeof(Async.AsyncConcurrencyStrategy).Assembly,
            typeof(Async.AsyncStreamExtensions).Assembly,
            typeof(Collections.IIdentity<>).Assembly,
            typeof(Collections.CollectionExtensionMethods).Assembly,
            typeof(IBindableFactory).Assembly,
            typeof(BindableFactory).Assembly
        ];

        IReadOnlyList<string> operators =
        [
            .. libraries
                .SelectMany(static assembly => assembly.GetExportedTypes())
                .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                // A property getter is not an operator: it has no inputs of its own.
                .Where(static method => !method.IsSpecialName)
                .Where(static method => method.ReturnType.IsGenericType && ResultTypes.Contains(method.ReturnType.GetGenericTypeDefinition().Name))
                .Select(static method => method.Name)
                .Distinct()
                .OrderBy(static name => name, StringComparer.Ordinal)
        ];

        await Assert.That(operators).IsNotEmpty();

        IReadOnlyList<string> unknown =
        [
            .. operators.Where(static name => !OperatorTable.Names.Contains(name) && !OperatorTable.DefaultIsSound.Contains(name))
        ];

        await Assert.That(unknown).IsEmpty().Because("each public operator needs a row in OperatorTable, or a place in DefaultIsSound");
    }

    [Test]
    public async Task NoNameIsBothARowAndListedAsSound() =>
        await Assert.That(OperatorTable.Names.Intersect(OperatorTable.DefaultIsSound)).IsEmpty();
}
