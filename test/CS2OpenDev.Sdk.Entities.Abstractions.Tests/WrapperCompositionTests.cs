using System.Numerics;
using CS2OpenDev.Sdk.Entities;

namespace CS2OpenDev.Sdk.Entities.Abstractions.Tests;

// Proves the three pieces compose into the thing they exist for: a typed wrapper
// reading fields and resolving a handle to another wrapper, with no parser involved.
//
// The wrappers below are hand-written in exactly the shape the emitter will produce
// — ordinal constants private, one expression per property, two read policies, a raw
// handle beside its resolved companion. If the emitted shape has to change, this is
// where it should hurt first.
public class WrapperCompositionTests
{
    private const string Origin = "m_CBodyComponent.m_pSceneNode.m_vecOrigin";

    // ── What the emitter would generate ───────────────────────────────────────

    private sealed class BasePlayerWeapon(IEntityFieldReader reader, IEntityWorld world)
        : EntityWrapper(reader, world)
    {
        [SchemaFieldVersion("genesis")]
        public int Clip1 => Reader.TryReadInt32(Ord.Clip1, out int v) ? v : 0;

        private static class Ord
        {
            internal const int Clip1 = 0;
        }
    }

    private sealed class CSPlayerPawn(IEntityFieldReader reader, IEntityWorld world)
        : EntityWrapper(reader, world)
    {
        /// <summary>0-default policy: absent reads as 0, which is harmless for health.</summary>
        [SchemaFieldVersion("genesis")]
        public int Health => Reader.TryReadInt32(Ord.Health, out int v) ? v : 0;

        /// <summary>Seen-aware policy: 0 means LIFE_ALIVE, so absent must not be 0.</summary>
        [SchemaFieldVersion("genesis")]
        public int? LifeState => Reader.TryReadInt32(Ord.LifeState, out int v) ? v : null;

        [SchemaFieldVersion("genesis")]
        public Vector3? Origin => Reader.TryReadVector3(Ord.Origin, out Vector3 v) ? v : null;

        [SchemaFieldVersion("genesis")]
        public uint ActiveWeaponHandle =>
            Reader.TryReadEntityHandle(Ord.ActiveWeapon, out uint h) ? h : 0u;

        public BasePlayerWeapon? ActiveWeapon => World.Resolve<BasePlayerWeapon>(ActiveWeaponHandle);

        private static class Ord
        {
            internal const int Health = 0;
            internal const int LifeState = 1;
            internal const int Origin = 2;
            internal const int ActiveWeapon = 3;
        }
    }

    // ── A world with a handle table, standing in for a runtime ────────────────

    private sealed class FakeWorld : IEntityWorld
    {
        private readonly Dictionary<uint, EntityWrapper> _entities = [];

        public void Add(uint handle, EntityWrapper entity) => _entities[handle] = entity;

        public T? Resolve<T>(uint rawHandle) where T : EntityWrapper =>
            _entities.TryGetValue(rawHandle, out EntityWrapper? e) ? e as T : null;
    }

    private static EntityClassBinding PawnBinding() => new(
        EngineClass: "CCSPlayerPawn",
        NetName: "CSPlayerPawn",
        CanonicalPaths: ["m_iHealth", "m_lifeState", Origin, "m_pWeaponServices.m_hActiveWeapon"],
        Aliases: new Dictionary<string, string> { ["m_vecOrigin"] = Origin },
        HandleOrdinals: [3]);

    private static EntityClassBinding WeaponBinding() => new(
        EngineClass: "CBasePlayerWeapon",
        NetName: "BasePlayerWeapon",
        CanonicalPaths: ["m_iClip1"],
        Aliases: new Dictionary<string, string>(),
        HandleOrdinals: []);

    // ── The tests ─────────────────────────────────────────────────────────────

    /// <summary>A wrapper reads its fields through the seam with no parser present.</summary>
    [Test]
    public async Task Wrapper_ReadsFieldsThroughTheSeam()
    {
        FakeWorld world = new();
        CSPlayerPawn pawn = new(
            new DictionaryEntityReader(PawnBinding(), new Dictionary<string, object?>
            {
                ["m_iHealth"] = 87,
                ["m_lifeState"] = 0,
                [Origin] = new Vector3(1, 2, 3),
            }),
            world);

        await Assert.That(pawn.Health).IsEqualTo(87);
        await Assert.That(pawn.LifeState).IsEqualTo(0);
        await Assert.That(pawn.Origin).IsEqualTo(new Vector3(1, 2, 3));
        await Assert.That(pawn.EngineClassName).IsEqualTo("CCSPlayerPawn");
    }

    /// <summary>The two read policies differ where it counts: an unsent lifeState is null, an unsent health is 0.</summary>
    [Test]
    public async Task ReadPolicies_DistinguishAbsentFromZero()
    {
        CSPlayerPawn pawn = new(
            new DictionaryEntityReader(PawnBinding(), new Dictionary<string, object?>()),
            new FakeWorld());

        await Assert.That(pawn.Health).IsEqualTo(0);      // default-when-absent
        await Assert.That(pawn.LifeState).IsNull();       // null-when-absent
    }

    /// <summary>A handle resolves to a live wrapper of the requested type.</summary>
    [Test]
    public async Task Handle_ResolvesToAnotherWrapper()
    {
        const uint weaponHandle = 0x0002_1234u;
        FakeWorld world = new();
        BasePlayerWeapon weapon = new(
            new DictionaryEntityReader(WeaponBinding(), new Dictionary<string, object?> { ["m_iClip1"] = 30 }),
            world);
        world.Add(weaponHandle, weapon);

        CSPlayerPawn pawn = new(
            new DictionaryEntityReader(PawnBinding(), new Dictionary<string, object?>
            {
                ["m_pWeaponServices.m_hActiveWeapon"] = weaponHandle,
            }),
            world);

        await Assert.That(pawn.ActiveWeaponHandle).IsEqualTo(weaponHandle);
        await Assert.That(pawn.ActiveWeapon).IsNotNull();
        await Assert.That(pawn.ActiveWeapon!.Clip1).IsEqualTo(30);
    }

    /// <summary>An unresolvable handle yields null rather than throwing — stale, empty and wrong-type all collapse to the same answer.</summary>
    [Test]
    public async Task UnresolvableHandle_YieldsNull()
    {
        CSPlayerPawn pawn = new(
            new DictionaryEntityReader(PawnBinding(), new Dictionary<string, object?>
            {
                ["m_pWeaponServices.m_hActiveWeapon"] = 0xDEAD_BEEFu,
            }),
            new FakeWorld());

        await Assert.That(pawn.ActiveWeapon).IsNull();
    }

    /// <summary>The string indexer reaches a field no property covers, including through an alias.</summary>
    [Test]
    public async Task Indexer_ReachesUncoveredFieldsAndHistoricalSpellings()
    {
        CSPlayerPawn pawn = new(
            new DictionaryEntityReader(PawnBinding(), new Dictionary<string, object?>
            {
                [Origin] = new Vector3(9, 9, 9),
            }),
            new FakeWorld());

        await Assert.That(pawn[Origin]).IsEqualTo(new Vector3(9, 9, 9));
        await Assert.That(pawn["m_vecOrigin"]).IsEqualTo(new Vector3(9, 9, 9));
        await Assert.That(pawn["m_notAField"]).IsNull();
    }

    /// <summary>The bindings the emitter would ship pass conformance, and the pawn's handle ordinal really is the handle field.</summary>
    [Test]
    public async Task EmittedShapeBindings_Conform()
    {
        EntityClassBinding pawn = PawnBinding();

        await Assert.That(BindingConformance.Validate(pawn)).IsEmpty();
        await Assert.That(BindingConformance.Validate(WeaponBinding())).IsEmpty();

        // The manifest's HandleOrdinals is what a runtime walks the reference graph
        // by, so it has to name the field the wrapper actually reads as a handle.
        await Assert.That(pawn.CanonicalPaths[pawn.HandleOrdinals.Single()])
            .IsEqualTo("m_pWeaponServices.m_hActiveWeapon");
    }
}
