namespace SodaFlow.Bindable.ObjectModel
{
    public static partial class BindableCoreExtensionMethods
    {
        /// <summary>
        ///     Holds one value, so that the field referring to it can be <c>volatile</c>.
        /// </summary>
        /// <typeparam name="T">The type of the value held.</typeparam>
        /// <remarks>
        ///     <para>
        ///         A field of type <typeparamref name="T" /> cannot be marked <c>volatile</c>, because
        ///         <typeparamref name="T" /> may be a value type, and <see cref="System.Threading.Volatile" />
        ///         will not accept one either. That leaves a bindable's cached value written by whichever
        ///         thread constructed it and read by the binding thread with nothing ordering the two.
        ///     </para>
        ///     <para>
        ///         A reference to an immutable box solves both halves. The assignment is a single
        ///         reference write, so a wide struct can never be read half-updated; and
        ///         <c>volatile</c> on the reference gives the release on write and acquire on read that
        ///         publish the value along with it. That is what lets a view model build its bindables
        ///         on whatever thread it happens to be running on, which is the whole point — a view
        ///         model has no business knowing which thread the binding engine uses.
        ///     </para>
        ///     <para>
        ///         The cost is an allocation per change. A bound property changes at the rate a person
        ///         types or a graph updates, so this is not a hot path, and the alternative — a lock
        ///         around every get and set — is worse on both counts.
        ///     </para>
        /// </remarks>
        private sealed class ValueBox<T>
        {
            internal ValueBox(T value) => this.Value = value;

            /// <summary>The value held. Readonly, so it is published with the box.</summary>
            internal T Value { get; }
        }
    }
}
