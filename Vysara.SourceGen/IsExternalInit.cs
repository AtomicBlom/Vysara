// ReSharper disable once CheckNamespace
// This needs to be in this namespace to be picked up by the compiler, but it is internal so it never escapes the analyzer assembly.
namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill enabling <c>init</c>-only setters (and therefore positional records) on the netstandard2.0 analyzer
/// target, where this type is not part of the framework. Internal so it never escapes the analyzer assembly.
/// </summary>
internal static class IsExternalInit;
