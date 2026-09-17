#if !NETCOREAPP3_0_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis
{
    // ReSharper disable once RedundantAttributeUsageProperty
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    // ReSharper disable once InheritdocConsiderUsage
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">
        ///     The return value condition. If the method returns this value, the associated parameter may be null.
        /// </param>
        // ReSharper disable once InheritdocConsiderUsage
        public MaybeNullWhenAttribute(bool returnValue) => this.ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        [UsedImplicitly]
        public bool ReturnValue { get; }
    }

    /// <summary>
    ///     Specifies that when a method returns <see cref="ReturnValue" />, the parameter will not be null even if the
    ///     corresponding type allows it.
    /// </summary>
    // ReSharper disable once RedundantAttributeUsageProperty
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    // ReSharper disable once InheritdocConsiderUsage
    internal sealed class NotNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">
        ///     The return value condition. If the method returns this value, the associated parameter will not be null.
        /// </param>
        // ReSharper disable once InheritdocConsiderUsage
        public NotNullWhenAttribute(bool returnValue) => this.ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        [UsedImplicitly]
        public bool ReturnValue { get; }
    }
}
#endif
