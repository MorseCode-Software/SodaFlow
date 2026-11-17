using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
///     does. A member is each method, property, event, and field of a type of
///     the library. Each member of an interface of the platform that such a type
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
        typeof(AsyncMapStatus).Assembly,
        typeof(AsyncStreamExtensions).Assembly,
        typeof(IIdentity<>).Assembly,
        typeof(CollectionExtensionMethods).Assembly,
        typeof(IOneWayBindableValue<>).Assembly,
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
    // Enum or Exception is, gives its own interfaces, which read no value of the graph. A field
    // can hold a cell or a function, thus each field that is not a constant is a member also. A
    // constant, as the value of an enum, holds a value that the compiler sets.
    private static IEnumerable<string> MembersOf(Type type) =>
        type.GetMethods(Declared)
            .Concat(
                type.GetInterfaces()
                    .Where(static contract => contract.Namespace?.StartsWith(
                        value: "System",
                        comparisonType: StringComparison.Ordinal) == true)
                    .Except(type.BaseType?.GetInterfaces() ?? [])
                    .SelectMany(static contract => contract.GetMethods()))
            .Select(NameOf)
            .Concat(
                type.GetFields(Declared)
                    .Where(static field => !field.IsLiteral && !field.IsSpecialName)
                    .Select(static field => field.Name));

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

    /// <summary>
    ///     An operator whose function computes its value can also get a function
    ///     that it keeps for a later time. Only the first makes the result
    ///     pending. Thus, each function parameter of such an operator has to say
    ///     which it is, and this test fails for a new one until it does. A
    ///     comparer, and a cell of functions, are functions here also.
    /// </summary>
    [Test]
    public async Task EachFunctionOfAnOperatorThatComputesItsValueSaysWhetherItComputesIt()
    {
        IReadOnlyList<(string Method, string Parameter)> functions =
        [
            .. Libraries
                .SelectMany(static assembly => assembly.GetExportedTypes())
                .Where(static type => !typeof(Delegate).IsAssignableFrom(type))
                .SelectMany(static type => type.GetMethods(Declared))
                .Where(static method => OperatorTable.ValueFromFunction.ContainsKey(method.Name))
                .SelectMany(static method =>
                    method.GetParameters()
                        .Where(static parameter => IsFunction(parameter.ParameterType))
                        .Select(parameter => (Method: method.Name, Parameter: parameter.Name ?? string.Empty)))
                .Distinct()
        ];

        await Assert.That(functions).Contains(("Map", "onEvicted"));

        IReadOnlyList<string> unknown =
        [
            .. functions
                .Where(static function =>
                    IsListed(table: OperatorTable.ValueFromFunction, function: function)
                    == IsListed(table: OperatorTable.KeptBesideTheValue, function: function))
                .Select(static function => function.Method + "(" + function.Parameter + ")")
        ];

        await Assert.That(unknown)
            .IsEmpty()
            .Because("each function parameter is in ValueFromFunction or in KeptBesideTheValue, and not in both");

        IReadOnlyList<(string Method, string Parameter)> listed =
        [
            .. OperatorTable.ValueFromFunction.Concat(OperatorTable.KeptBesideTheValue)
                .SelectMany(static row => row.Value.Select(parameter => (Method: row.Key, Parameter: parameter)))
        ];

        await Assert.That(listed.Except(functions))
            .IsEmpty()
            .Because("each parameter in the two tables is a function parameter of a public method");
    }

    /// <summary>
    ///     A comparer is an object whose members the library calls. Each
    ///     parameter of a public method that gets a comparer has to say when the
    ///     library calls it. This test fails for a new one until it does. A
    ///     comparer of an operator that computes its value is in the tables of
    ///     that operator.
    /// </summary>
    /// <remarks>
    ///     The test sees a comparer only where the signature names one. The
    ///     default comparer of a type parameter, which calls <c>CompareTo</c> or
    ///     <c>Equals</c> of each value, has no parameter.
    /// </remarks>
    [Test]
    public async Task EachComparerParameterSaysWhenTheLibraryCallsIt()
    {
        IReadOnlyList<(string Method, string Parameter)> comparers =
        [
            .. Libraries
                .SelectMany(static assembly => assembly.GetExportedTypes())
                .Where(static type => !typeof(Delegate).IsAssignableFrom(type))
                .SelectMany(static type => type.GetMethods(Declared))
                .SelectMany(static method =>
                    method.GetParameters()
                        .Where(static parameter => IsComparer(parameter.ParameterType))
                        .Select(parameter => (Method: method.Name, Parameter: parameter.Name ?? string.Empty)))
                .Distinct()
        ];

        await Assert.That(comparers).Contains(("CreateTwoWay", "comparer"));

        IReadOnlyList<string> unknown =
        [
            .. comparers
                .Where(static comparer =>
                    new[]
                        {
                            OperatorTable.ValueFromFunction,
                            OperatorTable.KeptBesideTheValue,
                            OperatorTable.ComparersAtAWrite,
                            OperatorTable.ComparersAtAnUpdate
                        }
                        .Count(table => IsListed(table: table, function: comparer))
                    != 1)
                .Select(static comparer => comparer.Method + "(" + comparer.Parameter + ")")
        ];

        await Assert.That(unknown)
            .IsEmpty()
            .Because("each comparer parameter is in exactly one table of OperatorTable");

        IReadOnlyList<(string Method, string Parameter)> listed =
        [
            .. OperatorTable.ComparersAtAWrite.Concat(OperatorTable.ComparersAtAnUpdate)
                .SelectMany(static row => row.Value.Select(parameter => (Method: row.Key, Parameter: parameter)))
        ];

        await Assert.That(listed.Except(comparers))
            .IsEmpty()
            .Because("each parameter in the two lists of comparers is a comparer parameter of a public method");
    }

    private static bool IsComparer(Type type) =>
        type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(IComparer<>)
            || type.GetGenericTypeDefinition() == typeof(IEqualityComparer<>));

    private static bool IsListed(
        ImmutableDictionary<string, ImmutableHashSet<string>> table,
        (string Method, string Parameter) function) =>
        table.TryGetValue(key: function.Method, value: out ImmutableHashSet<string>? parameters)
        && parameters.Contains(function.Parameter);

    // A delegate, a comparer, or a type with a delegate as a type argument, as a cell of predicates
    // is.
    private static bool IsFunction(Type type) =>
        typeof(Delegate).IsAssignableFrom(type)
        || (type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(IComparer<>)
                || type.GetGenericTypeDefinition() == typeof(IEqualityComparer<>)
                || type.GetGenericArguments().Any(static argument => typeof(Delegate).IsAssignableFrom(argument))));
}
