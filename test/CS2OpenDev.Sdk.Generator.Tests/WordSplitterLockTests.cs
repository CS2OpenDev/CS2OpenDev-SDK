using CS2SchemaGen.Emitters;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — unit tests for the name lock.
//
// The lock's whole job is to make the word vocabulary safe to edit. Adding a
// word to split some new field also re-splits every existing name that word can
// cut — `base` turned `Database` into `DataBase` — and without the lock that
// renames published API from a job that publishes unattended.
//
// So the test that matters is not "the lock is read". It is "a locked name does
// not move when the vocabulary would move it", asserted below by locking values
// the vocabulary demonstrably disagrees with.
//
// These use SplitWith rather than LoadLock: the lock is a process-wide static,
// and installing one here while the runner executes other name tests in
// parallel changes their answers too.
public class WordSplitterLockTests
{
    private static Dictionary<string, string> Lock(params (string Run, string Result)[] entries) =>
        entries.ToDictionary(e => e.Run, e => e.Result, StringComparer.Ordinal);

    /// <summary>A locked run is returned verbatim even when the vocabulary would split it differently. This is the property the lock exists for.</summary>
    [Test]
    public async Task Split_LockedRun_OverridesTheVocabulary()
    {
        // The vocabulary splits this to UserId; the lock says otherwise and wins.
        await Assert.That(WordSplitter.SplitWith("Userid", Lock(("userid", "Userid"))))
                    .IsEqualTo("Userid");
    }

    /// <summary>A run locked as a split stays split even when the vocabulary could not produce that split at all.</summary>
    [Test]
    public async Task Split_LockedSplit_SurvivesAVocabularyThatCannotProduceIt()
    {
        await Assert.That(WordSplitter.SplitWith("Zzzqqq", Lock(("zzzqqq", "ZzzQqq"))))
                    .IsEqualTo("ZzzQqq");
    }

    /// <summary>An unlocked run falls through to the vocabulary, which is how a new upstream field gets a name at all.</summary>
    [Test]
    public async Task Split_UnlockedRun_UsesTheVocabulary()
    {
        await Assert.That(WordSplitter.SplitWith("Userid", Lock())).IsEqualTo("UserId");
    }

    /// <summary>The lock keys on the lowercase run, so it applies whatever casing the earlier folds left behind.</summary>
    [Test]
    [Arguments("Userid")]
    [Arguments("userid")]
    public async Task Split_LockLookup_KeysOnTheLowercasedRun(string input)
    {
        await Assert.That(WordSplitter.SplitWith(input, Lock(("userid", "UserIdentifier"))))
                    .IsEqualTo("UserIdentifier");
    }

    /// <summary>A locked run inside a longer identifier applies to that run only and leaves the surrounding humps alone.</summary>
    [Test]
    public async Task Split_LockedRun_AppliesWithinACompoundIdentifier()
    {
        await Assert.That(
                WordSplitter.SplitWith("HintActivatorUserid", Lock(("userid", "Userid"))))
            .IsEqualTo("HintActivatorUserid");
    }

    /// <summary>A lock entry for one run does not leak onto a different run.</summary>
    [Test]
    public async Task Split_LockedRun_DoesNotAffectOtherRuns()
    {
        Dictionary<string, string> lockEntries = Lock(("userid", "Userid"));

        await Assert.That(WordSplitter.SplitWith("Thrusmoke", lockEntries)).IsEqualTo("ThruSmoke");
    }

    /// <summary>The `ID` -> `Id` fold is a fixed rule rather than a vocabulary decision, so it applies with or without a lock.</summary>
    [Test]
    public async Task Split_IdFold_IsNotSubjectToTheLock()
    {
        await Assert.That(WordSplitter.SplitWith("AnimParamID", Lock())).IsEqualTo("AnimParamId");
    }
}
