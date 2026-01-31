# Gallery Rendering Protocol v0.1

> Contract between CodeComfy (writer) and NextGallery (reader) for rendering job outputs.

## Gallery MUST NOT (Scope Boundaries)

To preserve Phase 2 simplicity and prevent scope creep:

| Prohibited Action | Rationale |
|-------------------|-----------|
| Read per-job debug files (`jobs/{id}/job.json`, `events.jsonl`) | Gallery is a viewer, not a debugger |
| Infer jobs from filesystem scanning | Index is the single source of truth |
| Rewrite or "repair" `index.json` | Writer owns the file |
| Sort or reorder the index file | Append-only contract preserved |
| Delete entries or prune old items | Writer decides retention policy |
| Validate file existence during render | Fast path - metadata only |
| Verify SHA-256 hashes during render | Expensive - deferred to explicit action |

## Index Path Resolution

```
INDEX_PATH = {workspace_root}/.codecomfy/outputs/index.json
```

- `workspace_root` is passed via `--workspace` argument or derived from `CODECOMFY_WORKSPACE_KEY`
- Path uses forward slashes internally, converted to platform separators for I/O
- Index path is **deterministic** - no discovery, no fallbacks

## Single Source of Truth

Gallery renders **only** from `index.json`. It MUST NOT read:

| File Type | Purpose | Gallery Access |
|-----------|---------|----------------|
| `index.json` | Display data | **READ** |
| `jobs/{id}/job.json` | Debug/audit | NO |
| `jobs/{id}/events.jsonl` | Debug/audit | NO |
| `jobs/{id}/*.tmp` | Temp artifacts | NO |

Rationale: Gallery is a viewer, not a debugger. Per-job files are implementation details.

## Index Structure

```json
{
  "schema_version": "0.1",
  "updated_at": "2025-01-15T10:30:00Z",
  "items": [
    {
      "job_id": "abc123",
      "created_at": "2025-01-15T10:30:00Z",
      "kind": "image",
      "files": [
        {
          "path": "abc123/image_0.png",
          "sha256": "...",
          "content_type": "image/png",
          "width": 1024,
          "height": 1024,
          "size_bytes": 1234567
        }
      ],
      "prompt": "a cat",
      "seed": 42,
      "preset_id": "flux_schnell",
      "elapsed_seconds": 12.5
    }
  ]
}
```

## Ordering Contract

| Writer (CodeComfy) | Reader (Gallery) |
|--------------------|------------------|
| Appends new entries to **end** of `items` array | Displays in **reverse** order (newest first) |
| Never reorders existing entries | Never writes to index |
| Updates `updated_at` on each write | Uses `updated_at` for cache invalidation |

Gallery reverses the array in memory for display. This keeps the file append-friendly while showing newest-first UI.

## Required Fields for Rendering

Gallery MUST handle missing optional fields gracefully.

### Minimum Required Fields (entry is malformed if any missing/invalid)

| Field | Type | Validation |
|-------|------|------------|
| `job_id` | string | Non-empty |
| `created_at` | string | Valid ISO 8601 |
| `kind` | string | One of: `"image"`, `"video"` |
| `files` | array | At least one valid file |
| `seed` | integer | Any integer |

### File Object Required Fields

| Field | Type | Validation |
|-------|------|------------|
| `files[].path` | string | Non-empty, no `..` segments |
| `files[].sha256` | string | 64 hex characters |

### Optional Fields with Fallbacks

| Field | Fallback if Missing |
|-------|---------------------|
| `prompt` | Display "(no prompt)" |
| `negative_prompt` | Omit from display |
| `preset_id` | Display "unknown" |
| `elapsed_seconds` | Omit from display |
| `width`, `height` | Read from file or show "?" |
| `content_type` | Infer from extension |
| `size_bytes` | Read from file or omit |
| `tags` | Empty array `[]` |
| `favorite` | `false` |
| `notes` | Empty string `""` |

### Rendering Without File I/O

**Rule**: Gallery renders using index metadata only. It does NOT:
- Validate file existence during list rendering
- Verify SHA-256 hashes during list rendering
- Read image dimensions from files (use `width`/`height` from index, or show "?")

File I/O happens only when:
- User clicks to view full image (lazy load)
- User explicitly requests hash verification (future feature)
- Thumbnail generation (optional, background task)

This keeps rendering fast and avoids extra failure modes.

### Malformed Entry Handling

**Rule**: If any required field is missing or invalid, the entry is malformed and is **skipped silently** from the list.

**Safety valve**: If any entries were skipped during load, show a non-fatal banner:

> "Some items couldn't be displayed (3 skipped)"
>
> [Details...] → expands to show first error reason(s)

**Banner behavior (deterministic)**:
- Banner shows only after a **successful** parse completes
- Reports skip count for the **current** load only
- Clears on next load if skip count goes to 0
- Does NOT flicker during polling (only updates on state change)

This preserves clean UX while surfacing contract violations during development/rollout.

## UI States

Gallery has exactly 5 states:

### 1. Loading
- Shown briefly while reading index
- Spinner or skeleton UI
- Timeout: 5 seconds → transition to Error

### 2. Empty
- **Condition**: `items` array is empty OR index file doesn't exist
- **Title**: "Waiting for first generation..."
- **Subtitle**: "No jobs yet. Run a generation to see results here."
- **Actions**: Refresh button (optional)

### 3. List
- **Condition**: `items.length > 0` with at least one valid entry
- Display job cards in reverse chronological order
- Lazy-load thumbnails

### 4. Error Banner (non-fatal)
- **Condition**: Index exists but has issues (parse error, permission denied)
- Show warning banner at top of list (or empty state)
- **Message**: "Index is unreadable: {reason}"
- **Actions**: "Open Folder" button, "Retry" button
- Don't block the UI - user can still interact

### 5. Fatal Error
- **Condition**: Workspace path invalid or inaccessible
- Full-screen error state
- **Message**: "Cannot access workspace: {path}"
- **Actions**: "Choose Different Workspace" or exit

## Error Severity Table

| Condition | Severity | UI State | User Action |
|-----------|----------|----------|-------------|
| Workspace path doesn't exist | FATAL | Fatal Error | Choose new workspace |
| Workspace not a directory | FATAL | Fatal Error | Choose new workspace |
| `.codecomfy/` folder missing | NONE | Empty | Wait for generation |
| `index.json` missing | NONE | Empty | Wait for generation |
| `index.json` 0 bytes | WARNING | Error Banner | "Index is empty/corrupt" - Retry |
| `index.json` truncated/invalid JSON | WARNING | Error Banner | "Index is corrupt" - Open folder |
| `index.json` permission denied | WARNING | Error Banner | Check permissions |
| `items` array empty | NONE | Empty | Wait for generation |
| Individual entry malformed | NONE | Skip + count | Show "N skipped" banner |
| Individual file missing | NONE | Show placeholder | (silent) |

### Distinguishing Empty vs Corrupt

| File State | Detection | Severity |
|------------|-----------|----------|
| File doesn't exist | `FileNotFoundException` | NONE (Empty state) |
| File exists, 0 bytes | `file.Length == 0` | WARNING (corrupt) |
| File exists, valid JSON, empty items | `items.Length == 0` | NONE (Empty state) |
| File exists, invalid JSON | Parse exception | WARNING (corrupt) |

**Rationale**: 0-byte file indicates writer crash mid-write. This is corruption, not "no data yet."

## Refresh Behavior

Gallery refreshes index on:

1. **App launch** - always
2. **Window activation** (focus gained) - always
3. **Manual Refresh button** - on user click
4. **Timer poll** (optional) - every 3 seconds if implemented

### Timer Poll Rules (if implemented)

- Poll only when window is visible/focused
- **Stability check**: Only re-render if `LastWriteTime` changed (cheap win)
- On failure: log warning, skip this cycle, retry next cycle
- Never show repeated error banners on consecutive failures
- **Backoff**: After 3 consecutive failures, switch to manual-refresh-only until focus-gained
- Reset poll timer and failure count after successful read or focus-gained refresh

### Writer Race Condition

Gallery may read while CodeComfy is mid-write, resulting in temporary invalid JSON.

**Expected behavior**:
- Show Error Banner (non-fatal): "Index temporarily unavailable"
- Auto-recover on next successful read (no restart needed)
- Keep last-known-good list visible during transient errors (optional but recommended)

## File Path Resolution

### Directory Structure

```csharp
// C# - canonical path computation
var outputsDir = Path.Combine(workspaceRoot, ".codecomfy", "outputs");
var indexPath = Path.Combine(outputsDir, "index.json");
```

```python
# Python - canonical path computation
outputs_dir = workspace_root / ".codecomfy" / "outputs"
index_path = outputs_dir / "index.json"
```

### Artifact Path Resolution

File paths in `files[].path` are **relative to outputs directory**:

```
ABSOLUTE_PATH = {workspace_root}/.codecomfy/outputs/{files[].path}
```

Example:
- Workspace: `C:\Projects\MyApp`
- Relative path: `abc123/image_0.png`
- Absolute: `C:\Projects\MyApp\.codecomfy\outputs\abc123\image_0.png`

### Traversal Protection (Testable)

Gallery MUST enforce containment using **path-aware checks**, not string prefix:

```csharp
// C# - CORRECT containment check
var outputsResolved = Path.GetFullPath(outputsDir);
var candidateResolved = Path.GetFullPath(candidatePath);

// Use Uri or path segment check, NOT StartsWith
var outputsUri = new Uri(outputsResolved + Path.DirectorySeparatorChar);
var candidateUri = new Uri(candidateResolved);
bool isContained = outputsUri.IsBaseOf(candidateUri);
```

```python
# Python - CORRECT containment check
outputs_resolved = outputs_dir.resolve()
candidate_resolved = candidate_path.resolve()

# relative_to raises ValueError if not contained
candidate_resolved.relative_to(outputs_resolved)
```

Gallery MUST NOT:
- Accept user-provided relative paths for index location
- Use naive string prefix checks (vulnerable to `/output_evil/` bypass)
- Follow symlinks that resolve outside outputs directory
- Access paths with `..` segments

## Thumbnail Generation

Gallery MAY generate thumbnails for faster loading:

- Store in: `{workspace_root}/.codecomfy/cache/thumbnails/`
- Filename: `{sha256}.thumb.{ext}`
- Size: 256x256 max, preserve aspect ratio
- Cache invalidation: by SHA-256 (content-addressed)

Thumbnails are optional - Gallery can render from full images if simpler.

## Version Compatibility

Index includes `schema_version` field (string like `"0.1"`).

### Parsing Rules

```
Parse schema_version as "major.minor"

if major == 0:
    # Development versions - best effort
    Parse all known fields, ignore unknown fields

if major >= 1 and version not in SUPPORTED_VERSIONS:
    # Breaking change - cannot safely parse
    Show Fatal Error: "This gallery version needs an update to read this index"
    Provide upgrade link/action

if schema_version missing:
    # Legacy file - assume 0.1
    Treat as version "0.1"
```

### Version Table

| Index Version | Gallery Action |
|---------------|----------------|
| `0.1` | Full support |
| `0.x` (x > 1) | Best effort - ignore unknown fields |
| `1.x` (unsupported) | Fatal Error with upgrade prompt |
| Missing | Treat as `0.1` |

**Rationale**: Major version bump (0.x → 1.x) indicates breaking schema changes. Gallery cannot safely render without upgrade.

## Test Vectors

Concrete test cases with exact inputs and expected UI states.

### Workspace/Folder Tests

| ID | Setup | Expected UI State |
|----|-------|-------------------|
| W1 | `--workspace C:\nonexistent` | Fatal Error: "Cannot access workspace" |
| W2 | `--workspace C:\file.txt` (file, not dir) | Fatal Error: "Workspace is not a directory" |
| W3 | Workspace exists, no `.codecomfy/` folder | Empty: "Waiting for first generation..." |
| W4 | `.codecomfy/` exists, no `outputs/` folder | Empty: "Waiting for first generation..." |
| W5 | `outputs/` exists, no `index.json` | Empty: "Waiting for first generation..." |

### Index File Tests

| ID | `index.json` Content | Expected UI State |
|----|---------------------|-------------------|
| I1 | File missing | Empty |
| I2 | 0 bytes | Error Banner: "Index is empty/corrupt" |
| I3 | `{` (truncated) | Error Banner: "Index is corrupt" |
| I4 | `{"schema_version":"0.1","items":[]}` | Empty |
| I5 | `{"schema_version":"0.1","items":[...valid...]}` | List |
| I6 | Permission denied | Error Banner: "Cannot read index" |
| I7 | `{"schema_version":"2.0","items":[]}` | Fatal Error: "Update required" |

### Entry Validation Tests

| ID | Entry Content | Expected Behavior |
|----|---------------|-------------------|
| E1 | Missing `job_id` | Skip entry, increment skip count |
| E2 | Missing `created_at` | Skip entry |
| E3 | Invalid `created_at` (not ISO 8601) | Skip entry |
| E4 | Missing `kind` | Skip entry |
| E5 | `kind: "unknown"` | Skip entry |
| E6 | Empty `files` array | Skip entry |
| E7 | Missing `seed` | Skip entry |
| E8 | All required fields present | Render entry |
| E9 | Missing `prompt` | Render with "(no prompt)" |
| E10 | 3 entries skipped, 7 valid | List with 7 items + banner "3 items skipped" |

### File Reference Tests

| ID | `files[].path` | Expected Behavior |
|----|----------------|-------------------|
| F1 | `abc123/image_0.png` (exists) | Show image |
| F2 | `abc123/image_0.png` (missing) | Show placeholder |
| F3 | `../../../etc/passwd` | Skip file (traversal) |
| F4 | `C:\Windows\System32\config` | Skip file (absolute path) |
| F5 | Empty string | Skip file |
| F6 | Missing `sha256` | Skip file |

### Race Condition Tests

| ID | Scenario | Expected Behavior |
|----|----------|-------------------|
| R1 | Read during write (truncated JSON) | Error Banner, auto-recover on next read |
| R2 | Read during write (valid partial) | Show available entries |
| R3 | 3 consecutive poll failures | Switch to manual-refresh mode |
| R4 | Poll failure then success | Clear error, show list |

### Performance Tests

| ID | Scenario | Expected Behavior |
|----|----------|-------------------|
| P1 | 1000 items | Renders < 500ms, no UI freeze |
| P2 | 10,000 items | Virtualized list, smooth scroll |
| P3 | 100 items, 50 missing files | Renders quickly, 50 placeholders |

### Unicode/Edge Case Tests

| ID | Content | Expected Behavior |
|----|---------|-------------------|
| U1 | Prompt: `"生成一只猫"` (Chinese) | Displays correctly |
| U2 | Path: `outputs/项目/image.png` | Resolves correctly |
| U3 | Prompt: 2000 characters | Truncated with "..." |
| U4 | `job_id` with emoji: `"job_🎨_123"` | Displays correctly |

## Testing Checklist

Gallery implementation MUST pass all test vectors above. Summary:

- [ ] W1-W5: Workspace/folder edge cases
- [ ] I1-I7: Index file states
- [ ] E1-E10: Entry validation
- [ ] F1-F6: File reference handling
- [ ] R1-R4: Race conditions
- [ ] P1-P3: Performance
- [ ] U1-U4: Unicode/edge cases

## Test Vector ID Stability

**Rules for test vector IDs:**

| Rule | Example |
|------|---------|
| IDs are stable and never reused | W1 always means "workspace doesn't exist" |
| New cases append | Add E11, E12... not renumber E1-E10 |
| Removed cases are deprecated, not deleted | Mark as `[DEPRECATED]` with reason |
| Behavior changes require new protocol version | v0.2 with migration notes |

This prevents "soft rewrites" that silently change expectations.

## Protocol Versioning

| Version | Status | Notes |
|---------|--------|-------|
| v0.1 | **CURRENT** | Initial release |

When updating this protocol:
1. Bump version (v0.1 → v0.2)
2. Add migration notes for breaking changes
3. Keep old test vectors marked `[DEPRECATED]` if behavior changed
4. Update `schema_version` in index if format changes
