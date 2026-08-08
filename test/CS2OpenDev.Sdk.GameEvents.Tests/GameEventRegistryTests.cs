#region

using CS2OpenDev.Sdk.GameEvents;
using CS2OpenSchema.Events;

#endregion

namespace CS2OpenDev.Sdk.GameEvents.Tests;

// Registry tests, focused on the duplicate-name problem.
//
// A registry keyed on native event name cannot be a plain 1:1 map: 15 of 272
// names carry more than one declaration, because the same event is declared in
// several `.gameevents` files with different field sets. A dispatcher that
// assumes uniqueness silently decodes `player_death` into the 2-field core
// shape and loses 16 fields.
public class GameEventRegistryTests
{
    /// <summary>Every declared native name resolves to a factory.</summary>
    [Test]
    public async Task EveryEventName_HasAPreferredFactory()
    {
        await Assert.That(GameEventRegistry.EventNames.Count).IsEqualTo(GameEventRegistry.NameCount);

        foreach (string name in GameEventRegistry.EventNames)
        {
            await Assert.That(GameEventRegistry.TryGetFactory(name, out _)).IsTrue();
        }
    }

    /// <summary>There are more declarations than names — the duplicates this registry exists to disambiguate.</summary>
    [Test]
    public async Task DeclarationCount_ExceedsNameCount()
    {
        // Counted from the live table rather than compared against the emitted
        // constants, so this asserts the registry's actual contents rather than
        // that two generated literals differ.
        int declarations = GameEventRegistry.EventNames.Sum(n => GameEventRegistry.GetAllFactories(n).Count);

        await Assert.That(declarations).IsEqualTo(GameEventRegistry.DeclarationCount);
        await Assert.That(declarations).IsGreaterThan(GameEventRegistry.EventNames.Count);
    }

    /// <summary>An unknown name resolves to nothing rather than throwing.</summary>
    [Test]
    public async Task UnknownName_ResolvesToNothing()
    {
        await Assert.That(GameEventRegistry.TryGetFactory("no_such_event", out _)).IsFalse();
        await Assert.That(GameEventRegistry.GetAllFactories("no_such_event")).IsEmpty();
    }

    /// <summary>player_death has more than one declaration, and the preferred one is the mod shape CS2 actually fires.</summary>
    [Test]
    public async Task PlayerDeath_PrefersModDeclaration()
    {
        IReadOnlyList<GameEventDeclaration> all = GameEventRegistry.GetAllFactories("player_death");
        await Assert.That(all.Count).IsGreaterThan(1);

        // The mod declaration is the rich one; core declares only attacker+userid.
        GameEventDeclaration mod = all.First(d => d.Source == "mod.gameevents");
        await Assert.That(mod.RecordType).IsEqualTo(typeof(PlayerDeathEvent));

        GameEventDeclaration core = all.First(d => d.Source == "core.gameevents");
        await Assert.That(core.RecordType).IsEqualTo(typeof(PlayerDeathCoreEvent));

        // TryGetFactory must hand back the mod shape, not whichever happened to
        // be enumerated first.
        await Assert.That(GameEventRegistry.TryGetFactory("player_death", out GameEventFactory preferred)).IsTrue();
        GameEventReader reader = default;
        await Assert.That(preferred(in reader)).IsTypeOf<PlayerDeathEvent>();
    }

    /// <summary>round_end carries three declarations; all remain reachable.</summary>
    [Test]
    public async Task RoundEnd_ExposesAllThreeDeclarations()
    {
        IReadOnlyList<GameEventDeclaration> all = GameEventRegistry.GetAllFactories("round_end");
        await Assert.That(all.Count).IsEqualTo(3);
        await Assert.That(all.Select(d => d.Source).Distinct().Count()).IsEqualTo(3);
    }

    /// <summary>Every declaration's factory builds an instance of the record type it advertises.</summary>
    [Test]
    public async Task EveryDeclaration_ProducesItsAdvertisedType()
    {
        GameEventReader reader = default;

        foreach (string name in GameEventRegistry.EventNames)
        {
            foreach (GameEventDeclaration declaration in GameEventRegistry.GetAllFactories(name))
            {
                object produced = declaration.Factory(in reader);
                await Assert.That(produced.GetType()).IsEqualTo(declaration.RecordType);
            }
        }
    }

    /// <summary>
    ///     Every factory survives a default reader — no descriptor, no keys.
    /// </summary>
    /// <remarks>
    ///     Exercises all 288 factories against the empty case in one pass. A field
    ///     whose accessor could throw on absence would fail here rather than in a
    ///     consumer's demo parse, which is where a null-vs-default mistake would
    ///     otherwise first appear.
    /// </remarks>
    [Test]
    public async Task EveryFactory_ToleratesAnEmptyEvent()
    {
        GameEventReader reader = default;
        int built = 0;

        foreach (string name in GameEventRegistry.EventNames)
        {
            GameEventRegistry.TryGetFactory(name, out GameEventFactory factory);
            await Assert.That(factory(in reader)).IsNotNull();
            built++;
        }

        await Assert.That(built).IsEqualTo(GameEventRegistry.NameCount);
    }
}
