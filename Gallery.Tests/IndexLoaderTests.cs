using Gallery.Domain.Index;

namespace Gallery.Tests;

/// <summary>
/// Tests for IndexLoader covering gallery-rendering-protocol.v0.1 vectors.
/// </summary>
[TestFixture]
public class IndexLoaderTests
{
    private FakeFileReader _fs = null!;
    private IndexLoader _loader = null!;

    private const string WorkspaceRoot = @"C:\Projects\MyApp";
    private const string IndexPath = @"C:\Projects\MyApp\.codecomfy\outputs\index.json";

    [SetUp]
    public void SetUp()
    {
        _fs = new FakeFileReader();
        _loader = new IndexLoader(_fs);
    }

    private void SetupValidWorkspace()
    {
        _fs.AddDirectory(WorkspaceRoot);
        _fs.AddDirectory(@"C:\Projects\MyApp\.codecomfy");
        _fs.AddDirectory(@"C:\Projects\MyApp\.codecomfy\outputs");
    }

    private static string ValidIndex(string items = "[]") => $$"""
        {
          "schema_version": "0.1",
          "updated_at": "2025-01-15T10:30:00Z",
          "items": {{items}}
        }
        """;

    private static string ValidEntry(
        string jobId = "abc123",
        string createdAt = "2025-01-15T10:30:00Z",
        string kind = "image",
        long seed = 42,
        string? prompt = "a cat") => $$"""
        {
          "job_id": "{{jobId}}",
          "created_at": "{{createdAt}}",
          "kind": "{{kind}}",
          "files": [{"path": "{{jobId}}/image_0.png", "sha256": "{{new string('a', 64)}}"}],
          "seed": {{seed}}{{(prompt != null ? $",\n      \"prompt\": \"{prompt}\"" : "")}}
        }
        """;

    #region Spine Tests (critical first 3)

    [Test]
    public void MissingIndexFile_ReturnsEmpty_NoBanner()
    {
        // I1: File doesn't exist → Empty state
        SetupValidWorkspace();
        // Note: no index.json file added

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Empty>());
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.None));
    }

    [Test]
    public void ZeroByteIndex_ReturnsWarning_Corrupt()
    {
        // I2: 0 bytes = corrupt (writer crash mid-write)
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, Array.Empty<byte>());

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Empty>());
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
        Assert.That(result.Banner.Message, Does.Contain("empty").Or.Contain("corrupt"));
    }

    [Test]
    public void InvalidJson_ReturnsWarning_AndKeepsLastKnownGood()
    {
        // I3: Truncated JSON with last-known-good
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, "{");

        var lastKnownGood = new List<JobRow>
        {
            new JobRow
            {
                JobId = "old123",
                CreatedAt = DateTimeOffset.UtcNow,
                Kind = JobKind.Image,
                Files = new[] { new FileRef { RelativePath = "old/img.png", Sha256 = new string('b', 64) } },
                Seed = 1
            }
        };

        var result = _loader.Load(WorkspaceRoot, lastKnownGood);

        // Should keep showing last known good list
        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;
        Assert.That(list.Items, Has.Count.EqualTo(1));
        Assert.That(list.Items[0].JobId, Is.EqualTo("old123"));

        // With warning banner
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
        Assert.That(result.Banner.Message, Does.Contain("corrupt"));
    }

    #endregion

    #region Workspace/Folder Tests (W1-W5)

    [Test]
    public void W1_WorkspaceNotFound_ReturnsFatal()
    {
        // W1: --workspace C:\nonexistent
        var result = _loader.Load(@"C:\nonexistent");

        Assert.That(result.State, Is.TypeOf<GalleryState.Fatal>());
        var fatal = (GalleryState.Fatal)result.State;
        Assert.That(fatal.Message, Does.Contain("Cannot access workspace"));
        Assert.That(fatal.Reason, Is.EqualTo(FatalReason.WorkspaceNotFound));
    }

    [Test]
    public void W2_WorkspaceIsFile_ReturnsFatal()
    {
        // W2: --workspace C:\file.txt (file, not dir)
        _fs.AddFile(@"C:\file.txt", "content");

        var result = _loader.Load(@"C:\file.txt");

        Assert.That(result.State, Is.TypeOf<GalleryState.Fatal>());
        var fatal = (GalleryState.Fatal)result.State;
        Assert.That(fatal.Message, Does.Contain("not a directory"));
        Assert.That(fatal.Reason, Is.EqualTo(FatalReason.WorkspaceNotDirectory));
    }

    [Test]
    public void W3_NoCodecomfyFolder_ReturnsEmpty()
    {
        // W3: Workspace exists, no .codecomfy/ folder
        _fs.AddDirectory(WorkspaceRoot);

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Empty>());
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.None));
    }

    [Test]
    public void W4_NoOutputsFolder_ReturnsEmpty()
    {
        // W4: .codecomfy/ exists, no outputs/ folder
        _fs.AddDirectory(WorkspaceRoot);
        _fs.AddDirectory(@"C:\Projects\MyApp\.codecomfy");

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Empty>());
    }

    [Test]
    public void W5_NoIndexJson_ReturnsEmpty()
    {
        // W5: outputs/ exists, no index.json
        SetupValidWorkspace();

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Empty>());
    }

    #endregion

    #region Index File Tests (I1-I7)

    [Test]
    public void I1_FileMissing_ReturnsEmpty()
    {
        SetupValidWorkspace();
        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.State, Is.TypeOf<GalleryState.Empty>());
    }

    [Test]
    public void I2_ZeroBytes_ReturnsWarning()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, Array.Empty<byte>());

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
        Assert.That(result.Banner.Message, Does.Contain("empty").Or.Contain("corrupt"));
    }

    [Test]
    public void I3_TruncatedJson_ReturnsWarning()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, "{");

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
        Assert.That(result.Banner.Message, Does.Contain("corrupt"));
    }

    [Test]
    public void I4_EmptyItemsArray_ReturnsEmpty()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, ValidIndex("[]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Empty>());
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.None));
    }

    [Test]
    public void I5_ValidItems_ReturnsList()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, ValidIndex($"[{ValidEntry()}]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;
        Assert.That(list.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void I6_PermissionDenied_ReturnsWarning()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, ValidIndex());
        _fs.DenyPermission(IndexPath);

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
        Assert.That(result.Banner.Message, Does.Contain("permission"));
    }

    [Test]
    public void I7_UnsupportedVersion_ReturnsFatal()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, """{"schema_version":"2.0","items":[]}""");

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Fatal>());
        var fatal = (GalleryState.Fatal)result.State;
        Assert.That(fatal.Message, Does.Contain("update"));
        Assert.That(fatal.Reason, Is.EqualTo(FatalReason.UnsupportedVersion));
    }

    #endregion

    #region Entry Validation Tests (E1-E10)

    [Test]
    public void E1_MissingJobId_SkipsEntry()
    {
        SetupValidWorkspace();
        var entry = """{"created_at":"2025-01-15T10:30:00Z","kind":"image","files":[{"path":"x/y.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);

        // All entries skipped = empty with warning
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void E2_MissingCreatedAt_SkipsEntry()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","kind":"image","files":[{"path":"x/y.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void E3_InvalidCreatedAt_SkipsEntry()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"not-a-date","kind":"image","files":[{"path":"x/y.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void E4_MissingKind_SkipsEntry()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","files":[{"path":"x/y.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void E5_UnknownKind_SkipsEntry()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"unknown","files":[{"path":"x/y.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void E6_EmptyFilesArray_SkipsEntry()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"image","files":[],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void E7_MissingSeed_SkipsEntry()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"image","files":[{"path":"x/y.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void E8_AllRequiredFieldsPresent_RendersEntry()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, ValidIndex($"[{ValidEntry()}]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;
        Assert.That(list.Items, Has.Count.EqualTo(1));
        Assert.That(list.Items[0].JobId, Is.EqualTo("abc123"));
    }

    [Test]
    public void E9_MissingPrompt_RendersWithFallback()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, ValidIndex($"[{ValidEntry(prompt: null)}]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;
        Assert.That(list.Items[0].Prompt, Is.EqualTo("(no prompt)"));
    }

    [Test]
    public void E10_SomeSkippedSomeValid_ListWithBanner()
    {
        SetupValidWorkspace();
        var valid1 = ValidEntry(jobId: "valid1");
        var invalid1 = """{"job_id":"bad1"}"""; // Missing required fields
        var invalid2 = """{"job_id":"bad2"}""";
        var invalid3 = """{"job_id":"bad3"}""";
        var valid2 = ValidEntry(jobId: "valid2");
        _fs.AddFile(IndexPath, ValidIndex($"[{valid1},{invalid1},{invalid2},{invalid3},{valid2}]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;
        Assert.That(list.Items, Has.Count.EqualTo(2));

        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Info));
        Assert.That(result.Banner.SkippedCount, Is.EqualTo(3));
        Assert.That(result.Banner.Message, Does.Contain("3"));
    }

    #endregion

    #region File Reference Tests (F1-F6)

    [Test]
    public void F3_TraversalPath_SkipsFile()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"image","files":[{"path":"../../../etc/passwd","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);

        // Entry skipped because no valid files
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void F4_AbsolutePath_SkipsFile()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"image","files":[{"path":"C:\\Windows\\System32\\config","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void F5_EmptyPath_SkipsFile()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"image","files":[{"path":"","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void F6_MissingSha256_SkipsFile()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"image","files":[{"path":"x/y.png"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    [Test]
    public void InvalidSha256Format_SkipsFile()
    {
        SetupValidWorkspace();
        var entry = """{"job_id":"abc","created_at":"2025-01-15T10:30:00Z","kind":"image","files":[{"path":"x/y.png","sha256":"not-valid-hex"}],"seed":1}""";
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
    }

    #endregion

    #region Race Condition Tests (R1-R4)

    [Test]
    public void R1_TruncatedJson_AutoRecovers_KeepsLastKnownGood()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, "{");

        var lastKnown = new List<JobRow>
        {
            new JobRow
            {
                JobId = "prev",
                CreatedAt = DateTimeOffset.UtcNow,
                Kind = JobKind.Image,
                Files = new[] { new FileRef { RelativePath = "x.png", Sha256 = new string('a', 64) } },
                Seed = 1
            }
        };

        var result = _loader.Load(WorkspaceRoot, lastKnown);

        // Shows last known good with warning
        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        Assert.That(result.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));
        Assert.That(result.LastKnownGood, Is.Not.Null);
    }

    [Test]
    public void R4_FailureThenSuccess_ClearsError()
    {
        SetupValidWorkspace();

        // First: failure
        _fs.AddFile(IndexPath, "{");
        var result1 = _loader.Load(WorkspaceRoot);
        Assert.That(result1.Banner.Severity, Is.EqualTo(BannerSeverity.Warning));

        // Second: success
        _fs = new FakeFileReader();
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, ValidIndex($"[{ValidEntry()}]"));
        _loader = new IndexLoader(_fs);

        var result2 = _loader.Load(WorkspaceRoot);

        Assert.That(result2.State, Is.TypeOf<GalleryState.List>());
        Assert.That(result2.Banner.Severity, Is.EqualTo(BannerSeverity.None));
    }

    #endregion

    #region Version Handling Tests

    [Test]
    public void MissingSchemaVersion_TreatsAs01()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, $$$"""{"items":[{{{ValidEntry()}}}]}""");

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
    }

    [Test]
    public void Version0x_BestEffort()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, $$$"""{"schema_version":"0.9","items":[{{{ValidEntry()}}}]}""");

        var result = _loader.Load(WorkspaceRoot);

        // Major version 0 = best effort
        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
    }

    [Test]
    public void Version1x_Fatal()
    {
        SetupValidWorkspace();
        _fs.AddFile(IndexPath, """{"schema_version":"1.0","items":[]}""");

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.Fatal>());
    }

    #endregion

    #region Ordering Tests

    [Test]
    public void DisplayOrder_NewestFirst()
    {
        SetupValidWorkspace();
        var entry1 = ValidEntry(jobId: "first", createdAt: "2025-01-01T00:00:00Z");
        var entry2 = ValidEntry(jobId: "second", createdAt: "2025-01-02T00:00:00Z");
        var entry3 = ValidEntry(jobId: "third", createdAt: "2025-01-03T00:00:00Z");
        _fs.AddFile(IndexPath, ValidIndex($"[{entry1},{entry2},{entry3}]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;

        // Reversed from file order (newest first)
        Assert.That(list.Items[0].JobId, Is.EqualTo("third"));
        Assert.That(list.Items[1].JobId, Is.EqualTo("second"));
        Assert.That(list.Items[2].JobId, Is.EqualTo("first"));
    }

    #endregion

    #region Unicode Tests (U1-U4)

    [Test]
    public void U1_ChinesePrompt_DisplaysCorrectly()
    {
        SetupValidWorkspace();
        var entry = ValidEntry(prompt: "生成一只猫");
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;
        Assert.That(list.Items[0].Prompt, Is.EqualTo("生成一只猫"));
    }

    [Test]
    public void U4_EmojiJobId_DisplaysCorrectly()
    {
        SetupValidWorkspace();
        var entry = ValidEntry(jobId: "job_🎨_123");
        _fs.AddFile(IndexPath, ValidIndex($"[{entry}]"));

        var result = _loader.Load(WorkspaceRoot);

        Assert.That(result.State, Is.TypeOf<GalleryState.List>());
        var list = (GalleryState.List)result.State;
        Assert.That(list.Items[0].JobId, Is.EqualTo("job_🎨_123"));
    }

    #endregion
}
