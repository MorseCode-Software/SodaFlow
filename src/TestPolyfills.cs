// Compiler-support types that net472 does not have, linked into every C# test project.
//
// TUnit's source generator emits a module initializer as part of each test assembly, and
// ModuleInitializerAttribute did not exist before .NET 5. The attribute is recognized by name, so
// declaring it here is enough to make the generated code compile; nothing references it directly.
//
// IsExternalInit is the same arrangement for init accessors and records, which the tests use and
// which net472 has no support type for. Nothing references it by name either: the compiler looks it
// up, which is why it is marked as used rather than left to look dead.

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [JetBrains.Annotations.UsedImplicitly]
    internal static class IsExternalInit;

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    [JetBrains.Annotations.UsedImplicitly]
    internal sealed class ModuleInitializerAttribute : Attribute;
}
#endif
