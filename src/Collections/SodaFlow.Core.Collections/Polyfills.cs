#if !NETCOREAPP3_0_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
using JetBrains.Annotations;

// ReSharper disable MemberCanBeFileLocal - Each type here is a polyfill that the compiler finds by its full name, which a file type does not keep.
// ReSharper disable InheritdocConsiderUsage
// ReSharper disable CheckNamespace
// ReSharper disable RedundantAttributeUsageProperty

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">
        ///     The condition on the return value. When the method returns this value, the parameter
        ///     can be null.
        /// </param>
        public MaybeNullWhenAttribute(bool returnValue) => this.ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        [UsedImplicitly]
        public bool ReturnValue { get; }
    }

    /// <summary>
    ///     Gives that the parameter is not null when a method returns
    ///     <see cref="ReturnValue" />, at each type of the parameter that permits null.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">
        ///     The condition on the return value. When the method returns this value, the parameter
        ///     is not null.
        /// </param>
        public NotNullWhenAttribute(bool returnValue) => this.ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        [UsedImplicitly]
        public bool ReturnValue { get; }
    }
}
#endif
