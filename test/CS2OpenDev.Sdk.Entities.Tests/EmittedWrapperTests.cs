using System.Numerics;
using CS2OpenDev.Sdk.Entities;

namespace CS2OpenDev.Sdk.Entities.Tests;

// Verification for the emitted wrappers, in the order it gets harder: the
// manifests are well-formed, the wrappers agree with the manifests, and the whole
// thing composes over a reader and a world.
//
// All of it runs against DictionaryEntityReader with no demo bytes and no parser,
// which is exactly what the reference reader was shipped for. A failure here is a
// fault in what this repo emits rather than in anyone's runtime.
public class EmittedWrapperTests
{
    // ── The manifests ────────────────────────────────────────────────────────

    /// <summary>Every emitted binding satisfies the contract's own structural rules.</summary>
    [Test]
    public async Task EveryBinding_Conforms()
    {
        Exception? thrown = null;
        try
        {
            BindingConformance.ThrowIfInvalid(EntityWrapperRegistry.Bindings);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        await Assert.That(thrown).IsNull();
        await Assert.That(EntityWrapperRegistry.Bindings.Count).IsEqualTo(58);
    }

    /// <summary>The registry pins the curated state it was emitted from, so a runtime can detect skew at startup.</summary>
    [Test]
    public async Task Registry_CarriesTheCurationIdentity()
    {
        await Assert.That(EntityWrapperRegistry.LensHash).StartsWith("sha256:");
        await Assert.That(EntityWrapperRegistry.SchemaBuild).IsNotEmpty();
    }

    /// <summary>Canonical paths are dense, ordinal-sorted and duplicate-free — the property the wrappers' private ordinals rely on.</summary>
    [Test]
    public async Task CanonicalPaths_AreOrdinalSorted()
    {
        foreach (EntityClassBinding b in EntityWrapperRegistry.Bindings)
        {
            string[] sorted = b.CanonicalPaths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            await Assert.That(b.CanonicalPaths).IsEquivalentTo(sorted);
        }
    }

    /// <summary>An alias never shadows a live field, and always targets one — the case that only shows up on demos recorded before a rename.</summary>
    [Test]
    public async Task Aliases_ResolveAndDoNotShadow()
    {
        foreach (EntityClassBinding b in EntityWrapperRegistry.Bindings)
        {
            foreach ((string alias, string target) in b.Aliases)
            {
                await Assert.That(b.CanonicalPaths).Contains(target);
                await Assert.That(b.CanonicalPaths.Contains(alias)).IsFalse();
            }
        }
    }

    // ── The factory ──────────────────────────────────────────────────────────

    /// <summary>A known class constructs its wrapper; an unknown one yields null rather than throwing.</summary>
    [Test]
    public async Task Create_BuildsKnownClassesAndDeclinesUnknownOnes()
    {
        EntityClassBinding pawn = Binding("CCSPlayerPawn");
        EntityWrapper? w = EntityWrapperRegistry.Create(
            "CCSPlayerPawn", Reader(pawn, []), new EmptyWorld());

        await Assert.That(w).IsTypeOf<CSPlayerPawn>();
        await Assert.That(EntityWrapperRegistry.Create("CNotAThing", Reader(pawn, []), new EmptyWorld())).IsNull();
    }

    /// <summary>Abstract bases have a wrapper type but no factory case, because a factory for them would never fire.</summary>
    [Test]
    [Arguments("CCSWeaponBaseShotgun")]
    [Arguments("CBaseCSGrenade")]
    public async Task Create_OmitsClassesThatNeverAppearLive(string engineClass)
    {
        EntityClassBinding b = Binding(engineClass);

        await Assert.That(EntityWrapperRegistry.Create(engineClass, Reader(b, []), new EmptyWorld())).IsNull();
        // The type still exists — it is a usable base and a Resolve<T> target.
        await Assert.That(EntityWrapperRegistry.Bindings.Any(x => x.EngineClass == engineClass)).IsTrue();
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    /// <summary>Properties read the field their manifest ordinal names — the invariant the private ordinal constants stake everything on.</summary>
    [Test]
    public async Task Properties_ReadTheFieldTheirOrdinalNames()
    {
        EntityClassBinding b = Binding("CCSPlayerPawn");
        CSPlayerPawn pawn = new(
            Reader(b, new Dictionary<string, object?>
            {
                ["m_iHealth"] = 87,
                ["m_ArmorValue"] = 42,
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin"] = new Vector3(1, 2, 3),
            }),
            new EmptyWorld());

        await Assert.That(pawn.Health).IsEqualTo(87);
        await Assert.That(pawn.ArmorValue).IsEqualTo(42);
        await Assert.That(pawn.Origin).IsEqualTo(new Vector3(1, 2, 3));
        await Assert.That(pawn.EngineClassName).IsEqualTo("CCSPlayerPawn");
    }

    /// <summary>The seen-aware field is nullable and reports absence as null, so a pawn that never sent it is not reported alive.</summary>
    [Test]
    public async Task LifeState_IsSeenAware()
    {
        EntityClassBinding b = Binding("CCSPlayerPawn");

        CSPlayerPawn absent = new(Reader(b, []), new EmptyWorld());
        await Assert.That(absent.LifeState).IsNull();

        CSPlayerPawn alive = new(
            Reader(b, new Dictionary<string, object?> { ["m_lifeState"] = 0 }), new EmptyWorld());
        await Assert.That(alive.LifeState).IsEqualTo(0);

        // Health takes the other policy: absent is 0, because 0 HP is not a state
        // anyone reads as meaningful.
        await Assert.That(absent.Health).IsEqualTo(0);
    }

    /// <summary>Origin is seen-aware on the three classes whose canonical is the quantized-vector struct: the wire carries the struct's leaves, never the parent path, so absent must read null rather than the world origin.</summary>
    [Test]
    [Arguments("CCSPlayerPawn")]
    [Arguments("CBaseCSGrenadeProjectile")]
    [Arguments("CPlantedC4")]
    public async Task Origin_IsSeenAware(string engineClass)
    {
        EntityClassBinding b = Binding(engineClass);

        // Absent is null, not (0,0,0). On a real GOTV demo absent is the normal
        // case — the parent path never materialises, only the struct's cell
        // leaves do (SDK#25, finding F3) — and a 0-default here presented that
        // as a plausible coordinate. Reflection because the three wrappers share
        // no base with an Origin member; the property's declared type is part of
        // what this test pins.
        EntityWrapper absent = EntityWrapperRegistry.Create(engineClass, Reader(b, []), new EmptyWorld())!;
        var origin = absent.GetType().GetProperty("Origin")!;

        await Assert.That(origin.PropertyType).IsEqualTo(typeof(Vector3?));
        await Assert.That(origin.GetValue(absent)).IsNull();

        // A runtime that stores a value under the canonical path — fabricated
        // state, or reconstructed world coordinates — still serves it through
        // the property. Nullability changes what absence reads as, not what
        // presence does.
        EntityWrapper present = EntityWrapperRegistry.Create(
            engineClass,
            Reader(b, new Dictionary<string, object?>
            {
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin"] = new Vector3(4, 5, 6),
            }),
            new EmptyWorld())!;

        await Assert.That(origin.GetValue(present)).IsEqualTo(new Vector3(4, 5, 6));
    }

    /// <summary>A field declared as a struct but carrying uint64 on the wire reads as ulong — the derivation that replaced the hand-curated wide-int table.</summary>
    [Test]
    public async Task Buttons_ReadsWideDespiteItsDeclaredType()
    {
        const ulong pressed = 0x0000_0001_0000_0005UL;
        CSPlayerPawn pawn = new(
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?>
            {
                ["m_pMovementServices.m_nButtons"] = pressed,
            }),
            new EmptyWorld());

        await Assert.That(pawn.Buttons).IsEqualTo(pressed);
    }

    // ── Handles ──────────────────────────────────────────────────────────────

    /// <summary>A handle crosses raw and resolves through the world, with a serial in the high bits so mask policy is genuinely exercised.</summary>
    [Test]
    public async Task Handle_CrossesRawAndResolves()
    {
        // Serial 1 << 17 | slot 0x1234 — a handle that is not slot-0 luck.
        const uint handle = 0x0002_1234u;

        EntityClassBinding weaponBinding = Binding("CBasePlayerWeapon");
        BasePlayerWeapon weapon = new(
            Reader(weaponBinding, new Dictionary<string, object?> { ["m_iClip1"] = 30 }),
            new EmptyWorld());

        TableWorld world = new();
        world.Add(handle, weapon);

        CSPlayerPawn pawn = new(
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?>
            {
                ["m_pWeaponServices.m_hActiveWeapon"] = handle,
            }),
            world);

        await Assert.That(pawn.ActiveWeaponHandle).IsEqualTo(handle);
        await Assert.That(pawn.ActiveWeapon).IsNotNull();

        // The companion is EntityWrapper? because its target has curated
        // descendants, so a consumer narrows to the class it expects. That cast is
        // the cost of the companion working at all on real data — see
        // WeaponCompanions_AreTypedForWhatARuntimeActuallyReturns.
        await Assert.That(((BasePlayerWeapon)pawn.ActiveWeapon!).Clip1).IsEqualTo(30);
    }

    /// <summary>An unresolvable handle yields null rather than throwing.</summary>
    [Test]
    public async Task Handle_UnresolvableIsNull()
    {
        CSPlayerPawn pawn = new(
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?>
            {
                ["m_pWeaponServices.m_hActiveWeapon"] = 0xFFFFFFFFu,
            }),
            new TableWorld());

        await Assert.That(pawn.ActiveWeaponHandle).IsEqualTo(0xFFFFFFFFu);
        await Assert.That(pawn.ActiveWeapon).IsNull();
    }

    /// <summary>Every ordinal the manifest calls a handle is one the wrapper reads as a handle.</summary>
    [Test]
    public async Task HandleOrdinals_NameHandleFields()
    {
        foreach (EntityClassBinding b in EntityWrapperRegistry.Bindings)
        {
            foreach (int ordinal in b.HandleOrdinals)
            {
                await Assert.That(ordinal).IsGreaterThanOrEqualTo(0);
                await Assert.That(ordinal).IsLessThan(b.CanonicalPaths.Count);
                // Handle fields are the m_h* family by engine convention.
                await Assert.That(b.CanonicalPaths[ordinal].Split('.')[^1]).StartsWith("m_h");
            }
        }
    }

    // ── Companion typing (SDK#25 F1 and F2, both found over a real demo) ──────

    /// <summary>A companion whose target has curated descendants is typed EntityWrapper, because a runtime dispatches the handle to the concrete class and a narrower fold could never succeed.</summary>
    [Test]
    public async Task WeaponCompanions_AreTypedForWhatARuntimeActuallyReturns()
    {
        const uint handle = 0x0002_1234u;

        // A live smoke grenade is what m_hActiveWeapon points at in practice, and
        // a registry-faithful runtime resolves it to that class's own wrapper.
        EntityWrapper smoke = EntityWrapperRegistry.Create(
            "CSmokeGrenade", Reader(Binding("CSmokeGrenade"), []), new EmptyWorld())!;

        TableWorld world = new();
        world.Add(handle, smoke);

        CSPlayerPawn pawn = new(
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?>
            {
                ["m_pWeaponServices.m_hActiveWeapon"] = handle,
            }),
            world);

        // Before the fix this was typed BasePlayerWeapon? and read null here, because
        // the emitted types are flat and SmokeGrenade is not a BasePlayerWeapon.
        await Assert.That(pawn.ActiveWeapon).IsNotNull();
        await Assert.That(pawn.ActiveWeapon).IsTypeOf<SmokeGrenade>();
    }

    /// <summary>A handle declared against the client-side spelling of a curated class still gets its companion — controller to pawn is the most-used traversal there is.</summary>
    [Test]
    public async Task ClientSpelledHandle_StillGetsItsCompanion()
    {
        const uint handle = 0x0003_0042u;

        EntityWrapper target = EntityWrapperRegistry.Create(
            "CCSPlayerPawn",
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?> { ["m_iHealth"] = 55 }),
            new EmptyWorld())!;

        TableWorld world = new();
        world.Add(handle, target);

        // m_hPlayerPawn declares CHandle< C_CSPlayerPawn > — the client spelling of
        // the curated, server-named CCSPlayerPawn.
        CSPlayerController controller = new(
            Reader(Binding("CCSPlayerController"), new Dictionary<string, object?>
            {
                ["m_hPlayerPawn"] = handle,
            }),
            world);

        await Assert.That(controller.PlayerPawn).IsNotNull();
        await Assert.That(controller.PlayerPawn!.Health).IsEqualTo(55);
    }

    /// <summary>A target with no curated descendants keeps its specific type, so the fix does not flatten companions that were already correct.</summary>
    [Test]
    public async Task CompanionsWithNoCuratedDescendants_StayTyped()
    {
        const uint handle = 0x0004_0007u;

        EntityWrapper defuser = EntityWrapperRegistry.Create(
            "CCSPlayerPawn", Reader(Binding("CCSPlayerPawn"), []), new EmptyWorld())!;

        TableWorld world = new();
        world.Add(handle, defuser);

        PlantedC4 bomb = new(
            Reader(Binding("CPlantedC4"), new Dictionary<string, object?>
            {
                ["m_hBombDefuser"] = handle,
            }),
            world);

        // Statically CSPlayerPawn?, not EntityWrapper? — the type still carries information.
        CSPlayerPawn? typed = bomb.BombDefuser;
        await Assert.That(typed).IsNotNull();
    }

    // ── Support ──────────────────────────────────────────────────────────────

    private static EntityClassBinding Binding(string engineClass) =>
        EntityWrapperRegistry.Bindings.Single(b => b.EngineClass == engineClass);

    private static DictionaryEntityReader Reader(
        EntityClassBinding binding, Dictionary<string, object?> values) => new(binding, values);

    private sealed class EmptyWorld : IEntityWorld
    {
        public T? Resolve<T>(uint rawHandle) where T : EntityWrapper => null;
    }

    private sealed class TableWorld : IEntityWorld
    {
        private readonly Dictionary<uint, EntityWrapper> _entities = [];

        public void Add(uint handle, EntityWrapper e) => _entities[handle] = e;

        public T? Resolve<T>(uint rawHandle) where T : EntityWrapper =>
            _entities.TryGetValue(rawHandle, out EntityWrapper? e) ? e as T : null;
    }
}
