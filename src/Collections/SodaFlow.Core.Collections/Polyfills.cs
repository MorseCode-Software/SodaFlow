#if !NETCOREAPP3_0_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis
{
    // ReSharper disable once RedundantAttributeUsageProperty
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        [UsedImplicitly]
        public bool ReturnValue { get; }

        public NotNullWhenAttribute(bool returnValue) =>
            this.ReturnValue = returnValue;
    }
}
#endif
