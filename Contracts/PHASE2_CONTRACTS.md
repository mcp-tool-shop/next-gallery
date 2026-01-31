# Phase 2 Contracts Summary

> All contracts required for NextGallery + VS Code integration.

## Contract Documents

| Contract | File | Test Vectors |
|----------|------|--------------|
| Workspace Normalization v0.2 | `test-vectors/workspace_normalization.v0.2.json` | 16 vectors |
| Gallery Rendering Protocol v0.1 | `docs/gallery-rendering-protocol.v0.1.md` | 33 vectors (W/I/E/F/R/P/U) |
| Single-Instance Routing v0.1 | `docs/single-instance-routing.v0.1.md` | 20 scenarios (S1-S20) |

## Implementation Order

Fastest feedback loop - each step validates the previous:

### 1. Workspace Key (routing prerequisite)
- Implement `NormalizeWorkspacePath()` and `ComputeWorkspaceKey()`
- Run 16 workspace vectors
- **Done when**: All vectors pass

### 2. Single-Instance Plumbing
- Implement mutex acquisition
- Implement named pipe server/client
- Implement `HandleSecondInstanceActivation()`
- Run S1-S15 scenarios
- **Done when**: All scenarios pass

### 3. Rendering Loader
- Implement index-only loader
- Implement entry validation (skip malformed)
- Run E1-E10, F1-F6 vectors
- **Done when**: All entry/file vectors pass

### 4. UI States + Banners
- Implement 5 UI states (Loading, Empty, List, Error Banner, Fatal Error)
- Implement "N skipped" banner
- Run W1-W5, I1-I7 vectors
- **Done when**: All workspace/index vectors pass

### 5. Polling/Backoff
- Implement refresh on focus-gained
- Implement optional timer poll with backoff
- Run R1-R4 race condition vectors
- **Done when**: Race vectors pass, no UI flicker

## Test Harnesses Required

### WorkspaceNormalizationVectorsRunner (C#)
```csharp
[TestFixture]
public class WorkspaceNormalizationTests
{
    private TestVectors _vectors;

    [OneTimeSetUp]
    public void LoadVectors()
    {
        var json = File.ReadAllText("test-vectors/workspace_normalization.v0.2.json");
        _vectors = JsonSerializer.Deserialize<TestVectors>(json);
    }

    [Test]
    public void AllVectors_ProduceCorrectCanonPath()
    {
        foreach (var v in _vectors.Vectors)
        {
            var canon = WorkspaceNormalizer.Normalize(v.Step1FullPath);
            Assert.AreEqual(v.CanonPath, canon, $"Vector {v.Id}");
        }
    }

    [Test]
    public void AllVectors_ProduceCorrectWorkspaceKey()
    {
        foreach (var v in _vectors.Vectors)
        {
            var key = WorkspaceNormalizer.ComputeKey(v.Step1FullPath);
            Assert.AreEqual(v.WorkspaceKey, key, $"Vector {v.Id}");
        }
    }
}
```

### GalleryRenderingProtocolRunner (C#)
```csharp
[TestFixture]
public class GalleryRenderingTests
{
    [Test]
    [TestCase("W1", null, UIState.FatalError)]           // workspace doesn't exist
    [TestCase("W3", null, UIState.Empty)]                // no .codecomfy folder
    [TestCase("I1", null, UIState.Empty)]                // index missing
    [TestCase("I2", "", UIState.ErrorBanner)]            // 0 bytes
    [TestCase("I3", "{", UIState.ErrorBanner)]           // truncated
    [TestCase("I4", EMPTY_INDEX, UIState.Empty)]         // empty items
    [TestCase("I5", VALID_INDEX, UIState.List)]          // valid
    public void IndexState_ProducesExpectedUIState(string vectorId, string? indexContent, UIState expected)
    {
        var loader = new TestIndexLoader(indexContent);
        var result = loader.Load();
        Assert.AreEqual(expected, result.UIState, $"Vector {vectorId}");
    }
}
```

### SingleInstanceRoutingScenarioRunner (C#)
```csharp
[TestFixture]
public class SingleInstanceRoutingTests
{
    [Test]
    public void S1_FirstLaunch_CreatesNewWindow()
    {
        var registry = new MockInstanceRegistry();
        var result = InstanceRouter.Route("88b49a59944589bd4779b7931d127abc", registry);

        Assert.AreEqual(RouteAction.CreateWindow, result.Action);
        Assert.IsTrue(registry.MutexAcquired);
    }

    [Test]
    public void S2_SecondLaunch_ActivatesExisting()
    {
        var registry = new MockInstanceRegistry { HasExistingInstance = true };
        var result = InstanceRouter.Route("88b49a59944589bd4779b7931d127abc", registry);

        Assert.AreEqual(RouteAction.ActivateExisting, result.Action);
        Assert.IsTrue(registry.ActivationMessageSent);
    }

    [Test]
    public void S12_OrphanMutex_ClaimsAndCreatesWindow()
    {
        var registry = new MockInstanceRegistry {
            HasExistingInstance = true,
            PingTimesOut = true  // Instance is dead
        };
        var result = InstanceRouter.Route("88b49a59944589bd4779b7931d127abc", registry);

        Assert.AreEqual(RouteAction.CreateWindow, result.Action);
        Assert.IsTrue(registry.OrphanDetected);
    }
}
```

## Ship Criteria

Phase 2 is **done** when:

### Automated Tests
- [ ] 16/16 workspace normalization vectors pass
- [ ] 33/33 gallery rendering vectors pass
- [ ] 20/20 single-instance routing scenarios pass

### Manual Smoke Test (Windows)
- [ ] Open Gallery for workspace with no `.codecomfy/outputs` → Empty state
- [ ] Run first job in CodeComfy → index appears → Gallery list updates
- [ ] Launch second Gallery for same workspace → existing window focuses
- [ ] Corrupt index (truncate JSON) → Error banner shown
- [ ] Fix index → Auto-recovers, list displays
- [ ] Close Gallery → Re-open → Same workspace key routes correctly

### Performance
- [ ] 1000 items renders < 500ms
- [ ] No UI freeze during index load
- [ ] Polling doesn't cause flicker

## Contract Stability Rules

1. **Test vector IDs are permanent** - W1 always means "workspace doesn't exist"
2. **New cases append** - Add W6, not renumber W1-W5
3. **Behavior changes require version bump** - v0.1 → v0.2 with migration notes
4. **Unknown fields are ignored** - Forward-compatible by default

## Quick Reference

### Workspace Key Formula
```
key = sha256(UTF8(NFC(canon_path))).hex()[0:32]
```

### Index Path
```
{workspace_root}/.codecomfy/outputs/index.json
```

### Mutex Name
```
Global\NextGallery_{workspace_key}
```

### Pipe Name
```
\\.\pipe\NextGallery_{workspace_key}
```
