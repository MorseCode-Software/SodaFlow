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
///     Holds <see cref="OperatorTable" /> to the public members of the
///     library.
/// </summary>
/// <remarks>
///     A member with no row falls to the default rule, which keeps the state of
///     its inputs. That is correct for most members. It is incorrect for one
///     that crosses a transaction or reads a value at construction. Thus, a new
///     member has to say which it is, with a row in the table or an entry in
///     <see cref="OperatorTable.DefaultIsSound" />. This test fails until it
///     does. A member is each method, property, and event of a type of the
///     library. Each member of an interface of the platform that such a type
///     implements is one also.
/// </remarks>
internal sealed class OperatorTableTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

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
    public async Task EveryPublicMemberHasARowOrIsListedAsSound()
    {
        IReadOnlyList<string> members =
        [
            .. Libraries
                .SelectMany(static assembly => assembly.GetExportedTypes())
                // A delegate type has methods to call it. They are not members of the library.
                .Where(static type => !typeof(Delegate).IsAssignableFrom(type))
                .SelectMany(MembersOf)
                .Distinct()
                .OrderBy(keySelector: static name => name, comparer: StringComparer.Ordinal)
        ];

        // An operator, a property, a setter, and an interface member of the platform are each in
        // the list.
        await Assert.That(members).Contains("Map");
        await Assert.That(members).Contains("KeysCell");
        await Assert.That(members).Contains("Value");
        await Assert.That(members).Contains("CanExecuteChanged");

        IReadOnlyList<string> unknown =
        [
            .. members.Where(static name =>
                !OperatorTable.Names.Contains(name) && !OperatorTable.DefaultIsSound.Contains(name))
        ];

        await Assert.That(unknown)
            .IsEmpty()
            .Because("each public member needs a row in OperatorTable, or a place in DefaultIsSound");
    }

    // The methods that a type declares, and the members of each interface of the platform that
    // it implements, as ICommand of a bindable action. An accessor has the name of its property
    // or event, thus a setter is in the list as its property. A base type of the platform, as
    // Enum or Exception is, gives its own interfaces, which read no value of the graph.
    private static IEnumerable<string> MembersOf(Type type) =>
        type.GetMethods(Declared)
            .Concat(
                type.GetInterfaces()
                    .Where(static contract => contract.Namespace?.StartsWith(
                        value: "System",
                        comparisonType: StringComparison.Ordinal) == true)
                    .Except(type.BaseType?.GetInterfaces() ?? [])
                    .SelectMany(static contract => contract.GetMethods()))
            .Select(NameOf);

    private static string NameOf(MethodInfo method)
    {
        if (!method.IsSpecialName)
        {
            return method.Name;
        }

        string[] prefixes = ["get_", "set_", "add_", "remove_"];

        string? prefix =
            prefixes.FirstOrDefault(candidate =>
                method.Name.StartsWith(value: candidate, comparisonType: StringComparison.Ordinal));

        return prefix == null ? method.Name : method.Name[prefix.Length..];
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
                .SelectMany(static type => type.GetMethods(Declared))
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
