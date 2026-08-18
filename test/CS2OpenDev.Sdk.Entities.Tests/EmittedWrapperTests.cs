using System.Numerics;
using CS2OpenDev.Sdk.Entities;

namespace CS2OpenDev.Sdk.Entities.Tests;

// Tests for the emitted wrappers: manifests are well-formed, wrappers agree with
// the manifests, and the whole thing composes over a reader and a world.
//
// The layout law under test is SDK#30's prefix rule:
//
//   layout(C) = layout(nearestCuratedAncestor(C)) ++ ordinal-sort(ownFields(C))
//
// which is what makes a base property's compile-time ordinal valid through every
// descendant's binding. These tests cannot prove the wire agrees: both
// projections here come from one computation and agree by construction, so a
// layout that is self-consistently wrong about a real serializer would still
// pass. That half is DemoViewer.NET's real-demo battery, per the SDK#25
// division of labor.
//
// Everything runs against DictionaryEntityReader with no demo bytes and no
// parser, which is what the reference reader was shipped for. A failure here is
// a fault in what this repo emits, not in anyone's runtime.
public class EmittedWrapperTests
{
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
        await Assert.That(EntityWrapperRegistry.Bindings.Count).IsEqualTo(61);
    }

    /// <summary>The registry pins the curated state it was emitted from, so a runtime can detect skew at startup.</summary>
    [Test]
    public async Task Registry_CarriesTheCurationIdentity()
    {
        await Assert.That(EntityWrapperRegistry.LensHash).StartsWith("sha256:");
        await Assert.That(EntityWrapperRegistry.SchemaBuild).IsNotEmpty();
    }

    /// <summary>
    ///     The prefix law: a curated base's CanonicalPaths is a verbatim prefix of every
    ///     descendant's, and the own suffix is ordinal-sorted. Roots stay fully sorted.
    /// </summary>
    /// <remarks>
    ///     This replaced the old whole-array sortedness assertion, which pinned a repo
    ///     convention the contract never stated. The prefix property is stronger and is the
    ///     one correctness rests on: it is exactly what keeps a base wrapper's private
    ///     ordinal constants valid when its body executes over a derived class's binding.
    /// </remarks>
    [Test]
    public async Task CanonicalPaths_FollowThePrefixLaw()
    {
        foreach (EntityClassBinding b in EntityWrapperRegistry.Bindings)
        {
            Type baseType = WrapperType(b.NetName).BaseType!;
            int prefix = 0;

            if (baseType != typeof(EntityWrapper))
            {
                EntityClassBinding baseBinding = EntityWrapperRegistry.Bindings
                    .Single(x => x.NetName == baseType.Name);
                prefix = baseBinding.CanonicalPaths.Count;

                await Assert.That(b.CanonicalPaths.Count).IsGreaterThanOrEqualTo(prefix);
                for (int i = 0; i < prefix; i++)
                {
                    await Assert.That(b.CanonicalPaths[i]).IsEqualTo(baseBinding.CanonicalPaths[i]);
                }
            }

            string[] own = b.CanonicalPaths.Skip(prefix).ToArray();
            string[] sorted = own.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            await Assert.That(own).IsEquivalentTo(sorted);
        }
    }

    /// <summary>An alias never shadows a live field and always targets one. Only matters on demos recorded before a rename.</summary>
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

    /// <summary>A marker class curates nothing of its own, so its binding is its base's layout, verbatim.</summary>
    [Test]
    public async Task MarkerBindings_AreTheirBasesLayoutVerbatim()
    {
        EntityClassBinding ak = Binding("CAK47");
        EntityClassBinding gun = Binding("CCSWeaponBaseGun");

        await Assert.That(ak.CanonicalPaths).IsEquivalentTo(gun.CanonicalPaths);
        await Assert.That(ak.HandleOrdinals).IsEquivalentTo(gun.HandleOrdinals);
        await Assert.That(ak.Aliases).IsEquivalentTo(gun.Aliases);
    }

    /// <summary>
    ///     The five projectile descendants inherit the relocated-origin alias from
    ///     CBaseCSGrenadeProjectile, and the historical spelling still answers lookups.
    /// </summary>
    [Test]
    [Arguments("CDecoyProjectile")]
    [Arguments("CFlashbangProjectile")]
    [Arguments("CHEGrenadeProjectile")]
    [Arguments("CMolotovProjectile")]
    [Arguments("CSmokeGrenadeProjectile")]
    public async Task DerivedProjectiles_CarryTheInheritedOriginAlias(string engineClass)
    {
        EntityClassBinding b = Binding(engineClass);

        await Assert.That(b.Aliases["m_vecOrigin"])
            .IsEqualTo("m_CBodyComponent.m_pSceneNode.m_vecOrigin");

        DictionaryEntityReader reader = Reader(b, new Dictionary<string, object?>
        {
            ["m_CBodyComponent.m_pSceneNode.m_vecOrigin"] = new Vector3(1, 2, 3),
        });

        await Assert.That(reader.TryReadByEnginePath("m_vecOrigin", out object? v)).IsTrue();
        await Assert.That(v).IsEqualTo(new Vector3(1, 2, 3));
    }

    // Every emitted base-type edge, as literal data. This pin is what makes an
    // unattended schema reparent fail CI instead of publishing a silently
    // reshaped C# surface: base edges derive from the live schema on every
    // 4-hourly regen, no other check looks at the emitted hierarchy, and a base
    // swap is case 1 in the #38 surface gate's own "cannot catch" list. A change
    // here has to arrive with a human edit to this table, same discipline as
    // names.lock. typeof rather than strings, so a removed class fails the
    // compile before it can fail the assertion.
    private static readonly Dictionary<Type, Type> ExpectedBases = new()
    {
        // Roots: the schema chain above them reaches no other curated class.
        [typeof(BaseCSGrenadeProjectile)] = typeof(EntityWrapper),
        [typeof(BasePlayerWeapon)] = typeof(EntityWrapper),
        [typeof(CSGameRules)] = typeof(EntityWrapper),
        [typeof(CSGameRulesProxy)] = typeof(EntityWrapper),
        [typeof(CSPlayerController)] = typeof(EntityWrapper),
        [typeof(CSPlayerPawnBase)] = typeof(EntityWrapper),
        [typeof(CSTeam)] = typeof(EntityWrapper),
        [typeof(Inferno)] = typeof(EntityWrapper),
        [typeof(PlantedC4)] = typeof(EntityWrapper),

        [typeof(CSPlayerPawn)] = typeof(CSPlayerPawnBase),

        // The projectile chain.
        [typeof(DecoyProjectile)] = typeof(BaseCSGrenadeProjectile),
        [typeof(FlashbangProjectile)] = typeof(BaseCSGrenadeProjectile),
        [typeof(HEGrenadeProjectile)] = typeof(BaseCSGrenadeProjectile),
        [typeof(MolotovProjectile)] = typeof(BaseCSGrenadeProjectile),
        [typeof(SmokeGrenadeProjectile)] = typeof(BaseCSGrenadeProjectile),

        // The weapon chain. Uncurated intermediates (CCSWeaponBase, CEconEntity)
        // contribute nothing and are skipped by the ancestor walk.
        [typeof(BaseCSGrenade)] = typeof(BasePlayerWeapon),
        [typeof(C4)] = typeof(BasePlayerWeapon),
        [typeof(CSWeaponBaseGun)] = typeof(BasePlayerWeapon),
        [typeof(CSWeaponBaseShotgun)] = typeof(BasePlayerWeapon),
        [typeof(Knife)] = typeof(BasePlayerWeapon),

        [typeof(DecoyGrenade)] = typeof(BaseCSGrenade),
        [typeof(Flashbang)] = typeof(BaseCSGrenade),
        [typeof(HEGrenade)] = typeof(BaseCSGrenade),
        [typeof(MolotovGrenade)] = typeof(BaseCSGrenade),
        [typeof(SmokeGrenade)] = typeof(BaseCSGrenade),

        // The incendiary sits under the molotov, not beside it. That is the
        // schema's shape, and the wire agrees: SDK#34 found it live as a
        // LastWeapon target.
        [typeof(IncendiaryGrenade)] = typeof(MolotovGrenade),

        // Shotguns are not guns. DemoViewer.NET measured exactly this: their
        // serializers carry the weapon base's 8 paths and none of the gun's 3,
        // so a manifest routing them through CSWeaponBaseGun would emit three
        // ordinals that read absent on every real shotgun (SDK#30).
        [typeof(WeaponNOVA)] = typeof(CSWeaponBaseShotgun),
        [typeof(WeaponSawedoff)] = typeof(CSWeaponBaseShotgun),
        [typeof(WeaponXM1014)] = typeof(CSWeaponBaseShotgun),

        [typeof(AK47)] = typeof(CSWeaponBaseGun),
        [typeof(DEagle)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponAWP)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponAug)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponBizon)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponCZ75a)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponElite)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponFamas)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponFiveSeven)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponG3SG1)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponGalilAR)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponGlock)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponHKP2000)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponM249)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponM4A1)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponM4A1Silencer)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponMAC10)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponMP5SD)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponMP7)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponMP9)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponMag7)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponNegev)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponP250)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponP90)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponRevolver)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponSCAR20)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponSG556)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponSSG08)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponTaser)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponTec9)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponUMP45)] = typeof(CSWeaponBaseGun),
        [typeof(WeaponUSPSilencer)] = typeof(CSWeaponBaseGun),
    };

    /// <summary>
    ///     All 61 base-type edges match the pinned census, sealing follows from the edges,
    ///     and no wrapper exists outside the pin.
    /// </summary>
    [Test]
    public async Task Hierarchy_MatchesThePinnedCensus()
    {
        // Completeness first: a class added to the curated set must be added here
        // too, deliberately, or this count fails.
        Type[] emitted = typeof(EntityWrapperRegistry).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(EntityWrapper).IsAssignableFrom(t))
            .ToArray();
        await Assert.That(emitted.Length).IsEqualTo(ExpectedBases.Count);
        await Assert.That(ExpectedBases.Count).IsEqualTo(61);

        // A class is sealed exactly when nothing in the pin derives from it.
        // Sealing follows from the edges, so it is asserted from them rather
        // than pinned twice.
        HashSet<Type> curatedBases = ExpectedBases.Values
            .Where(t => t != typeof(EntityWrapper))
            .ToHashSet();

        foreach ((Type wrapper, Type expectedBase) in ExpectedBases)
        {
            await Assert.That(wrapper.BaseType).IsEqualTo(expectedBase);
            await Assert.That(wrapper.IsSealed).IsEqualTo(!curatedBases.Contains(wrapper));
        }
    }

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
        // The type still exists: it is a usable base and a Resolve<T> target.
        await Assert.That(EntityWrapperRegistry.Bindings.Any(x => x.EngineClass == engineClass)).IsTrue();
    }

    /// <summary>Properties read the field their manifest ordinal names — the invariant behind the private ordinal constants.</summary>
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

    /// <summary>
    ///     A base property body executes over a derived class's binding and reads the right
    ///     field. This is the read SDK#30 exists to make correct.
    /// </summary>
    /// <remarks>
    ///     <c>BasePlayerWeapon.Clip1</c> compiles against <c>Ord.Clip1 = 2</c> in the base's
    ///     own space; the reader here is bound to <c>CAK47</c>'s binding. Under the old flat
    ///     emission this combination read absent for every inherited field; under the prefix
    ///     law the base's space is a verbatim prefix of AK47's, so the constant is right by
    ///     construction.
    /// </remarks>
    [Test]
    public async Task InheritedProperty_ReadsThroughTheDerivedBinding()
    {
        EntityWrapper? w = EntityWrapperRegistry.Create(
            "CAK47",
            Reader(Binding("CAK47"), new Dictionary<string, object?>
            {
                ["m_iClip1"] = 30,
                ["m_zoomLevel"] = 2,
            }),
            new EmptyWorld());

        await Assert.That(w).IsTypeOf<AK47>();

        // Through the base-typed reference, which is what a companion hands out.
        BasePlayerWeapon weapon = (BasePlayerWeapon)w!;
        await Assert.That(weapon.Clip1).IsEqualTo(30);

        // The intermediate level reads through the same binding too.
        await Assert.That(((CSWeaponBaseGun)w!).ZoomLevel).IsEqualTo(2);
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
        // case (the parent path never materialises, only the struct's cell
        // leaves do — SDK#25, finding F3), and a 0-default here presented that
        // as a plausible coordinate. Reflection because the three wrappers share
        // no base with an Origin member; the property's declared type is part of
        // what this test pins.
        EntityWrapper absent = EntityWrapperRegistry.Create(engineClass, Reader(b, []), new EmptyWorld())!;
        var origin = absent.GetType().GetProperty("Origin")!;

        await Assert.That(origin.PropertyType).IsEqualTo(typeof(Vector3?));
        await Assert.That(origin.GetValue(absent)).IsNull();

        // A runtime that stores a value under the canonical path (fabricated
        // state, or reconstructed world coordinates) still serves it through
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

    /// <summary>
    ///     The six quantized-origin leaves are seen-aware on every class carrying the
    ///     relocated origin canonical: absent reads null, not a coordinate. Cell 0 is a legal
    ///     world cell (the consumer-side reconstruction is (cell − 32) × 512 + offset), so a
    ///     0-default would place a never-received entity at −16384 on that axis.
    /// </summary>
    /// <remarks>
    ///     Reflection for the same reason as <see cref="Origin_IsSeenAware"/>: the three
    ///     carriers share no base with these members, and the declared property types
    ///     (<c>int?</c> for cells, <c>float?</c> for offsets) are part of what is pinned:
    ///     a boxed <c>object?</c> here would be the exact allocation the SDK#41 ask exists
    ///     to remove.
    /// </remarks>
    [Test]
    [Arguments("CCSPlayerPawn")]
    [Arguments("CBaseCSGrenadeProjectile")]
    [Arguments("CPlantedC4")]
    public async Task OriginLeaves_AreSeenAwareOnAllThreeCarriers(string engineClass)
    {
        EntityClassBinding b = Binding(engineClass);
        EntityWrapper absent = EntityWrapperRegistry.Create(engineClass, Reader(b, []), new EmptyWorld())!;

        foreach (string cell in new[] { "OriginCellX", "OriginCellY", "OriginCellZ" })
        {
            var p = absent.GetType().GetProperty(cell)!;
            await Assert.That(p.PropertyType).IsEqualTo(typeof(int?));
            await Assert.That(p.GetValue(absent)).IsNull();
        }

        foreach (string vec in new[] { "OriginVecX", "OriginVecY", "OriginVecZ" })
        {
            var p = absent.GetType().GetProperty(vec)!;
            await Assert.That(p.PropertyType).IsEqualTo(typeof(float?));
            await Assert.That(p.GetValue(absent)).IsNull();
        }

        // Presence reads the raw wire value, including the zeros that motivated
        // the nullability. Those must survive the trip as values.
        EntityWrapper present = EntityWrapperRegistry.Create(
            engineClass,
            Reader(b, new Dictionary<string, object?>
            {
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellX"] = 0,
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecX"] = 0f,
            }),
            new EmptyWorld())!;

        await Assert.That(present.GetType().GetProperty("OriginCellX")!.GetValue(present)).IsEqualTo(0);
        await Assert.That(present.GetType().GetProperty("OriginVecX")!.GetValue(present)).IsEqualTo(0f);
    }

    /// <summary>The pawn's leaves read typed alongside the struct-valued Origin, which they decompose rather than replace.</summary>
    [Test]
    public async Task OriginLeaves_ReadTypedBesideOrigin()
    {
        CSPlayerPawn pawn = new(
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?>
            {
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellX"] = 35,
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellY"] = 33,
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellZ"] = 32,
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecX"] = 231.96875f,
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecY"] = 12.5f,
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_vecZ"] = 0.03125f,
            }),
            new EmptyWorld());

        await Assert.That(pawn.OriginCellX).IsEqualTo(35);
        await Assert.That(pawn.OriginCellY).IsEqualTo(33);
        await Assert.That(pawn.OriginCellZ).IsEqualTo(32);
        await Assert.That(pawn.OriginVecX).IsEqualTo(231.96875f);
        await Assert.That(pawn.OriginVecY).IsEqualTo(12.5f);
        await Assert.That(pawn.OriginVecZ).IsEqualTo(0.03125f);

        // The struct-valued parent path stays absent: the leaves are curated
        // beside Origin, not through it, and neither read disturbs the other.
        await Assert.That(pawn.Origin).IsNull();
    }

    /// <summary>The projectile leaves are curated on the base once, so every projectile wrapper inherits them through its own binding.</summary>
    [Test]
    [Arguments("CSmokeGrenadeProjectile")]
    [Arguments("CMolotovProjectile")]
    public async Task ProjectileOriginLeaves_ReadThroughDerivedBindings(string engineClass)
    {
        EntityWrapper w = EntityWrapperRegistry.Create(
            engineClass,
            Reader(Binding(engineClass), new Dictionary<string, object?>
            {
                ["m_CBodyComponent.m_pSceneNode.m_vecOrigin.m_cellZ"] = 31,
            }),
            new EmptyWorld())!;

        // Base-typed reference, base ordinal constant, derived binding: the
        // prefix law working for the new fields the same way it does for Clip1.
        BaseCSGrenadeProjectile projectile = (BaseCSGrenadeProjectile)w;
        await Assert.That(projectile.OriginCellZ).IsEqualTo(31);
        await Assert.That(projectile.OriginCellX).IsNull();
    }

    /// <summary>CCSTeam curates the scoreboard triple; the two scalars resolve on its ancestors, the clan name on the class itself.</summary>
    [Test]
    public async Task CSTeam_ReadsTheScoreboardTriple()
    {
        EntityWrapper? w = EntityWrapperRegistry.Create(
            "CCSTeam",
            Reader(Binding("CCSTeam"), new Dictionary<string, object?>
            {
                ["m_iTeamNum"] = 3,
                ["m_iScore"] = 13,
                ["m_szClanTeamname"] = "NAVI",
            }),
            new EmptyWorld());

        await Assert.That(w).IsTypeOf<CSTeam>();
        CSTeam team = (CSTeam)w!;
        await Assert.That(team.TeamNum).IsEqualTo(3);
        await Assert.That(team.Score).IsEqualTo(13);
        // A char[129] has no first-class shape on the seam; boxed is the honest
        // projection, and the runtime decides what a string field decodes to.
        await Assert.That(team.ClanTeamname).IsEqualTo("NAVI");
    }

    /// <summary>
    ///     CInferno curates the scalar; the fire arrays stay on the by-path hatch. An [i]
    ///     element is not a schema field a canonical path can name, so the ordinal space has
    ///     nothing to offer arrays without new contract surface — which SDK#41 does not ask
    ///     for and constraint 1 forbids.
    /// </summary>
    [Test]
    public async Task Inferno_CuratesTheScalarAndLeavesArraysToTheHatch()
    {
        bool[] burning = new bool[64];
        burning[0] = true;

        DictionaryEntityReader reader = Reader(Binding("CInferno"), new Dictionary<string, object?>
        {
            ["m_fireCount"] = 7,
            ["m_bFireIsBurning"] = burning,
        });

        Inferno inferno = (Inferno)EntityWrapperRegistry.Create("CInferno", reader, new EmptyWorld())!;
        await Assert.That(inferno.FireCount).IsEqualTo(7);

        // The arrays answer by exact spelling through the hatch, boxed. Same
        // read DemoViewer.NET's runtime serves today; this curation does not
        // change it.
        await Assert.That(reader.TryReadByEnginePath("m_bFireIsBurning", out object? v)).IsTrue();
        await Assert.That(v).IsEqualTo(burning);
    }

    /// <summary>
    ///     The match-scoped totals read beside the round-scoped stats. The schema-true
    ///     canonical routes through the embedded m_matchStats; the issue's
    ///     serializer-flattened spelling still answers through the alias table, the same
    ///     adaptation as m_vecOrigin.
    /// </summary>
    [Test]
    public async Task MatchTotals_AreScopedApartFromRoundStats()
    {
        DictionaryEntityReader reader = Reader(Binding("CCSPlayerController"), new Dictionary<string, object?>
        {
            ["m_pActionTrackingServices.m_matchStats.m_iKills"] = 24,
            ["m_pActionTrackingServices.m_matchStats.m_iDeaths"] = 17,
            ["m_pActionTrackingServices.m_matchStats.m_iAssists"] = 5,
            ["m_pActionTrackingServices.m_matchStats.m_iDamage"] = 2412,
            ["m_pActionTrackingServices.m_iNumRoundKills"] = 3,
        });

        CSPlayerController controller = new(reader, new EmptyWorld());

        await Assert.That(controller.MatchKills).IsEqualTo(24);
        await Assert.That(controller.MatchDeaths).IsEqualTo(17);
        await Assert.That(controller.MatchAssists).IsEqualTo(5);
        await Assert.That(controller.MatchDamage).IsEqualTo(2412);
        await Assert.That(controller.NumRoundKills).IsEqualTo(3);

        // The flattened spelling from the issue resolves through the alias.
        await Assert.That(reader.TryReadByEnginePath("m_pActionTrackingServices.m_iKills", out object? kills)).IsTrue();
        await Assert.That(kills).IsEqualTo(24);
    }

    /// <summary>The minor leaves read through their curated names: minimap framing, last place, duck amount, pending team.</summary>
    [Test]
    public async Task MinorLeaves_ReadThroughTheirCuratedNames()
    {
        CSGameRulesProxy rules = new(
            Reader(Binding("CCSGameRulesProxy"), new Dictionary<string, object?>
            {
                ["m_pGameRules.m_vMinimapMins"] = new Vector3(-2476, -2444, -100),
                ["m_pGameRules.m_vMinimapMaxs"] = new Vector3(1735, 1770, 300),
            }),
            new EmptyWorld());

        await Assert.That(rules.MinimapMins).IsEqualTo(new Vector3(-2476, -2444, -100));
        await Assert.That(rules.MinimapMaxs).IsEqualTo(new Vector3(1735, 1770, 300));

        CSPlayerPawn pawn = new(
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?>
            {
                ["m_szLastPlaceName"] = "BombsiteA",
                ["m_pMovementServices.m_flDuckAmount"] = 0.62f,
            }),
            new EmptyWorld());

        await Assert.That(pawn.LastPlaceName).IsEqualTo("BombsiteA");
        await Assert.That(pawn.DuckAmount).IsEqualTo(0.62f);

        CSPlayerController controller = new(
            Reader(Binding("CCSPlayerController"), new Dictionary<string, object?>
            {
                ["m_iPendingTeamNum"] = 2,
            }),
            new EmptyWorld());

        await Assert.That(controller.PendingTeamNum).IsEqualTo(2);
    }

    /// <summary>A handle crosses raw and resolves through the world, with a serial in the high bits so mask policy is genuinely exercised.</summary>
    [Test]
    public async Task Handle_CrossesRawAndResolves()
    {
        // Serial 1 << 17 | slot 0x1234, so a pass here is not slot-0 luck.
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

        // No cast: the companion is BasePlayerWeapon?, so Clip1 is directly
        // readable. SDK#30's hierarchy is what makes this compile.
        await Assert.That(pawn.ActiveWeapon!.Clip1).IsEqualTo(30);
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

    // Companion typing. SDK#25 F1/F2 and SDK#30, all found over real demos.

    /// <summary>
    ///     The weapon companions carry the type a runtime's dispatch actually satisfies:
    ///     a concrete weapon's wrapper is a BasePlayerWeapon now, so the typed fold
    ///     succeeds and the companion stops needing a cast.
    /// </summary>
    /// <remarks>
    ///     This reverses the #29 reversal, and the hierarchy is what makes the typed
    ///     version correct this time. Under the flat emission a resolved
    ///     <c>SmokeGrenade</c> was not a <c>BasePlayerWeapon</c>, so a companion typed
    ///     that way read null for a weapon that resolved fine (SDK#25, finding F1), and
    ///     <c>EntityWrapper?</c> was the honest type. The declared property type is
    ///     pinned by reflection because it is what DemoViewer.NET's adaptation binds
    ///     against.
    /// </remarks>
    [Test]
    public async Task WeaponCompanions_AreTypedForWhatARuntimeActuallyReturns()
    {
        await Assert.That(typeof(CSPlayerPawn).GetProperty("ActiveWeapon")!.PropertyType)
            .IsEqualTo(typeof(BasePlayerWeapon));
        await Assert.That(typeof(CSPlayerPawn).GetProperty("LastWeapon")!.PropertyType)
            .IsEqualTo(typeof(BasePlayerWeapon));

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

        BasePlayerWeapon? active = pawn.ActiveWeapon;
        await Assert.That(active).IsNotNull();
        await Assert.That(active).IsTypeOf<SmokeGrenade>();
    }

    /// <summary>
    ///     The one common weapon class SDK#34's measurements found missing from the curated
    ///     set: a live incendiary resolves through the LastWeapon companion and reads its
    ///     inherited fields — all of them, since it curates nothing of its own.
    /// </summary>
    [Test]
    public async Task IncendiaryGrenade_IsCuratedAndResolvesAsALastWeapon()
    {
        const uint handle = 0x0005_0021u;

        EntityWrapper incendiary = EntityWrapperRegistry.Create(
            "CIncendiaryGrenade",
            Reader(Binding("CIncendiaryGrenade"), new Dictionary<string, object?>
            {
                ["m_iClip1"] = 1,
            }),
            new EmptyWorld())!;

        await Assert.That(incendiary).IsTypeOf<IncendiaryGrenade>();

        TableWorld world = new();
        world.Add(handle, incendiary);

        CSPlayerPawn pawn = new(
            Reader(Binding("CCSPlayerPawn"), new Dictionary<string, object?>
            {
                ["m_pWeaponServices.m_hLastWeapon"] = handle,
            }),
            world);

        BasePlayerWeapon? last = pawn.LastWeapon;
        await Assert.That(last).IsNotNull();
        await Assert.That(last!.Clip1).IsEqualTo(1);
    }

    /// <summary>A handle declared against the client-side spelling of a curated class still gets its companion. Controller to pawn is the most common traversal.</summary>
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

        // m_hPlayerPawn declares CHandle< C_CSPlayerPawn >, the client spelling
        // of the curated, server-named CCSPlayerPawn.
        CSPlayerController controller = new(
            Reader(Binding("CCSPlayerController"), new Dictionary<string, object?>
            {
                ["m_hPlayerPawn"] = handle,
            }),
            world);

        await Assert.That(controller.PlayerPawn).IsNotNull();
        await Assert.That(controller.PlayerPawn!.Health).IsEqualTo(55);
    }

    /// <summary>A target with no curated descendants keeps its specific type, so the hierarchy change did not flatten companions that were already correct.</summary>
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

        // Statically CSPlayerPawn?, not EntityWrapper?. The type still carries information.
        CSPlayerPawn? typed = bomb.BombDefuser;
        await Assert.That(typed).IsNotNull();
    }

    private static EntityClassBinding Binding(string engineClass) =>
        EntityWrapperRegistry.Bindings.Single(b => b.EngineClass == engineClass);

    private static Type WrapperType(string netName) =>
        typeof(EntityWrapperRegistry).Assembly
            .GetType($"CS2OpenDev.Sdk.Entities.{netName}", throwOnError: true)!;

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
