#region

using CS2_OpenDev.Sdk.Generator.Tests.Support;
using CS2SchemaGen.Models;

#endregion

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 3 — end-to-end tests that drive ModuleEmitter.EmitAll via a CapturingSink.
//
// These tests exercise the orchestration in ModuleEmitter — module grouping,
// conflict propagation, stub emission, naming, namespace plumbing — that the
// pure unit tests in Tier 1 / 2 don't reach.
//
// Every generator run invokes ModuleEmitter.EmitAll which writes to the static
// TypeMapper._nameMap. We share the "NameMap" parallelism key with
// TypeMapperTests and ClassEmitterTests so all writers to that static field
// serialize globally — see TypeMapper.SetNameMap for the underlying state.

[NotInParallel("NameMap")]
public class GeneratorPipelineTests
{
    // ── Attributes file content ──────────────────────────────────────────────

    /// <summary>
    ///     Pins the full surface of <c>CS2Schema.Attributes.g.cs</c>: <c>NativeName</c> (class+property),
    ///     <c>NativeOffset</c> (property), <c>NativeSize</c> (class, Q2), and <c>NativeMetadata</c> with
    ///     <c>AllowMultiple = true</c> (CE-2/EE-1).
    /// </summary>
    [Test]
    public async Task Generator_AttributesFile_DefinesAllNativeAttributes()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""{ "classes": [], "enums": [] }""");
        string attr = result.Files["Attributes"];
        // Bare `Attribute` (not `System.Attribute`) — ImplicitUsings on the SDK csproj
        // makes `using System;` redundant, so the formatter elides the fully-qualified form.
        await Assert.That(attr).Contains("public sealed class NativeNameAttribute : Attribute");
        await Assert.That(attr).Contains("public sealed class NativeOffsetAttribute : Attribute");
        await Assert.That(attr).Contains("AttributeUsage(AttributeTargets.Property)");
        // Q2: [NativeSize(N)] for class-level size documentation.
        await Assert.That(attr).Contains("public sealed class NativeSizeAttribute : Attribute");
        // CE-2 / EE-1: [NativeMetadata(name)] and [NativeMetadata(name, value)] round-trip
        // every schema metadata entry; AllowMultiple lets us stack them on a single member.
        await Assert.That(attr).Contains("public sealed class NativeMetadataAttribute : Attribute");
        await Assert.That(attr).Contains("AllowMultiple = true");
    }

    // ── B1 end-to-end: compound Hungarian stripped through the pipeline ──────

    /// <summary>
    ///     B1 end-to-end: a field named <c>m_iszPlayerName</c> in the schema reaches the generated property as
    ///     <c>public string PlayerName</c> (not <c>IszPlayerName</c>).
    /// </summary>
    [Test]
    public async Task Generator_CompoundHungarian_EndToEnd_StripsToCleanName()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{
                                                                     "name": "CFoo", "module": "client",
                                                                     "fields": [{ "name": "m_iszPlayerName", "offset": 0,
                                                                       "type": { "category": "atomic", "name": "CUtlString" } }]
                                                                   }]
                                                                 }
                                                                 """);

        string source = result.GetModuleSource("client");
        await Assert.That(source).Contains("public string PlayerName");
        await Assert.That(source).DoesNotContain("IszPlayerName");
    }

    // ── Name-map collision disambiguation ────────────────────────────────────
    //
    // When two C++ names map to the same desired C# name (because a CA1711 suffix
    // strip flattens `CFooQueue` onto `CFoo`), the "natural" mapping — the class
    // whose desired C# name equals its raw C++ name — must win the un-suffixed
    // identifier regardless of schema declaration order. The transformed class
    // falls back to its sanitized raw name (`CFooQueue`), NOT an ordinal suffix
    // like `CFoo2`. This is the regression case for `CHintMessage` /
    // `CHintMessageQueue` in the real schema, where the loser appeared first.

    /// <summary>
    ///     Two-pass name map: when CA1711 strips <c>CFooQueue</c> → <c>CFoo</c>, the natural <c>CFoo</c> wins the
    ///     un-suffixed name regardless of schema declaration order — the transformed class falls back to its sanitized raw
    ///     name, never to <c>CFoo2</c>.
    /// </summary>
    [Test]
    public async Task Generator_CsNameCollision_NaturalMappingWins_RegardlessOfSchemaOrder()
    {
        // CFooQueue is declared FIRST (the failing order before the two-pass fix).
        // Without "natural wins", schema order would let CFooQueue steal the bare
        // name and force CFoo onto an ordinal suffix.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CFooQueue", "module": "client",
                                                                       "fields": [{ "name": "m_x", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] },
                                                                     { "name": "CFoo", "module": "client",
                                                                       "fields": [{ "name": "m_x", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] }
                                                                   ]
                                                                 }
                                                                 """);

        string client = result.GetModuleSource("client");
        await Assert.That(client).Contains("public partial class CFoo\n");
        await Assert.That(client).Contains("public partial class CFooQueue\n");
        // The natural CFoo wins; CFooQueue must NOT have been forced onto an
        // ordinal suffix like `CFoo2`.
        await Assert.That(client).DoesNotContain("public partial class CFoo2");
    }

    // ── F1: reverse-lookup SchemaNames table ─────────────────────────────────

    /// <summary>
    ///     F1: emits <c>CS2Schema.SchemaNames.g.cs</c> with a <c>const string</c> per property name pointing back to its
    ///     raw C++ field name (e.g. <c>SchemaNames.CFoo.Health = "m_iHealth"</c>).
    /// </summary>
    [Test]
    public async Task Generator_EmitsSchemaNamesReverseLookupTable()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{
                                                                     "name": "CFoo", "module": "client",
                                                                     "fields": [{ "name": "m_iHealth", "offset": 0,
                                                                       "type": { "category": "builtin", "name": "int32" } }]
                                                                   }]
                                                                 }
                                                                 """);

        await Assert.That(result.Files).ContainsKey("SchemaNames");
        string source = result.Files["SchemaNames"];
        await Assert.That(source).Contains("public static class SchemaNames");
        await Assert.That(source).Contains("public const string Health = \"m_iHealth\";");
    }

    // ── B3 end-to-end: char arrays come through the whole pipeline as string ─

    /// <summary>
    ///     B3 end-to-end: a <c>char[N]</c> field in <c>schemas.json</c> reaches the generated property as
    ///     <c>public string Name</c> (not <c>sbyte[]</c>).
    /// </summary>
    [Test]
    public async Task Generator_FixedCharArray_EndToEnd_ProjectsAsString()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{
                                                                     "name": "CFoo", "module": "client",
                                                                     "fields": [{
                                                                       "name": "m_szName", "offset": 0,
                                                                       "type": { "category": "fixed_array", "count": 18,
                                                                                 "inner": { "category": "builtin", "name": "char" } }
                                                                     }]
                                                                   }]
                                                                 }
                                                                 """);

        string source = result.GetModuleSource("client");
        await Assert.That(source).Contains("public string Name");
        await Assert.That(source).DoesNotContain("public sbyte[] Name");
    }

    // ── Module classification: identical-fingerprint duplicates → Common ─────

    /// <summary>
    ///     Classification: same-name same-fingerprint classes across modules dedupe into the <c>shared</c> (Common) file
    ///     rather than being emitted per-module.
    /// </summary>
    [Test]
    public async Task Generator_IdenticalClassInTwoModules_EmittedOnceInCommon()
    {
        // Same name, same parents, same field names → "full duplicate".
        // Goes into the shared (Common) file, not duplicated per-module.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CShared", "module": "client",
                                                                       "fields": [{ "name": "m_x", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] },
                                                                     { "name": "CShared", "module": "server",
                                                                       "fields": [{ "name": "m_x", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] }
                                                                   ]
                                                                 }
                                                                 """);

        await Assert.That(result.HasModule("shared")).IsTrue();
        await Assert.That(result.GetModuleSource("shared")).Contains("public partial class CShared");
        // Per-module files for client/server may still exist for unrelated reasons, but
        // they must NOT contain a duplicate CShared declaration.
        if (result.HasModule("client"))
        {
            await Assert.That(result.GetModuleSource("client")).DoesNotContain("public partial class CShared");
        }
    }

    /// <summary>
    ///     Schema parsing throws on malformed JSON. The CLI host catches this and exits 1 with a
    ///     readable message; this test pins that the parser itself surfaces the failure.
    /// </summary>
    [Test]
    public async Task SchemaModel_Parse_MalformedJson_Throws()
    {
        Exception? caught = null;
        try
        {
            SchemaModel.Parse("{ this is not valid json");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    /// <summary>
    ///     An empty schema document still emits the infrastructure files (Attributes, Synthetics,
    ///     SchemaNames). Stubs is omitted when nothing references an undeclared type.
    /// </summary>
    [Test]
    public async Task Generator_EmptySchema_EmitsInfrastructureFilesOnly()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""{ "classes": [], "enums": [] }""");

        await Assert.That(result.Files).ContainsKey("Attributes");
        await Assert.That(result.Files).ContainsKey("Synthetics");
        await Assert.That(result.Files).ContainsKey("SchemaNames");
        await Assert.That(result.Files.ContainsKey("Stubs")).IsFalse();
    }

    // ── Stubs file presence ──────────────────────────────────────────────────

    /// <summary>
    ///     When every referenced type is declared in the schema, no <c>CS2Schema.Stubs.g.cs</c> is emitted (empty stub
    ///     files would be noise).
    /// </summary>
    [Test]
    public async Task Generator_NoUndeclaredReferences_OmitsStubsFile()
    {
        // If every referenced type is declared in the schema, no stub file is needed.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{
                                                                     "name": "CFoo", "module": "client",
                                                                     "fields": [{ "name": "m_x", "offset": 0,
                                                                       "type": { "category": "builtin", "name": "int32" } }]
                                                                   }]
                                                                 }
                                                                 """);
        await Assert.That(result.Files.ContainsKey("Stubs")).IsFalse();
    }

    // ── Per-file using-directive minimization ────────────────────────────────

    /// <summary>
    ///     Per-file <c>using</c> minimisation: each module file imports only the cross-module namespaces it actually
    ///     references, never every namespace in the SDK.
    /// </summary>
    [Test]
    public async Task Generator_PerFileUsings_OnlyIncludesNamespacesActuallyReferenced()
    {
        // CFoo in module "a" references CBar in module "b". So a.cs needs `using CS2Schema.B;`.
        // CIsolated in module "c" references no cross-module types. So c.cs must NOT have
        // `using CS2Schema.A;` or `using CS2Schema.B;`.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CFoo", "module": "a",
                                                                       "fields": [{ "name": "m_bar", "offset": 0,
                                                                         "type": { "category": "declared_class", "name": "CBar", "module": "b" } }] },
                                                                     { "name": "CBar", "module": "b", "fields": [] },
                                                                     { "name": "CIsolated", "module": "c", "fields": [] }
                                                                   ]
                                                                 }
                                                                 """);

        string aSrc = result.GetModuleSource("a");
        string cSrc = result.GetModuleSource("c");
        await Assert.That(aSrc).Contains("using CS2Schema.B;");
        await Assert.That(cSrc).DoesNotContain("using CS2Schema.A;");
        await Assert.That(cSrc).DoesNotContain("using CS2Schema.B;");
    }

    // ── ME-3: source-traceability stamp from schemas.json metadata ───────────

    /// <summary>
    ///     ME-3 / F3: top-level <c>revision</c> and <c>version_date</c> in <c>schemas.json</c> are stamped into the
    ///     header of every emitted <c>.g.cs</c> file for source traceability.
    /// </summary>
    [Test]
    public async Task Generator_PropagatesSchemaRevisionIntoGeneratedHeaders()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "build_id": 10641237,
                                                                   "version_date": "May 07 2026",
                                                                   "classes": [{ "name": "CFoo", "module": "client", "fields": [] }]
                                                                 }
                                                                 """);

        string source = result.GetModuleSource("client");
        await Assert.That(source).Contains("10641237");
        await Assert.That(source).Contains("May 07 2026");
    }

    // ── Forward-declared stubs ───────────────────────────────────────────────

    /// <summary>
    ///     An undeclared cross-module type referenced from a field is emitted as an empty <c>partial class</c> stub in
    ///     <c>CS2Schema.Stubs.g.cs</c>.
    /// </summary>
    [Test]
    public async Task Generator_ReferencedUndeclaredType_EmitsAsStub()
    {
        // CFoo references CExternalUnknown via a field, but CExternalUnknown is
        // never declared anywhere. The generator emits it as an empty stub.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{
                                                                     "name": "CFoo", "module": "client",
                                                                     "fields": [{ "name": "m_external", "offset": 0,
                                                                       "type": { "category": "declared_class", "name": "CExternalUnknown", "module": "tool" } }]
                                                                   }]
                                                                 }
                                                                 """);

        await Assert.That(result.Files).ContainsKey("Stubs");
        await Assert.That(result.Files["Stubs"]).Contains("public partial class CExternalUnknown");
    }

    // ── Namespace plumbing ───────────────────────────────────────────────────

    /// <summary>
    ///     EX-2: the <c>CS2SchemaGen_Namespace</c> MSBuild property overrides the default <c>CS2Schema</c> namespace in
    ///     emitted source.
    /// </summary>
    [Test]
    public async Task Generator_RespectsCustomNamespaceFromMSBuildProperty()
    {
        // Set the same property that the Demo csproj will set after Step 6 (EX-2).
        GeneratorHarness.RunResult result = GeneratorHarness.Run(
            """
            {
              "classes": [{ "name": "CFoo", "module": "client", "fields": [] }]
            }
            """,
            "CS2OpenSchema");

        string source = result.GetModuleSource("client");
        await Assert.That(source).Contains("namespace CS2OpenSchema.Client;");
        await Assert.That(source).DoesNotContain("namespace CS2Schema.Client;");
    }

    // ── Module classification: different fingerprint → per-module ────────────

    /// <summary>
    ///     Classification: same-name different-fingerprint classes keep their own per-module copies and no <c>shared</c>
    ///     file is emitted.
    /// </summary>
    [Test]
    public async Task Generator_SameNameDifferentFieldsAcrossModules_KeptPerModule()
    {
        // Same name, different field names → "genuine conflict". Each module
        // keeps its own copy; nothing in the shared file.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CDiverged", "module": "client",
                                                                       "fields": [{ "name": "m_client_field", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] },
                                                                     { "name": "CDiverged", "module": "server",
                                                                       "fields": [{ "name": "m_server_field", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] }
                                                                   ]
                                                                 }
                                                                 """);

        await Assert.That(result.HasModule("client")).IsTrue();
        await Assert.That(result.HasModule("server")).IsTrue();
        await Assert.That(result.GetModuleSource("client")).Contains("public partial class CDiverged");
        await Assert.That(result.GetModuleSource("server")).Contains("public partial class CDiverged");
        // Shared file shouldn't exist (no full duplicates anywhere).
        await Assert.That(result.HasModule("shared")).IsFalse();
    }

    /// <summary>
    ///     F1 shape check: <c>SchemaNames</c> nests one <c>public static class</c> per schema class — discriminates
    ///     against a flat-emit bug where every const ends up directly under <c>SchemaNames</c>.
    /// </summary>
    [Test]
    public async Task Generator_SchemaNames_NestsOneStaticClassPerSchemaClass()
    {
        // Discriminating against a flat-emit bug: with two distinct schema classes
        // the file must contain two nested static classes under SchemaNames, each
        // carrying only its own consts. A flat implementation (everything under
        // SchemaNames directly) would pass the single-class test above but fail
        // here.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CFoo", "module": "client",
                                                                       "fields": [{ "name": "m_iHealth", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] },
                                                                     { "name": "CBar", "module": "client",
                                                                       "fields": [{ "name": "m_iAmmo", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] }
                                                                   ]
                                                                 }
                                                                 """);

        string source = result.Files["SchemaNames"];
        await Assert.That(source).Contains("public static class CFoo");
        await Assert.That(source).Contains("public static class CBar");
        await Assert.That(source).Contains("public const string Health = \"m_iHealth\";");
        await Assert.That(source).Contains("public const string Ammo = \"m_iAmmo\";");
    }

    // ── CE-1: stubs from `::`-replaced names should carry the original C++ name ─

    /// <summary>
    ///     CE-1: a stub whose original C++ name contained <c>::</c> gets sanitized to <c>Outer_Inner_Foo</c> in the
    ///     declaration but preserves the original via <c>[NativeName]</c>.
    /// </summary>
    [Test]
    public async Task Generator_ScopeOperatorStub_CarriesOriginalCppNameAttribute()
    {
        // A field references "Outer::Inner::Foo" via declared_class. The type is not
        // declared anywhere → stub emission. The stub's C# name is "Outer_Inner_Foo"
        // (via SanitizeName); the original "Outer::Inner::Foo" should be preserved
        // via [NativeName] for runtime interop.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{
                                                                     "name": "CFoo", "module": "client",
                                                                     "fields": [{ "name": "m_x", "offset": 0,
                                                                       "type": { "category": "declared_class", "name": "Outer::Inner::Foo", "module": "tool" } }]
                                                                   }]
                                                                 }
                                                                 """);

        string stubs = result.Files["Stubs"];
        await Assert.That(stubs).Contains("public partial class Outer_Inner_Foo");
        await Assert.That(stubs).Contains("[NativeName(\"Outer::Inner::Foo\")]");
    }

    // ── Conflict propagation: enum-typed field also triggers demotion ────────

    /// <summary>
    ///     ME-1 propagation extends to enum-typed fields: a shared class referencing a per-module-conflicted enum is
    ///     demoted out of <c>shared</c>.
    /// </summary>
    [Test]
    public async Task Generator_SharedClassReferencingConflictedEnum_DemotedToPerModule()
    {
        // EConflict has different members in client/server → genuine enum conflict.
        // CSafe is identical across both modules but its field type is EConflict, so
        // it cannot live in the shared file (ambiguous using-directive). Must be demoted.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CSafe", "module": "client",
                                                                       "fields": [{ "name": "m_state", "offset": 0,
                                                                         "type": { "category": "declared_enum", "name": "EConflict", "module": "client" } }] },
                                                                     { "name": "CSafe", "module": "server",
                                                                       "fields": [{ "name": "m_state", "offset": 0,
                                                                         "type": { "category": "declared_enum", "name": "EConflict", "module": "server" } }] }
                                                                   ],
                                                                   "enums": [
                                                                     { "name": "EConflict", "module": "client", "storage_size": 4,
                                                                       "members": [{ "name": "ClientOnly", "value": 1 }] },
                                                                     { "name": "EConflict", "module": "server", "storage_size": 4,
                                                                       "members": [{ "name": "ServerOnly", "value": 1 }] }
                                                                   ]
                                                                 }
                                                                 """);

        if (result.HasModule("shared"))
        {
            await Assert.That(result.GetModuleSource("shared")).DoesNotContain("public partial class CSafe");
        }

        await Assert.That(result.GetModuleSource("client")).Contains("public partial class CSafe");
        await Assert.That(result.GetModuleSource("server")).Contains("public partial class CSafe");
    }

    // ── Conflict propagation: shared classes referencing conflicted types ────

    /// <summary>
    ///     ME-1 propagation: an otherwise-shared class that references a per-module-conflicted class type gets demoted
    ///     out of <c>shared</c> to avoid ambiguous using-directives.
    /// </summary>
    [Test]
    public async Task Generator_SharedClassReferencingConflictedType_DemotedToPerModule()
    {
        // CSafe is identical across client/server → would go in shared.
        // But CSafe references CConflict, which has different fields per module.
        // The propagation loop must demote CSafe to per-module to avoid an
        // ambiguous using-directive in the shared file.
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CConflict", "module": "client",
                                                                       "fields": [{ "name": "m_client_field", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] },
                                                                     { "name": "CConflict", "module": "server",
                                                                       "fields": [{ "name": "m_server_field", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] },
                                                                     { "name": "CSafe", "module": "client",
                                                                       "fields": [{ "name": "m_ref", "offset": 0,
                                                                         "type": { "category": "declared_class", "name": "CConflict", "module": "client" } }] },
                                                                     { "name": "CSafe", "module": "server",
                                                                       "fields": [{ "name": "m_ref", "offset": 0,
                                                                         "type": { "category": "declared_class", "name": "CConflict", "module": "server" } }] }
                                                                   ]
                                                                 }
                                                                 """);

        // CSafe must NOT appear in shared — it would have an ambiguous CConflict reference.
        if (result.HasModule("shared"))
        {
            await Assert.That(result.GetModuleSource("shared")).DoesNotContain("public partial class CSafe");
        }

        await Assert.That(result.GetModuleSource("client")).Contains("public partial class CSafe");
        await Assert.That(result.GetModuleSource("server")).Contains("public partial class CSafe");
    }

    // ── Filename derivation ──────────────────────────────────────────────────

    /// <summary>
    ///     The on-disk filename is <c>CS2Schema.shared.g.cs</c> but the namespace segment is <c>Common</c> (CA1716 —
    ///     <c>Shared</c> collides with the VB keyword).
    /// </summary>
    [Test]
    public async Task Generator_SharedModule_FileIsNamedShared_AndNamespaceIsCommon()
    {
        // The on-disk filename hint is "CS2Schema.shared.g.cs" but the namespace
        // segment is "Common" (avoiding the CA1716 conflict with VB's "Shared").
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CDup", "module": "a",
                                                                       "fields": [{ "name": "m_x", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] },
                                                                     { "name": "CDup", "module": "b",
                                                                       "fields": [{ "name": "m_x", "offset": 0,
                                                                         "type": { "category": "builtin", "name": "int32" } }] }
                                                                   ]
                                                                 }
                                                                 """);

        await Assert.That(result.HasModule("shared")).IsTrue();
        await Assert.That(result.GetModuleSource("shared")).Contains("namespace CS2Schema.Common;");
    }
    // ── Baseline: single class produces a module file ────────────────────────

    /// <summary>
    ///     End-to-end: a single class in module <c>client</c> emits a per-class file under the <c>client</c> module with
    ///     the correct namespace and class declaration.
    /// </summary>
    [Test]
    public async Task Generator_SingleClass_EmitsModuleFile()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{ "name": "CFoo", "module": "client",
                                                                                 "fields": [{ "name": "m_iHealth", "offset": 0,
                                                                                   "type": { "category": "builtin", "name": "int32" } }] }]
                                                                 }
                                                                 """);

        await Assert.That(result.Files).ContainsKey("Client/CFoo");
        string source = result.GetModuleSource("client");
        await Assert.That(source).Contains("namespace CS2Schema.Client;");
        await Assert.That(source).Contains("public partial class CFoo");
    }

    // ── ME-2: deterministic output across runs ───────────────────────────────

    /// <summary>
    ///     ME-2 determinism: two generator runs over identical input produce byte-identical output across every emitted
    ///     file.
    /// </summary>
    [Test]
    public async Task Generator_TwoRuns_ProduceIdenticalOutput()
    {
        string schema = """
                        {
                          "classes": [
                            { "name": "CFoo", "module": "client",
                              "fields": [{ "name": "m_iHealth", "offset": 0,
                                "type": { "category": "builtin", "name": "int32" } }] },
                            { "name": "CBar", "module": "server", "fields": [] }
                          ],
                          "enums": [
                            { "name": "EState", "module": "client", "storage_size": 4,
                              "members": [{ "name": "A", "value": 0 }, { "name": "B", "value": 1 }] }
                          ]
                        }
                        """;

        GeneratorHarness.RunResult a = GeneratorHarness.Run(schema);
        GeneratorHarness.RunResult b = GeneratorHarness.Run(schema);

        await Assert.That(a.Files.Count).IsEqualTo(b.Files.Count);
        foreach (KeyValuePair<string, string> pair in a.Files)
        {
            await Assert.That(b.Files).ContainsKey(pair.Key);
            await Assert.That(b.Files[pair.Key]).IsEqualTo(pair.Value);
        }
    }

    /// <summary>Module names with underscores (<c>panorama_content</c>) PascalCase each segment in the C# namespace clause.</summary>
    [Test]
    public async Task Generator_UnderscoreModule_PascalCasesNamespaceSegment()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{ "name": "CFoo", "module": "panorama_content", "fields": [] }]
                                                                 }
                                                                 """);

        await Assert.That(result.HasModule("panorama_content")).IsTrue();
        await Assert.That(result.GetModuleSource("panorama_content"))
            .Contains("namespace CS2Schema.PanoramaContent;");
    }

    // ── TM-2: diagnostic for unknown atomics ─────────────────────────────────

    /// <summary>
    ///     TM-2: an unrecognised atomic name triggers a <c>CS2_GEN_003</c> info diagnostic so maintainers see new dumper
    ///     types instead of silently shipping stubs.
    /// </summary>
    [Test]
    public async Task Generator_UnknownAtomic_EmitsCS2GEN003Diagnostic()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [{
                                                                     "name": "CFoo", "module": "client",
                                                                     "fields": [{ "name": "m_mystery", "offset": 0,
                                                                       "type": { "category": "atomic", "name": "CFutureUnknownAtom" } }]
                                                                   }]
                                                                 }
                                                                 """);

        await Assert.That(result.Diagnostics).Contains(d => d.Id == "CS2_GEN_003");
    }

    // ── Q4 / sort stability: unsorted input → sorted output ──────────────────

    /// <summary>
    ///     Q4 sort stability: even when <c>schemas.json</c> lists classes in arbitrary order, the concatenation of
    ///     per-class files (in hint-name order) preserves alphabetical type ordering within a module.
    /// </summary>
    [Test]
    public async Task Generator_UnsortedClassInput_EmitsClassesAlphabeticallyWithinModule()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
                                                                 {
                                                                   "classes": [
                                                                     { "name": "CZebra",  "module": "client", "fields": [] },
                                                                     { "name": "CApple",  "module": "client", "fields": [] },
                                                                     { "name": "CMango",  "module": "client", "fields": [] }
                                                                   ]
                                                                 }
                                                                 """);

        // GetModuleSource concatenates per-class files in ordinal hint-name order, so
        // an alphabetised emit means CApple's file content precedes CMango's, which
        // precedes CZebra's in the joined string.
        string source = result.GetModuleSource("client");
        int apple = source.IndexOf("public partial class CApple", StringComparison.Ordinal);
        int mango = source.IndexOf("public partial class CMango", StringComparison.Ordinal);
        int zebra = source.IndexOf("public partial class CZebra", StringComparison.Ordinal);
        await Assert.That(apple).IsGreaterThan(0);
        await Assert.That(apple).IsLessThan(mango);
        await Assert.That(mango).IsLessThan(zebra);
    }

    /// <summary>
    ///     Emits <c>CS2Schema.Attributes.g.cs</c> defining <c>NativeNameAttribute</c> and <c>NativeOffsetAttribute</c>
    ///     whenever the generator runs against a non-null schema.
    /// </summary>
    [Test]
    public async Task Generator_WithSchema_EmitsAttributesFile()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""{ "classes": [], "enums": [] }""");
        await Assert.That(result.Files).ContainsKey("Attributes");
        await Assert.That(result.Files["Attributes"]).Contains("public sealed class NativeNameAttribute");
        await Assert.That(result.Files["Attributes"]).Contains("public sealed class NativeOffsetAttribute");
    }

    /// <summary>
    ///     Emits <c>CS2Schema.Synthetics.g.cs</c> with the built-in atom structs (Vector, etc.) whenever the schema is
    ///     non-null.
    /// </summary>
    [Test]
    public async Task Generator_WithSchema_EmitsSyntheticsFile()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""{ "classes": [], "enums": [] }""");
        await Assert.That(result.Files).ContainsKey("Synthetics");
        await Assert.That(result.Files["Synthetics"]).Contains("public readonly struct Vector");
    }

    // ── Schema 2.0 shapes that do not compile if passed through ──────────────
    //
    // Both of these produced a package that failed to build when the generator
    // was first pointed at a real 2.0 artifact, and neither is caught by "did
    // the regen succeed" — the generator exits 0 and emits invalid C#.

    /// <summary>
    ///     A 2.0 module name is a binary, and one of them is <c>!GlobalTypes</c>. Passed through it emitted
    ///     <c>namespace CS2OpenSchema.!GlobalTypes;</c> across 591 files — a syntax error, not a bad name.
    /// </summary>
    [Test]
    public async Task Generator_ModuleNameWithIllegalIdentifierChars_SanitisesNamespace()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
            {
              "classes": [{ "name": "CFoo", "module": "!GlobalTypes", "fields": [] }],
              "enums": []
            }
            """);

        string emitted = string.Join("\n", result.Files.Values);
        await Assert.That(emitted).DoesNotContain("namespace CS2Schema.!");
        await Assert.That(emitted).Contains("namespace CS2Schema.GlobalTypes");
    }

    /// <summary>
    ///     C# forbids a member named after its enclosing type (CS0542). Schema 2.0's <c>TagStatus</c> class carries
    ///     a field <c>m_TagStatus</c>, which broke both the class file and the SchemaNames table.
    /// </summary>
    [Test]
    public async Task Generator_FieldNamedAfterItsClass_IsRenamed()
    {
        GeneratorHarness.RunResult result = GeneratorHarness.Run("""
            {
              "classes": [{
                "name": "TagStatus", "module": "animgraphlib",
                "fields": [{ "name": "m_TagStatus", "offset": 0,
                  "type": { "category": "builtin", "name": "int32" } }]
              }],
              "enums": []
            }
            """);

        string emitted = string.Join("\n", result.Files.Values);
        // The property is renamed, and the native name is still recoverable.
        await Assert.That(emitted).Contains("TagStatusValue");
        await Assert.That(emitted).Contains("m_TagStatus");
        await Assert.That(emitted).DoesNotContain("int TagStatus { get; set; }");
    }
}
