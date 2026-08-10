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

    /// <summary>Single English words that a greedy match would happily chew up. Each of these segments cleanly into vocabulary tokens and is still wrong.</summary>
    [Test]
    [Arguments("Identity")]      // id + entity  — shipped as CEntityIdEntity before this was caught
    [Arguments("Instr")]         // in + str
    [Arguments("Screenshot")]    // screen + shot
    [Arguments("Subclass")]      // sub + class
    [Arguments("Assister")]      // assist + er
    [Arguments("Hostage")]       // host + age
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
}
