# NextGallery Phase 2 RC1 — `nextgallery-phase2-rc1`

This release is the Phase 2 "contracts-first" implementation of NextGallery. The implementation is driven by deterministic contracts and vector/scenario test suites; passing tests is the definition of correctness.

## What's included

### Gate 1 — Workspace Key Normalization
- Workspace key derivation:
  - `workspace_key = sha256( UTF8( NFC(canon_path) ) ).hex()[0:32]`
- Shared cross-platform test vectors:
  - `Contracts/workspace_normalization.v0.2.json`
- 21 normalization tests covering:
  - Windows path edge cases (drive roots, UNC, slashes)
  - Unicode NFC normalization
  - failure conditions (empty/whitespace/null bytes)
  - defensive `///` clamp behavior
  - key format: `/^[a-f0-9]{32}$/`

### Gate 2 — Single-Instance Routing
- Named pipe IPC:
  - `\\.\pipe\codecomfy.nextgallery.{workspace_key}`
- Global mutex:
  - `Global\NextGallery_{workspace_key}`
- Versioned message envelope with strict validation:
  - `protocol_version`, `message_type`, `workspace_key`, `payload`, `timestamp`
  - strict "drop vs respond with error" rules
  - 64KB max message size
  - timestamp is diagnostic only
- Activation handler manages window state correctly (restore/foreground/flash fallback)
- Routing tests: 35 (envelope, transport loopback, orchestration, outcome matrix)

### Gate 3 — Index Loader State Machine
- Single source of truth:
  - reads ONLY: `.codecomfy/outputs/index.json`
- Pure loader (no per-job file I/O for rendering)
- `GalleryState` union:
  - `Loading / Empty / List / Fatal`
- Last-known-good support for transient index read/parse failures
- Banner system (Warning/Info/None) including "N skipped" malformed-entry safety valve
- Loader tests: 38 (covers all rendering protocol vectors)

### Gate 4 — UI Wiring
- `CodeComfyViewModel` is a pure projection of `GalleryState` (no hidden UI state)
- Refresh triggers:
  - launch, focus-gained, manual refresh
  - optional polling with guardrails:
    - `LastWriteTime` change check
    - backoff after 3 failures → manual-refresh mode
- 4 view templates:
  - Loading, Empty, List, Fatal
- Banner always present (collapsed when severity == None)
- Diagnostics panel (Ctrl+Shift+D):
  - workspace path, canon path, workspace key, pipe name
  - polling status + timestamps (index write/load)

## Hardening
- Bulletproof polling cancellation:
  - disposal checks at all callback points
  - no timer callbacks after dispose
- P/Invoke guarded:
  - native calls wrapped in try/catch
  - focus/restore failures fall back to taskbar flash
- Naming clarity:
  - `MauiWindowManager` renamed → `WinUIWindowManager`

## Test coverage
- 115 tests passing:
  - Gate 1–3 contract suites + existing app tests

## Known issues / limitations
- `MVVMTK0045` warnings (AOT/trim compatibility):
  - safe unless enabling trimming/NativeAOT; tracked for future cleanup
- `XC0025` warnings (uncompiled bindings with `Source`):
  - performance-only; no runtime impact in current shipping configuration
- `NU1902` SixLabors.ImageSharp vulnerability warning:
  - pre-existing; tracked separately

## Notes
- Contract files are development/validation artifacts and are not required to be embedded in the shipped app.
- Runtime behavior is validated by the contract-driven test suites; correctness is defined by passing vectors/scenarios.
