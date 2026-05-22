// Polyfill required for C# 9+ record types and init-only properties on netstandard2.0.
// The runtime type System.Runtime.CompilerServices.IsExternalInit does not exist in
// netstandard2.0, so the compiler needs this stub to emit valid IL.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
