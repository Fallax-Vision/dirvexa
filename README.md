# Dirvexa

Dirvexa is a free and open-source, private-by-design filesystem analyzer for
Windows, released under the MIT License. It scans a local folder or drive,
keeps a searchable snapshot in SQLite, and lets you inspect
folder structure, file categories, hidden items, sizes, and stale results
without modifying the scanned content.

## Current feature set

- One-window Scan, History, and Settings navigation.
- Real one-pass scanning with live files, folders, logical bytes, elapsed time,
  current path, and recoverable issue counts.
- Cancellation with keep-partial, discard, or continue choices.
- Full saved snapshots with lazy folder navigation, search, category/hidden
  filters, server-side sorting, and paged rows.
- Read-only copy path, open, and reveal-in-Explorer actions.
- Newest-20 history retention with individual and clear-all deletion.
- Immediate System, Light, and Dark themes.
- Manual, anonymous GitHub release checks with no automatic download or execution.
- GPU-first Windows rendering with a safe software fallback.
- Local-only SQLite data and diagnostics under `%LOCALAPPDATA%\Dirvexa`.
- Self-contained portable and NSIS setup targets for Windows x64 and ARM64.

Dirvexa never follows junctions or symbolic links, elevates itself, changes
scanned files, or sends telemetry. Network access occurs only when the user
explicitly checks for updates; no local scan information is transmitted.

## Requirements and support

- Development: .NET 10 SDK.
- Official runtime target: Windows 11 x64 and ARM64.
- Windows 10 22H2 x64: best-effort compatibility only because ordinary Windows
  10 is outside the current .NET 10 support matrix.
- Installer builds: NSIS 3 (`makensis.exe`). Portable builds do not require NSIS.

## Build and test

```powershell
dotnet restore Dirvexa.slnx
dotnet build Dirvexa.slnx -c Release
dotnet test Dirvexa.slnx -c Release
```

Run the development build:

```powershell
dotnet run --project src/Dirvexa.App/Dirvexa.App.csproj
```

Build portable and setup artifacts:

```powershell
./scripts/Build-Windows.ps1 -Version 0.2.0
```

The checked-in app icon is generated from the symmetrical directory-focus mark
in `src/Dirvexa.App/Assets/DirvexaIcon.svg`. Regenerate the multi-resolution
Windows icon and PNG preview after changing its geometry:

```powershell
./scripts/Generate-AppIcon.ps1
```

User-facing outputs are written to three folders under `artifacts/windows/`:

- `Installers`: recommended setup executables. Most PCs use the newest
  `Dirvexa-Setup-<version>-win-x64.exe`; Windows on ARM devices use `win-arm64`.
- `Portable`: no-install executables that can run directly from any writable
  folder. Choose the architecture in the same way.
- `Checksums`: optional SHA-256 verification files for advanced users.

`artifacts/windows/README.txt` repeats this guidance beside every build. Publish
staging is temporary and is never presented as a downloadable application. If
NSIS is unavailable, the script still produces both portable executables and
reports that setup creation was skipped. The build version must match the
project version. After a successful versioned build, the output folders retain
only the current and immediately previous semantic versions.

Optional Authenticode signing is enabled only when
`DIRVEXA_SIGN_CERT_THUMBPRINT` is set and `signtool.exe` is available. No
certificate or signing secret belongs in this repository.

## Architecture

- `src/Dirvexa.Core`: platform-neutral models, contracts, path validation,
  classification, formatting, and iterative scanner.
- `src/Dirvexa.App`: Avalonia MVVM UI, SQLite persistence, LocalAppData settings,
  and Windows file actions.
- `tests/Dirvexa.Tests`: unit and integration coverage.
- `packaging/windows`: NSIS installer source.
- `plans`: approved implementation plans.

## Privacy and data

Scan snapshots are derived metadata and may contain local paths. They remain in
`%LOCALAPPDATA%\Dirvexa\data\dirvexa.db`. Settings live in
`%LOCALAPPDATA%\Dirvexa\settings.json`, and bounded diagnostic logs live in the
`logs` subfolder. See [PRIVACY.md](PRIVACY.md) for the complete boundary.

## License

Dirvexa is free and open-source software released under the MIT License. See
[LICENSE](LICENSE).
