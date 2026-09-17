# NAS Cloud Sync Client

A Windows Cloud Files API-based sync client for NAS devices, providing OneDrive-like experience in File Explorer with pluggable protocol and NAS system adapters.

## Features

- **File Explorer Integration** — Navigation pane entry with custom branding
- **Placeholder Files** — Virtual files that download on demand (Progressive Hydration)
- **Bidirectional Sync** — Real-time two-way synchronization between local and NAS
- **Offline Support** — Cached files remain available when NAS is unreachable
- **System Tray** — Status indicator with quick access to settings
- **Pluggable Adapters** — Modular protocol (SMB/WebDAV/REST) and NAS system (Synology/QNAP/TrueNAS) support
- **NAS Management UI** — Quota display, share management, snapshot browsing, system monitoring

## Requirements

- Windows 11
- .NET 9 SDK
- Windows App SDK 1.6+

## Project Structure

```
NAS/
├── src/
│   ├── NasSync.Core/              # Sync engine core logic
│   ├── NasSync.CfApi/             # Cloud Files API (CfAPI) P/Invoke wrapper
│   ├── NasSync.Adapters/          # Adapter interfaces and abstractions
│   ├── NasSync.Adapters.Smb/      # SMB/CIFS protocol adapter
│   ├── NasSync.Adapters.WebDav/   # WebDAV protocol adapter
│   ├── NasSync.UI/                # WinUI 3 management interface
│   ├── NasSync.Tray/              # System tray application
│   └── NasSync.Package/           # MSIX packaging project
├── tests/
│   ├── NasSync.Core.Tests/        # Core sync engine unit tests
│   ├── NasSync.CfApi.Tests/       # CfAPI wrapper tests
│   └── NasSync.Adapters.Tests/    # Adapter integration tests
├── resources/
│   ├── icons/                     # Brand icons and state icons
│   └── strings/                   # Localization (zh-CN, en-US)
├── docs/                          # Public documentation
├── Directory.Build.props          # Shared MSBuild properties
├── NasSync.sln                    # Solution file
└── README.md
```

## Building

```bash
dotnet restore
dotnet build
dotnet publish src/NasSync.Package/NasSync.Package.wapproj
```

## Testing

```bash
dotnet test
```

## License

See [LICENSE](LICENSE) for details.
