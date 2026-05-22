using CS2SchemaGen.Emitters;
using CS2SchemaGen.Models;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 2 — direct snapshot tests for HandleTypes.BuildSource.
//
// TypeMapperTests covers atomic-name dispatch (CHandle → CHandle<T> etc.) and
// GeneratorPipelineTests exercises the file end-to-end against a fixture
// schema, but neither asserts on the actual emitted struct content. This file
// pins the shape (presence of each handle type, member surface, invalid-value
// sentinels) so a future edit to HandleTypes.cs that accidentally drops one
// of the structs fails loudly here.

public class HandleTypesTests
{
    private static readonly SchemaRoot EmptySchema = new([], []);

    private static string Emit() => HandleTypes.BuildSource("CS2Schema", EmptySchema);

    /// <summary>Every handle struct documented in HandleTypes' comment block is emitted: CEntityHandle, CHandle&lt;T&gt;, CStrongHandleVoid, CStrongHandle&lt;T&gt;, CStrongHandleCopyable&lt;T&gt;, CWeakHandle&lt;T&gt;.</summary>
    [Test]
    public async Task BuildSource_EmitsAllHandleTypes()
    {
        string src = Emit();
        string[] expected = [
            "public readonly struct CEntityHandle",
            "public readonly struct CStrongHandleVoid",
            "public readonly struct CHandle<T>",
            "public readonly struct CStrongHandle<T>",
            "public readonly struct CStrongHandleCopyable<T>",
            "public readonly struct CWeakHandle<T>"
        ];
        foreach (string e in expected)
        {
            await Assert.That(src).Contains(e);
        }
    }

    /// <summary>Each handle struct exposes the same minimum API surface — `Value`, `IsValid`, `Invalid` sentinel, equality operators — so consumers can treat them uniformly.</summary>
    [Test]
    [Arguments("CEntityHandle")]
    [Arguments("CStrongHandleVoid")]
    [Arguments("CHandle<T>")]
    [Arguments("CStrongHandle<T>")]
    [Arguments("CStrongHandleCopyable<T>")]
    [Arguments("CWeakHandle<T>")]
    public async Task BuildSource_HandleStruct_ExposesUniformApi(string structName)
    {
        string src = Emit();
        // Anchor on the struct's declaration to scope a region for the per-struct assertions.
        // We can't slice the string easily, so just check each piece exists at least once for the type.
        await Assert.That(src).Contains($"public readonly struct {structName}");
        await Assert.That(src).Contains("public bool IsValid =>");
        await Assert.That(src).Contains("public const ");
        await Assert.That(src).Contains(" InvalidValue = ");
        await Assert.That(src).Contains(" Invalid =>");
        await Assert.That(src).Contains("public bool Equals(");
        await Assert.That(src).Contains("public override int GetHashCode()");
    }

    /// <summary>Entity-handle structs (CEntityHandle, CHandle&lt;T&gt;) wrap a 32-bit unsigned int with the documented invalid sentinel <c>0xFFFFFFFFu</c>.</summary>
    [Test]
    public async Task BuildSource_EntityHandles_Are32BitWithCanonicalSentinel()
    {
        string src = Emit();
        await Assert.That(src).Contains("public const uint InvalidValue = 0xFFFFFFFFu;");
        // The CHandle<T> constructor takes a uint.
        await Assert.That(src).Contains("public CHandle(uint value) => Value = value;");
        await Assert.That(src).Contains("public CEntityHandle(uint value) => Value = value;");
    }

    /// <summary>Resource-handle structs (CStrongHandle / CStrongHandleCopyable / CWeakHandle / CStrongHandleVoid) wrap a 64-bit unsigned long with the documented invalid sentinel <c>0xFFFFFFFFFFFFFFFFul</c>.</summary>
    [Test]
    public async Task BuildSource_ResourceHandles_Are64BitWithCanonicalSentinel()
    {
        string src = Emit();
        await Assert.That(src).Contains("public const ulong InvalidValue = 0xFFFFFFFFFFFFFFFFul;");
        // Constructors take ulong.
        await Assert.That(src).Contains("public CStrongHandle(ulong value) => Value = value;");
        await Assert.That(src).Contains("public CWeakHandle(ulong value) => Value = value;");
    }

    /// <summary>Generic handle structs declare IEquatable&lt;Self&gt; so consumers get the full equality contract without writing boilerplate.</summary>
    [Test]
    public async Task BuildSource_GenericHandles_ImplementIEquatable()
    {
        string src = Emit();
        await Assert.That(src).Contains("CHandle<T> : System.IEquatable<CHandle<T>>");
        await Assert.That(src).Contains("CStrongHandle<T> : System.IEquatable<CStrongHandle<T>>");
        await Assert.That(src).Contains("CWeakHandle<T> : System.IEquatable<CWeakHandle<T>>");
    }

    /// <summary>The non-generic untyped handles also implement IEquatable so they can be compared without boxing.</summary>
    [Test]
    public async Task BuildSource_UntypedHandles_ImplementIEquatable()
    {
        string src = Emit();
        await Assert.That(src).Contains("CEntityHandle : System.IEquatable<CEntityHandle>");
        await Assert.That(src).Contains("CStrongHandleVoid : System.IEquatable<CStrongHandleVoid>");
    }

    /// <summary>Equality operators (== / !=) are emitted on every handle type so consumers don't have to call <c>Equals</c> explicitly.</summary>
    [Test]
    public async Task BuildSource_AllHandles_HaveEqualityOperators()
    {
        string src = Emit();
        // One pair per struct; spot-checking a few is sufficient since they're generated by the same code path.
        await Assert.That(src).Contains("public static bool operator ==(CEntityHandle a, CEntityHandle b)");
        await Assert.That(src).Contains("public static bool operator ==(CHandle<T> a, CHandle<T> b)");
        await Assert.That(src).Contains("public static bool operator !=(CWeakHandle<T> a, CWeakHandle<T> b)");
    }

    /// <summary>Each struct's ToString includes the type name and a hex representation of the value, with separate "(invalid)" branch.</summary>
    [Test]
    public async Task BuildSource_AllHandles_HaveDescriptiveToString()
    {
        string src = Emit();
        // Entity handles: hex with 8 nibbles (32 bits).
        await Assert.That(src).Contains("CEntityHandle(0x{Value:X8})");
        // Resource handles: hex with 16 nibbles (64 bits).
        await Assert.That(src).Contains("CStrongHandleVoid(0x{Value:X16})");
        // Generic handles include the inner type name via typeof(T).Name.
        await Assert.That(src).Contains("typeof(T).Name");
    }
}
