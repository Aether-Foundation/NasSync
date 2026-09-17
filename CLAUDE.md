# NAS Cloud Sync Client — CLAUDE.md

## Project Overview

Windows 11 Cloud Files API sync client for NAS devices. Pure C# + WinUI 3, MSIX distribution.
Provides OneDrive-like File Explorer integration with pluggable NAS/protocol adapters.

## Architecture

- **NasSync.Core** — Sync engine: change detection, conflict resolution, hydration orchestration
- **NasSync.CfApi** — P/Invoke wrapper for Windows Cloud Filter API (cldflt.sys)
- **NasSync.Adapters** — `INasAdapter` / `IProtocolAdapter` interfaces
- **NasSync.Adapters.Smb/WebDav** — Concrete adapter implementations
- **NasSync.UI** — WinUI 3 management window (quota, shares, snapshots, monitoring)
- **NasSync.Tray** — System tray icon with status and quick actions
- **NasSync.Package** — MSIX packaging with Desktop Bridge

## Key Decisions

- Hydration policy: **Progressive** + AutoDehydrationAllowed
- Sync direction: **Bidirectional**
- Conflict resolution: Keep conflict copy + notify user
- Offline: Cached files only, placeholders unavailable
- Change detection: FileSystemWatcher primary, periodic scan secondary
- Multi-NAS: Architecture supports it, single NAS as default

## Code Conventions

- **Language**: C# with WinUI 3 (Windows App SDK)
- **Comments**: Detailed English XML doc comments on all public types/members. Inline comments for complex logic.
- **Naming**:
  - Interfaces: `IXxx` (e.g., `ISyncEngine`)
  - ViewModels: `XxxViewModel`
  - Services: `XxxService`
  - Private fields: `_camelCase`
  - Parameters: `camelCase`
  - Constants: `UPPER_SNAKE_CASE`
- **No hardcoded user-visible strings** — use resource files (zh-CN, en-US)

## Git Conventions

- **Format**: Conventional Commits — `type(scope): description`
- **Types**: feat, fix, refactor, docs, test, chore, ci, perf
- **Scopes**: sync, cfapi, ui, adapter, tray, build
- **Branches**: main, develop, feature/*, fix/*, release/*
- **Push policy**: Remote is configured — **always ask the user to review and push**. Never push directly.
- **CI/CD**: GitHub Actions workflow in `.github/workflows/build.yml` handles build, test, MSIX packaging, and release.

## Testing

- xUnit + Moq for unit tests
- WinAppDriver for UI automation
- Target ≥80% coverage on Core and CfApi layers
- Adapters: mock tests + real environment integration tests

## Key API References (in draft/)

- `draft/integrate-cloud-storage.md` — Navigation pane registry steps
- `draft/build-cloud-sync-engine.md` — CfAPI architecture overview
- `draft/cloud-filter-api-reference.md` — CfAPI functions/structs/enums
- `draft/storage-provider-api-reference.md` — WinRT API reference
- `draft/onedrive-sync-behavior.md` — OneDrive sync behavior notes
