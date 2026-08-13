# Dirvexa Windows Desktop App

## Summary

Build Dirvexa as an offline filesystem-analysis desktop app using .NET 10,
Avalonia 12.1, C#, XAML, MVVM, and SQLite. The first release targets Windows
x64 and ARM64 while preserving a later path to Linux and macOS.

The app uses one resizable window. Scan, History, and Settings remain directly
navigable; Progress and Results replace the Scan workspace contextually. An
active scan continues when the user visits another section.

## User experience

- Accept pasted or selected absolute local folders and drive roots. Reject
  files, relative paths, UNC paths, mapped network drives, and unavailable roots.
- Show five recent scans, allow one active scan, and report honest one-pass live
  activity using files, folders, bytes, elapsed time, current path, and issues.
- On cancellation, ask whether to keep a labeled partial snapshot, discard it,
  or continue scanning. Use the same decision when closing during a scan.
- Results show total logical size, folder/file/hidden counts, category totals,
  a lazy folder tree, and a virtualized paged table with search, filters, sort,
  breadcrumbs, and read-only copy/open/reveal actions.
- Retain the newest 20 complete or kept-partial snapshots. Support reopen,
  per-scan deletion, and clear-all with confirmation.
- Settings provides an immediate, persistent System/Light/Dark segmented theme
  control, history storage usage, data location, and clear-history action.
- Use a polished evolution of the reference: compact neutral surfaces,
  restrained green accent, consistent borders and radii, no drop shadows, and
  complete light/dark states.

## Implementation

- Use an MVVM application project, a core scanning/domain project, and tests.
  Pin .NET 10 and compatible Avalonia, DataGrid, CommunityToolkit.Mvvm,
  Microsoft.Data.Sqlite, and Fluent System Icons packages.
- Define `ThemePreference`, `ScanStatus`, scan request/progress/outcome records,
  `IScanService`, `IScanRepository`, and `IPlatformFileActions`.
- Traverse iteratively in the background, never follow reparse points, classify
  file types centrally, throttle progress to roughly 10 Hz, and insert SQLite
  rows in bounded transactions.
- Persist scans, entries, category totals, issues, and migrations under
  `%LOCALAPPDATA%\Dirvexa`. Use WAL, foreign keys, parameterized queries,
  indexed paging, atomic settings, retention after finalize, and a single-instance
  mutex.
- Keep file actions read-only and isolated behind a Windows platform service.
  Missing historical items produce a stale-result message.

## Packaging and safety

- Build versioned self-contained single-file portable executables and NSIS
  installers for `win-x64` and `win-arm64`.
- Install per user, create normal shortcuts, and ask whether LocalAppData should
  be removed during uninstall; preserving data is the default.
- Provide reproducible PowerShell builds and signing hooks. V1 remains unsigned
  and is not published automatically.
- No telemetry, accounts, file mutation, update checker, network service,
  external database, elevation, CI publishing, or signing credentials.
- Support Windows 11 x64/ARM64. Smoke-test Windows 10 22H2 x64 on a best-effort
  basis because it is outside the current ordinary .NET 10 support matrix.

## Verification and done criteria

- Unit-test path validation, classification, aggregation, formatting, theme
  persistence, retention, cancellation decisions, and query paging.
- Integration-test real temporary trees, hidden items, inaccessible/disappearing
  paths, long paths, reparse points, extensionless files, cancellation outcomes,
  and reopened snapshots.
- Verify responsive large scans, bounded memory, prompt cancellation, navigation
  during a scan, stale file actions, history operations, and every screen in
  Light, Dark, and System themes at minimum/default/large window sizes.
- Produce and smoke-test x64/ARM64 portable and setup artifacts. Report ARM64 as
  compile-verified but runtime-unverified when no ARM64 device is available.

## Assumptions

- Product name is Dirvexa; v1 is English-only and intended for general Windows
  users analyzing local storage.
- Logical size sums accessible file lengths, the root is excluded from the
  subfolder count, and reparse targets are never followed.
- Portable and installed builds share LocalAppData state. Linux/macOS, network
  paths, file mutation, export, automatic updates, CI publishing, and signing
  are deferred.
