using System.Reflection;
using BenchmarkDotNet.Running;

namespace SodaFlow.Benchmarks;

/// <summary>
///     Entry point for the benchmark suite.
/// </summary>
/// <remarks>
///     <para>
///         A switcher rather than a single <c>BenchmarkRunner.Run</c> call, so that adding a class
///         is all it takes to add a benchmark: nothing here has to name it.
///     </para>
///     <para>
///         Run everything, which takes a while, with
///         <c>dotnet run -c Release --project src/CSharp/SodaFlow.Benchmarks -- --filter *</c>,
///         or one class with <c>--filter *BindableValue*</c>. Release is not optional -
///         BenchmarkDotNet refuses to measure a debug build, and is right to.
///     </para>
/// </remarks>
// ReSharper disable once MemberCanBeFileLocal - The entry point, which the runtime locates
// rather than any caller in this file.
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
