# Single-Instance Routing Protocol v0.1

> Contract for NextGallery single-instance behavior per workspace.

## Core Principle

**One Gallery window per workspace.** Multiple invocations targeting the same workspace key MUST reuse the existing instance.

## Workspace Key

Workspace identity is determined by the **workspace key** (32 hex chars) computed using the [Workspace Normalization v0.2](../test-vectors/workspace_normalization.v0.2.json) algorithm:

```
workspace_key = sha256(UTF8(NFC(canon_path))).hex()[0:32]
```

Two paths producing the same key = same workspace = same instance.

## Invocation Methods

Gallery can be invoked via:

| Method | Example |
|--------|---------|
| Command line | `next-gallery.exe --workspace C:\Projects\MyApp` |
| Protocol handler | `codecomfy://open?workspace=C:\Projects\MyApp` |
| File association | Double-click `.codecomfy` folder (future) |

All methods resolve to a workspace key for routing.

## Instance Registry

Gallery maintains an instance registry using **named mutex + shared memory** (or app service on Windows):

```
Mutex name: Global\NextGallery_{workspace_key}
```

### Registry Operations

| Operation | Mechanism |
|-----------|-----------|
| Check if instance exists | `TryOpenExisting(mutex_name)` |
| Register new instance | `CreateMutex(mutex_name)` |
| Unregister on exit | Release mutex |

## Activation Flow

```
START
  │
  ├─ Compute workspace_key from --workspace argument
  │
  ├─ Try acquire mutex "Global\NextGallery_{workspace_key}"
  │     │
  │     ├─ SUCCESS (no existing instance)
  │     │     │
  │     │     └─ Create new window
  │     │        Register in instance registry
  │     │        Show gallery for workspace
  │     │
  │     └─ FAILURE (instance exists)
  │           │
  │           └─ Send activation message to existing instance
  │              Exit this process
  │
END
```

## Activation Message Protocol

### Message Envelope

All messages use a versioned JSON envelope:

```json
{
  "protocol_version": "1",
  "message_type": "activation_request",
  "workspace_key": "88b49a59944589bd4779b7931d127abc",
  "payload": { ... },
  "timestamp": "2025-01-15T10:30:00.000Z"
}
```

### Envelope Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `protocol_version` | string | Yes | Message protocol version (currently `"1"`) |
| `message_type` | string | Yes | One of: `activation_request`, `activation_response`, `ping`, `pong` |
| `workspace_key` | string | Yes | 32 lowercase hex chars - MUST match pipe name |
| `payload` | object | Yes | Message-type-specific data |
| `timestamp` | string | Yes | ISO 8601 with milliseconds |

### workspace_key Format (Canonical)

```
workspace_key = /^[a-f0-9]{32}$/
```

- **Lowercase hex only** (no uppercase)
- **Exactly 32 characters** (not full SHA-256)
- **No prefixes** (no `0x`, no `sha256:`)

Example: `88b49a59944589bd4779b7931d127abc`

### Pipe Name Derivation

Pipe name is derived **deterministically** from workspace_key:

```
pipe_name = "\\.\pipe\codecomfy.nextgallery." + workspace_key
```

Example:
```
workspace_key: 88b49a59944589bd4779b7931d127abc
pipe_name:     \\.\pipe\codecomfy.nextgallery.88b49a59944589bd4779b7931d127abc
```

**Rule**: Pipe name contains NO path components, only the workspace_key.

### Message Types

#### `activation_request`

Sent by new process to existing instance:

```json
{
  "protocol_version": "1",
  "message_type": "activation_request",
  "workspace_key": "88b49a59944589bd4779b7931d127abc",
  "payload": {
    "workspace_path": "C:\\Projects\\MyApp",
    "requested_view": "jobs",
    "args": ["--focus"]
  },
  "timestamp": "2025-01-15T10:30:00.000Z"
}
```

| Payload Field | Type | Required | Description |
|---------------|------|----------|-------------|
| `workspace_path` | string | Yes | Original path (for display/logging) |
| `requested_view` | string | No | View to navigate to: `"jobs"`, `"settings"`, `"gallery"` |
| `args` | string[] | No | Additional CLI args (for future use) |

#### `activation_response`

Sent by existing instance back to requester:

```json
{
  "protocol_version": "1",
  "message_type": "activation_response",
  "workspace_key": "88b49a59944589bd4779b7931d127abc",
  "payload": {
    "status": "activated",
    "window_state": "restored",
    "navigated_to": "jobs"
  },
  "timestamp": "2025-01-15T10:30:00.050Z"
}
```

| Payload Field | Type | Description |
|---------------|------|-------------|
| `status` | string | `"activated"`, `"error"`, `"busy"` |
| `window_state` | string | `"restored"`, `"already_foreground"`, `"minimized"` |
| `navigated_to` | string | View that was navigated to (or current view) |
| `error` | string | Error message if status is `"error"` |

#### `ping` / `pong`

For orphan detection:

```json
// ping
{
  "protocol_version": "1",
  "message_type": "ping",
  "workspace_key": "88b49a59944589bd4779b7931d127abc",
  "payload": {},
  "timestamp": "2025-01-15T10:30:00.000Z"
}

// pong (response)
{
  "protocol_version": "1",
  "message_type": "pong",
  "workspace_key": "88b49a59944589bd4779b7931d127abc",
  "payload": {
    "process_id": 12345,
    "uptime_seconds": 3600
  },
  "timestamp": "2025-01-15T10:30:00.010Z"
}
```

### Version Compatibility

| Received Version | Action |
|------------------|--------|
| `"1"` | Full support |
| `"2"`, `"3"`, ... | Attempt best-effort parse, ignore unknown payload fields |
| Missing/invalid | Reject message, log warning |

**Rule**: Unknown fields in `payload` are ignored (forward-compatible). Envelope fields are strict.

### Envelope Strictness (Executable Rules)

| Condition | Action |
|-----------|--------|
| `protocol_version` unsupported (not `"1"`) | Respond with `activation_response` status `"error"` |
| Required envelope field missing | Drop message, log warning |
| `message_type` unknown | Drop message, log warning |
| `workspace_key` invalid format (not 32 lowercase hex) | Drop message, log warning |
| `workspace_key` doesn't match pipe's key | Drop message, log warning (possible attack) |
| Message exceeds 64KB | Drop message, log warning |

**"Drop"** means: do not process, do not respond (except for unsupported version which gets an error response).

### Timestamp Semantics

**Rule**: `timestamp` is **diagnostic only** in v0.1.

- NOT used for ordering
- NOT used for freshness/expiry
- NOT used for replay prevention
- Used only for logging and debugging

Future versions MAY add replay prevention, but v0.1 does not.

### Message Size Limits

| Limit | Value | Action if Exceeded |
|-------|-------|-------------------|
| Max message size | 64KB | Drop + log warning |
| Max `args` array length | 100 | Truncate to 100 |
| Max `workspace_path` length | 32KB | Truncate |

### Extension Semantics

To add new functionality:

1. **New payload fields**: Add to existing message type, receiver ignores unknown
2. **New message types**: Add new `message_type` value, old receivers ignore
3. **Breaking changes**: Bump `protocol_version` to `"2"`

### Message Delivery (Windows)

**Primary**: Named Pipe

```
Pipe name: \\.\pipe\NextGallery_{workspace_key}
```

**Pipe Configuration**:
- Mode: `PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE`
- Max instances: 1 (single server per workspace)
- Buffer size: 64KB (generous for JSON messages)
- Timeout: 5000ms for connection

**Fallback**: None in v0.1. If pipe unavailable, create new window (degraded mode).

### Message Flow

```
[New Process]                    [Existing Instance]
     |                                   |
     |  1. Connect to pipe               |
     |---------------------------------->|
     |                                   |
     |  2. Send activation_request       |
     |---------------------------------->|
     |                                   |
     |                    3. Bring window to front
     |                    4. Navigate to view
     |                    5. Refresh index
     |                                   |
     |  6. Receive activation_response   |
     |<----------------------------------|
     |                                   |
     |  7. Exit(0)                       |
     X                                   |
```

### Timeout Handling

| Stage | Timeout | Action on Timeout |
|-------|---------|-------------------|
| Pipe connect | 2s | Assume no instance, create window |
| Send request | 1s | Log error, create window |
| Receive response | 5s | Log warning, exit anyway (instance is handling it) |

## Existing Instance Response

When receiving an activation message, the existing instance MUST:

1. **Bring window to foreground** (`SetForegroundWindow` or equivalent)
2. **Navigate to requested view** (if specified)
3. **Refresh index** (pick up any new jobs)
4. **Flash taskbar** if window was minimized (visual feedback)
5. **Optionally show toast**: "Already open for this workspace"

### HandleSecondInstanceActivation Contract

This is the core function that processes activation requests. Defined as a **testable, deterministic function**:

```csharp
public record ActivationResult(
    List<ActivationOutcome> Outcomes,  // Multiple outcomes possible
    string? NavigatedTo = null,
    string? ErrorMessage = null
);

public enum ActivationOutcome
{
    // Success outcomes (can combine)
    BroughtToFront,           // Window was background, now foreground
    AlreadyForeground,        // Window was already foreground
    RestoredFromMinimized,    // Window was minimized, now restored + foreground
    NavigatedToView,          // Navigated to requested view
    RefreshedIndex,           // Index was refreshed
    TaskbarFlashed,           // Taskbar icon flashed for attention

    // Error outcomes (terminal - no other outcomes)
    ErrorInvalidMessage,      // Message parsing failed
    ErrorUnsupportedVersion,  // Protocol version not supported
    ErrorWindowUnavailable,   // Window handle invalid
    ErrorWorkspaceKeyMismatch,// workspace_key doesn't match this instance
    ErrorMessageTooLarge,     // Message exceeded 64KB
    ErrorInvalidKeyFormat,    // workspace_key not 32 lowercase hex
}

public ActivationResult HandleSecondInstanceActivation(
    ActivationRequest request,
    IWindowManager windowManager,
    IIndexLoader indexLoader
);
```

### Outcome Combinations

Successful activation returns **multiple** outcomes:

| Window State | View Requested | Outcomes |
|--------------|----------------|----------|
| Background | No | `[BroughtToFront, RefreshedIndex]` |
| Background | Yes | `[BroughtToFront, NavigatedToView, RefreshedIndex]` |
| Foreground | No | `[AlreadyForeground, RefreshedIndex]` |
| Foreground | Yes | `[AlreadyForeground, NavigatedToView, RefreshedIndex]` |
| Minimized | No | `[RestoredFromMinimized, TaskbarFlashed, RefreshedIndex]` |
| Minimized | Yes | `[RestoredFromMinimized, TaskbarFlashed, NavigatedToView, RefreshedIndex]` |

Error outcomes are **terminal** (single outcome, no combinations).

### Activation Outcome Matrix

| Window State | Requested View | Expected Outcomes |
|--------------|----------------|-------------------|
| Background | None | `BroughtToFront`, `RefreshedIndex` |
| Background | "jobs" | `BroughtToFront`, `NavigatedToView`, `RefreshedIndex` |
| Foreground | None | `AlreadyForeground`, `RefreshedIndex` |
| Foreground | "settings" | `NavigatedToView`, `RefreshedIndex` |
| Minimized | None | `RestoredFromMinimized`, `RefreshedIndex` |
| Minimized | "gallery" | `RestoredFromMinimized`, `NavigatedToView`, `RefreshedIndex` |

### Testing HandleSecondInstanceActivation

Tests inject mock `IWindowManager` and `IIndexLoader`:

```csharp
[Test]
public void Activation_BringsBackgroundWindowToFront()
{
    var mockWindow = new MockWindowManager { IsMinimized = false, IsForeground = false };
    var mockLoader = new MockIndexLoader();

    var result = handler.HandleSecondInstanceActivation(
        new ActivationRequest { WorkspaceKey = "abc123" },
        mockWindow,
        mockLoader
    );

    Assert.Contains(ActivationOutcome.BroughtToFront, result.Outcomes);
    Assert.IsTrue(mockWindow.BringToFrontCalled);
    Assert.IsTrue(mockLoader.RefreshCalled);
}
```

This keeps activation logic testable without UI automation.

## Activation Scenarios

### S1: First Launch
| Input | `--workspace C:\Projects\MyApp` (no existing instance) |
|-------|--------------------------------------------------------|
| Expected | New window created, shows gallery |

### S2: Same Workspace, Second Launch
| Input | `--workspace C:\Projects\MyApp` (instance exists) |
|-------|---------------------------------------------------|
| Expected | Existing window brought to front, new process exits |

### S3: Different Workspace
| Input | `--workspace C:\Projects\OtherApp` (different key) |
|-------|-----------------------------------------------------|
| Expected | New window created (two windows now open) |

### S4: Same Path, Different Format
| Input | `--workspace C:/Projects/MyApp` (forward slashes) |
|-------|---------------------------------------------------|
| Expected | Routes to existing instance (same key after normalization) |

### S5: Case Variation
| Input | `--workspace c:\PROJECTS\myapp` |
|-------|----------------------------------|
| Expected | Routes to existing instance (case-insensitive key) |

### S6: Trailing Slash Variation
| Input | `--workspace C:\Projects\MyApp\` |
|-------|-----------------------------------|
| Expected | Routes to existing instance (trailing slash stripped) |

### S7: Existing Instance Minimized
| Input | Activation message to minimized window |
|-------|----------------------------------------|
| Expected | Window restored, brought to front, taskbar flash |

### S8: Existing Instance in Error State
| Input | Activation to instance showing Fatal Error |
|-------|---------------------------------------------|
| Expected | Window brought to front (error state preserved) |

### S9: Existing Instance Busy (Loading)
| Input | Activation during index load |
|-------|------------------------------|
| Expected | Window brought to front, continues loading |

### S10: Activation with View Parameter
| Input | `--workspace ... --view settings` |
|-------|-----------------------------------|
| Expected | Navigate to settings view after activation |

### S11: Rapid Multiple Activations
| Input | 5 activations in 1 second |
|-------|---------------------------|
| Expected | Single window, no duplicates, no crash |

### S12: Instance Crashed (Stale Mutex)
| Input | Mutex exists but process dead |
|-------|-------------------------------|
| Expected | Detect orphan, claim mutex, create new window |

### S13: UNC Path Workspace
| Input | `--workspace \\\\server\\share\\project` |
|-------|------------------------------------------|
| Expected | Works correctly (UNC key computed) |

### S14: Workspace Key Collision (Theoretical)
| Input | Two different paths with same 32-char key |
|-------|-------------------------------------------|
| Expected | Same instance used (by design - astronomically unlikely) |

### S15: No Workspace Argument
| Input | `next-gallery.exe` (no --workspace) |
|-------|--------------------------------------|
| Expected | Error: "Workspace required" OR prompt to choose |

### S16: Pipe Name Derivation
| Input | workspace_key `88b49a59944589bd4779b7931d127abc` |
|-------|--------------------------------------------------|
| Expected | Pipe name is `\\.\pipe\codecomfy.nextgallery.88b49a59944589bd4779b7931d127abc` |
| Note | No path components in pipe name, only workspace_key |

### S17: Existing Instance Busy (Index Loading)
| Input | Activation while existing instance is mid-index-load |
|-------|------------------------------------------------------|
| Expected | Activation proceeds, window brought to front, response has `status: "activated"` |
| Note | Loading state doesn't block activation |

### S18: Pipe Reachable But App Hung
| Input | Mutex held, pipe exists, but no response within 5s |
|-------|-----------------------------------------------------|
| Expected | Timeout → log warning → exit (assume instance is handling it) |
| Note | Don't create duplicate window; trust the mutex |

### S19: Pipe Reachable But Wrong Response
| Input | Pipe returns malformed JSON or wrong workspace_key |
|-------|-----------------------------------------------------|
| Expected | Log error, create new window (degraded mode) |
| Note | Wrong workspace_key indicates routing bug |

### S20: Uppercase workspace_key in Message
| Input | Message with `workspace_key: "88B49A59944589BD4779B7931D127ABC"` |
|-------|-------------------------------------------------------------------|
| Expected | Drop message (invalid format), log warning |
| Note | Keys MUST be lowercase

## Error Handling

| Condition | Behavior |
|-----------|----------|
| Mutex creation fails | Log error, proceed with new window (degraded mode) |
| Pipe/service unavailable | Log warning, create new window |
| Activation message times out (5s) | Create new window (assume instance hung) |
| Invalid workspace path | Show error before attempting routing |

## Cleanup on Exit

When Gallery window closes:

1. Release workspace mutex
2. Close named pipe (if using)
3. Deregister from any app service

## Multi-Monitor Behavior

When activating existing instance:

- Window appears on its **current monitor** (don't move it)
- If window is on disconnected monitor, move to primary

## Focus Stealing Prevention

Windows may block `SetForegroundWindow` from background processes.

Workarounds:
1. Use `AllowSetForegroundWindow` before sending activation
2. Flash taskbar instead of stealing focus
3. Use `INPUT` struct to simulate user input (last resort)

## Implementation Notes (Windows/WinAppSDK)

```csharp
// Mutex pattern
var mutexName = $"Global\\NextGallery_{workspaceKey}";
bool createdNew;
var mutex = new Mutex(true, mutexName, out createdNew);

if (!createdNew)
{
    // Instance exists - send activation and exit
    SendActivationMessage(workspaceKey, args);
    Environment.Exit(0);
}

// We own the mutex - proceed with window creation
```

```csharp
// Orphan detection
try
{
    var existing = Mutex.OpenExisting(mutexName);
    // Mutex exists - check if process is alive via named pipe ping
    if (!PingExistingInstance(workspaceKey, timeout: TimeSpan.FromSeconds(2)))
    {
        // Orphan - force release and claim
        existing.ReleaseMutex();
        existing.Dispose();
        // Retry creation
    }
}
catch (WaitHandleCannotBeOpenedException)
{
    // No mutex - we're first
}
```

## Test Vector ID Stability

Same rules as Gallery Rendering Protocol:
- IDs (S1-S20) are stable and never reused
- New scenarios append (S21, S22...)
- Behavior changes require protocol version bump

## Protocol Versioning

| Version | Status | Notes |
|---------|--------|-------|
| v0.1 | **CURRENT** | Initial release |
