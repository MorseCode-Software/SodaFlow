namespace SodaFlow.Functional;

/// <summary>
///     A method which produces a value if it can, reporting whether it did through its return
///     value and handing the value back through an output parameter.
/// </summary>
/// <typeparam name="TResult">The type of the value produced.</typeparam>
/// <param name="result">
///     Set to the value produced when this returns <see langword="true" />, and left at the
///     default for its type otherwise.
/// </param>
/// <returns>
///     <see langword="true" /> if a value was produced, and <see langword="false" /> otherwise.
/// </returns>
/// <remarks>
///     This describes the shape the framework uses everywhere it has to say "there may be no
///     answer" without an exception. Naming that shape is what lets
///     <see cref="Maybe.FromTryGet{TResult}" /> turn any such method into a
///     <see cref="Maybe{T}" />, including ones this library knows nothing about.
/// </remarks>
public delegate bool TryGet<TResult>(out TResult result);

/// <summary>
///     A method which produces a value from one input if it can, reporting whether it did
///     through its return value and handing the value back through an output parameter.
/// </summary>
/// <typeparam name="T">The type of the input.</typeparam>
/// <typeparam name="TResult">The type of the value produced.</typeparam>
/// <param name="value">The input to produce a value from.</param>
/// <param name="result">
///     Set to the value produced when this returns <see langword="true" />, and left at the
///     default for its type otherwise.
/// </param>
/// <returns>
///     <see langword="true" /> if a value was produced, and <see langword="false" /> otherwise.
/// </returns>
/// <remarks>
///     <see cref="int.TryParse(string,out int)" /> and
///     <see cref="System.Collections.Generic.IDictionary{TKey,TValue}.TryGetValue" /> both have
///     this shape. Pass one to <see cref="Maybe.FromTryGet{T,TResult}" /> to get a
///     <see cref="Maybe{T}" /> instead of a flag and an output parameter.
/// </remarks>
public delegate bool TryGet<in T, TResult>(T value, out TResult result);

/// <summary>
///     A method which produces a value from two inputs if it can, reporting whether it did
///     through its return value and handing the value back through an output parameter.
/// </summary>
/// <typeparam name="T1">The type of the first input.</typeparam>
/// <typeparam name="T2">The type of the second input.</typeparam>
/// <typeparam name="TResult">The type of the value produced.</typeparam>
/// <param name="value1">The first input.</param>
/// <param name="value2">The second input.</param>
/// <param name="result">
///     Set to the value produced when this returns <see langword="true" />, and left at the
///     default for its type otherwise.
/// </param>
/// <returns>
///     <see langword="true" /> if a value was produced, and <see langword="false" /> otherwise.
/// </returns>
public delegate bool TryGet<in T1, in T2, TResult>(T1 value1, T2 value2, out TResult result);

/// <summary>
///     A method which produces a value from three inputs if it can, reporting whether it did
///     through its return value and handing the value back through an output parameter.
/// </summary>
/// <typeparam name="T1">The type of the first input.</typeparam>
/// <typeparam name="T2">The type of the second input.</typeparam>
/// <typeparam name="T3">The type of the third input.</typeparam>
/// <typeparam name="TResult">The type of the value produced.</typeparam>
/// <param name="value1">The first input.</param>
/// <param name="value2">The second input.</param>
/// <param name="value3">The third input.</param>
/// <param name="result">
///     Set to the value produced when this returns <see langword="true" />, and left at the
///     default for its type otherwise.
/// </param>
/// <returns>
///     <see langword="true" /> if a value was produced, and <see langword="false" /> otherwise.
/// </returns>
/// <remarks>
///     <see cref="int.TryParse(string,System.Globalization.NumberStyles,System.IFormatProvider,out int)" />
///     has this shape.
/// </remarks>
public delegate bool TryGet<in T1, in T2, in T3, TResult>(T1 value1, T2 value2, T3 value3, out TResult result);