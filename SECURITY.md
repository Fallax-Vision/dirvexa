# Security policy

## Supported versions

Security fixes are applied to the latest source and latest published release.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting for
`Fallax-Vision/dirvexa`. Do not include real private filesystem paths or file
contents in a public issue.

## Security invariants

- Offline and local-only for scanning; no telemetry or background network access.
- Manual update checks contact only GitHub's public Releases API over HTTPS,
  validate release metadata and links, and never download or execute artifacts.
- No elevation and no mutation of scanned content.
- Reparse points are never followed.
- SQLite statements are parameterized and migrations are idempotent.
- Mutable data remains under the current user's LocalAppData directory.
- Signing credentials and certificates are never committed.
