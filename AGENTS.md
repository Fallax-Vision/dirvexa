# Dirvexa project guidance

## Architecture invariants

- Keep Scan, History, and Settings in one resizable Avalonia window. Progress and Results are contextual states of the Scan workspace; an active scan continues when another top-level section is open.
- Allow one scan at a time. Traverse local folders iteratively on a background task, throttle UI progress, batch SQLite writes, and keep result queries paged so large scans do not grow UI memory with the result count.
- Never follow reparse points, junctions, or symbolic links. Reject UNC and mapped-network roots in Windows v1 and record recoverable filesystem failures as scan issues.
- Keep cross-platform scanning models and contracts in `Dirvexa.Core`; isolate Windows open/reveal/clipboard behavior in platform services so Linux and macOS ports do not require domain changes.
- Store mutable state under `%LOCALAPPDATA%\Dirvexa`; portable and installed builds intentionally share the same database and settings.

## Privacy and safety

- Dirvexa is offline and read-only with respect to scanned content. Never add telemetry, accounts, background network access, file deletion, file moves, permission changes, or elevation.
- Never transmit, upload, or expose scanned paths or metadata. Diagnostics stay local and should contain only operational errors needed to troubleshoot a scan.
- Use parameterized SQLite queries, WAL mode, idempotent migrations, atomic settings writes, and bounded batch sizes.
- Preserve `%LOCALAPPDATA%\Dirvexa\data` and settings during upgrades. Uninstall must default to keeping user data and require explicit confirmation before deleting it.

## Development and verification

- Use two-space indentation and keep the UI free of drop shadows and translate-on-hover motion.
- Build with `dotnet build Dirvexa.slnx -c Release`.
- Test with `dotnet test Dirvexa.slnx -c Release`.
- Package with `./scripts/Build-Windows.ps1 -Version <semver>`.
- Verify x64 and ARM64 portable/setup builds, Light/Dark/System themes, cancellation keep/discard behavior, history retention, stale file actions, and dependency vulnerability status before release.

## Planning, release, and security rules

- Every time a plan is created in Plan Mode, save it as a Markdown file in `plans/`, using the most appropriate filename based on the plan title, before implementing it.
- Do not commit or push unless the project owner explicitly requests that specific action.
- When the project owner asks for a full or partial security check, run a standard security scan, not a deep scan.
- Keep Windows v1 English-only unless localization is explicitly added as a scoped feature.
