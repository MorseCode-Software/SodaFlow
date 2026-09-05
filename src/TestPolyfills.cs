// Compiler-support types that net472 does not have, linked into every C# test project.
//
// TUnit's source generator emits a module initializer as part of each test assembly, and
// ModuleInitializerAttribute did not exist before .NET 5. The attribute is recognized by name, so
// declaring it here is enough to make the generated code compile; nothing references this directly.

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>Polyfill of <see cref="IsExternalInit" />, introduced in .NET 5, for init and record syntax.</summary>
    internal static class IsExternalInit;

    /// <summary>Polyfill of <see cref="ModuleInitializerAttribute" />, introduced in .NET 5.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute;
}
#endif
