using CS2SchemaGen.Emitters;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — unit tests for WordSplitter.
//
// Two halves, and the second matters more. The first pins that known compounds
// split. The second pins that everything else is left EXACTLY as it was — the
// splitter's value depends on it never inventing a boundary, because a wrong
// identifier compiles just as happily as a right one and no downstream check
// catches it.
public class WordSplitterTests
{
    // ── Splits it should make ────────────────────────────────────────────────

    /// <summary>Run-together lowercase compounds separate into their words.</summary>
    [Test]
    [Arguments("Userid", "UserId")]
    [Arguments("Thrusmoke", "ThruSmoke")]
    [Arguments("Attackerinair", "AttackerInAir")]
    [Arguments("Attackerblind", "AttackerBlind")]
    [Arguments("Issilenced", "IsSilenced")]
    [Arguments("Noscope", "NoScope")]
    [Arguments("Splitscreenplayer", "SplitScreenPlayer")]
    [Arguments("Entindex", "EntIndex")]
    [Arguments("Musickitmvps", "MusicKitMvps")]
    public async Task Split_KnownCompound_SeparatesWords(string input, string expected) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(expected);

    /// <summary>Casing overrides win over plain capitalisation, so the output is idiomatic rather than merely split.</summary>
    [Test]
    [Arguments("Cvarname", "CVarName")]
    [Arguments("Accountid", "AccountId")]
    [Arguments("Weptype", "WepType")]
    public async Task Split_CasingOverride_UsesIdiomaticForm(string input, string expected) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(expected);

    /// <summary>A lone `ID` run folds to `Id`, because upstream spells the same suffix both ways and the two must not land on one type.</summary>
    [Test]
    [Arguments("SubclassID", "SubclassId")]
    [Arguments("AnimParamID", "AnimParamId")]
    [Arguments("PlayerID", "PlayerId")]
    public async Task Split_TrailingIDRun_FoldsToId(string input, string expected) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(expected);

    /// <summary>An all-caps word that merely ends in the letters I-D is not an `ID` run. `INVALID` becoming `INVALId` is the failure this prevents.</summary>
    [Test]
    [Arguments("INVALID")]
    [Arguments("VALID")]
    public async Task Split_AllCapsWordEndingInId_IsUntouched(string input) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(input);

    // ── Splits it must NOT make ──────────────────────────────────────────────

    /// <summary>Compounds reported by a downstream consumer against the shipped 3.0.3 API (GitHub issue #2).</summary>
    [Test]
    [Arguments("Isbot", "IsBot")]
    [Arguments("Noreplay", "NoReplay")]
    [Arguments("Damagebits", "DamageBits")]
    [Arguments("Totalrewards", "TotalRewards")]
    [Arguments("Fauxitemid", "FauxItemId")]
    [Arguments("Hcontent", "HContent")]
    public async Task Split_ConsumerReportedCompound_SeparatesWords(string input, string expected) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(expected);

    /// <summary>Single English words that a greedy match would happily chew up. Each of these segments cleanly into vocabulary tokens and is still wrong.</summary>
    [Test]
    [Arguments("Identity")]      // id + entity  — shipped as CEntityIdEntity before this was caught
    [Arguments("Instr")]         // in + str
    [Arguments("Screenshot")]    // screen + shot
    [Arguments("Subclass")]      // sub + class
    [Arguments("Assister")]      // assist + er
    [Arguments("Hostage")]       // host + age
    [Arguments("Both")]          // bot + h    — the price of the one-character `h` token
    [Arguments("Hash")]          // has + h
    [Arguments("Hold")]          // h + old
    [Arguments("Hover")]         // h + over
    public async Task Split_AtomicWord_IsNeverSegmented(string input) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(input);

    /// <summary>A compound the vocabulary does not fully cover is left alone rather than half-split. This is the safety property: unknown input yields the old name, never a guess.</summary>
    [Test]
    [Arguments("Zzzqqqwww")]
    [Arguments("Frobnicator")]
    public async Task Split_UnknownRun_FallsBackToInput(string input) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(input);

    /// <summary>Casing upstream already supplied is authoritative — an existing hump means the boundary is known and needs no guessing.</summary>
    [Test]
    [Arguments("NextAttack")]
    [Arguments("NumCTSlotsFree")]
    [Arguments("CPPClassName")]
    public async Task Split_ExistingHumps_ArePreserved(string input) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(input);

    /// <summary>A run that IS a single vocabulary word is a word, not a one-token split.</summary>
    [Test]
    [Arguments("Attacker")]
    [Arguments("Player")]
    [Arguments("Weapon")]
    public async Task Split_SingleVocabularyWord_IsUnchanged(string input) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(input);

    /// <summary>Underscores and digits between runs survive verbatim; the splitter rewrites words, not structure.</summary>
    [Test]
    [Arguments("Option1", "Option1")]
    [Arguments("Target2", "Target2")]
    public async Task Split_TrailingDigits_AreKept(string input, string expected) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(expected);

    /// <summary>Degenerate input does not throw.</summary>
    [Test]
    [Arguments("")]
    [Arguments("A")]
    [Arguments("_")]
    public async Task Split_DegenerateInput_IsReturnedUnchanged(string input) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(input);

    // ── Compositionality ─────────────────────────────────────────────────────
    //
    // The fold has to be a function of the run and nothing else. It was not: the
    // fast-path guard needed four LOWERCASE characters, so `Hbox` fell out early
    // and kept its spelling while the identical run inside `HboxReverse` cleared
    // the bar on the trailing word and got segmented. Both spellings shipped in
    // one generated file.

    /// <summary>A run resolves the same standing alone as it does with more identifier after it. Anything else means the fast-path guard is deciding outcomes rather than deciding whether to look.</summary>
    [Test]
    [Arguments("Hbox", "Reverse")]
    [Arguments("Hbox", "Enabled")]
    [Arguments("Userid", "Count")]
    [Arguments("Cat", "Reverse")]        // three characters: unsplittable either way, and must stay that way
    [Arguments("Frobnicator", "Value")]  // unknown run: still identical in both positions
    public async Task Split_Run_ResolvesIdenticallyAloneAndInACompound(string run, string suffix) =>
        await Assert.That(WordSplitter.Split(run) + suffix)
                    .IsEqualTo(WordSplitter.Split(run + suffix));

    /// <summary>The concrete regression: a four-character run with only three lowercase characters is a run like any other.</summary>
    [Test]
    [Arguments("Hbox", "HBox")]
    [Arguments("Hmodel", "HModel")]
    public async Task Split_ShortRunWithLeadingCapital_IsStillSegmented(string input, string expected) =>
        await Assert.That(WordSplitter.Split(input)).IsEqualTo(expected);

    // ── The CS2_GEN_006 near-miss filter ─────────────────────────────────────
    //
    // Tested through the predicate rather than through `UnsegmentedRuns`, which
    // is a process-wide bucket that is never cleared and is written to by every
    // other test in the run.

    /// <summary>Runs long enough to be a compound and touching a known word at one end are reported. Five characters is the floor; the old floor of seven put a whole class of compound out of reach.</summary>
    [Test]
    [Arguments("kitzz")]        // starts with `kit`
    [Arguments("buymenu")]      // starts with `buy`
    [Arguments("abortdefuse")]  // ends in `defuse`
    public async Task IsReportableNearMiss_TouchingRunAtOrAboveTheFloor_IsReported(string run) =>
        await Assert.That(WordSplitter.IsReportableNearMiss(run, 0)).IsTrue();

    /// <summary>Below the floor, or touching nothing, or already segmented — none of it is a vocabulary gap.</summary>
    [Test]
    [Arguments("okit", 0)]         // four characters: too short to be a compound worth reporting
    [Arguments("zzzqqqwww", 0)]    // touches no known word at either end
    [Arguments("abortdefuse", 2)]  // segmented fine; not a miss at all
    [Arguments("headshot", 1)]     // one token consumed the whole run — a word, not a failure
    public async Task IsReportableNearMiss_NonGaps_AreNotReported(string run, int matchedTokens) =>
        await Assert.That(WordSplitter.IsReportableNearMiss(run, matchedTokens)).IsFalse();
}
