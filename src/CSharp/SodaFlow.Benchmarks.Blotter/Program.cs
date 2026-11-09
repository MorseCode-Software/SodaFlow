using System.Reflection;
using BenchmarkDotNet.Running;

namespace SodaFlow.Benchmarks.Blotter;

/// <summary>Entry point. See README.md for how to run each copy of the libraries.</summary>
// ReSharper disable once MemberCanBeFileLocal - The entry point, which the runtime locates
// rather than any caller in this file.
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
