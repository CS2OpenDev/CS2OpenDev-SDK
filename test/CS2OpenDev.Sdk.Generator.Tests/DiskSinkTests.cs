using CS2OpenDev.SdkExporter;

namespace CS2_OpenDev.Sdk.Generator.Tests;

// Tier 1 — unit tests for DiskSink.
//
// Both behaviours pinned here were bugs, and both had the same shape: the run
// stayed green and the damage showed up somewhere else entirely. Identifier
// word-splitting turned them from theoretical into routine, because it produces
// case-only renames and near-collisions by the dozen.
public class DiskSinkTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cs2sink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Two emissions targeting one path is an error, not a last-writer-wins overwrite.</summary>
    [Test]
    public async Task AddSource_DuplicatePath_Throws()
    {
        string dir = NewDir();
        DiskSink sink = new(dir, []);

        sink.AddSource("Server/CFoo", "// first");

        // Before this threw, the second write simply replaced the first: 16
        // distinct schema types collapsed onto 8 files, the run reported
        // "Exported 4612 file(s)" with 4596 on disk, and the loss only surfaced
        // later as an unresolved type reference.
        await Assert.That(() => sink.AddSource("Server/CFoo", "// second"))
                    .Throws<InvalidOperationException>();
    }

    /// <summary>Distinct paths are unaffected by the duplicate guard.</summary>
    [Test]
    public async Task AddSource_DistinctPaths_BothWritten()
    {
        string dir = NewDir();
        DiskSink sink = new(dir, []);

        sink.AddSource("Server/CFoo", "// a");
        sink.AddSource("Server/CBar", "// b");

        await Assert.That(sink.WrittenCount).IsEqualTo(2);
        await Assert.That(File.Exists(Path.Combine(dir, "Server", "CFoo.cs"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(dir, "Server", "CBar.cs"))).IsTrue();
    }

    /// <summary>A case-only rename replaces the old file rather than writing through it.</summary>
    [Test]
    public async Task AddSource_CaseOnlyRename_ReplacesTheOldFile()
    {
        string dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "Sound"));
        string oldPath = Path.Combine(dir, "Sound", "Soundlevel.cs");
        File.WriteAllText(oldPath, "// stale");

        DiskSink sink = new(dir, []);
        sink.AddSource("Sound/SoundLevel", "// fresh");

        // macOS and Windows resolve paths case-insensitively, so without the
        // explicit delete this write lands on the old inode and KEEPS the old
        // name. The stale sweep then does not recognise the path as claimed and
        // deletes the file that was just written — which is how 16 types
        // vanished on macOS while the same generator produced a correct tree on
        // Linux CI.
        string[] files = Directory.GetFiles(Path.Combine(dir, "Sound"));
        await Assert.That(files.Length).IsEqualTo(1);
        await Assert.That(Path.GetFileName(files[0])).IsEqualTo("SoundLevel.cs");
        await Assert.That(File.ReadAllText(files[0])).Contains("fresh");
    }

    /// <summary>The replaced file is dropped from the stale set, so the sweep cannot delete the newly written one.</summary>
    [Test]
    public async Task AddSource_CaseOnlyRename_ClearsTheOldPathFromStaleCandidates()
    {
        string dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "Sound"));
        string oldPath = Path.GetFullPath(Path.Combine(dir, "Sound", "Soundlevel.cs"));
        File.WriteAllText(oldPath, "// stale");

        HashSet<string> stale = new(StringComparer.Ordinal) { oldPath };
        DiskSink sink = new(dir, stale);
        sink.AddSource("Sound/SoundLevel", "// fresh");

        await Assert.That(stale).IsEmpty();
    }

    /// <summary>Content is normalised to LF with exactly one trailing newline, so the committed tree is stable across platforms.</summary>
    [Test]
    public async Task AddSource_NormalisesLineEndingsAndTrailingWhitespace()
    {
        string dir = NewDir();
        DiskSink sink = new(dir, []);

        sink.AddSource("X", "line one\r\nline two\r\n\r\n\r\n");

        string written = File.ReadAllText(Path.Combine(dir, "X.cs"));
        await Assert.That(written).IsEqualTo("line one\nline two\n");
    }
}
