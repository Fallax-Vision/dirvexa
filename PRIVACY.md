# Dirvexa privacy boundary

Dirvexa works fully offline for scanning and browsing. It has no account system,
telemetry, analytics, advertising, background update check, cloud database, or
network service. If the user explicitly selects **Check for Updates**, Dirvexa
makes one anonymous HTTPS request to GitHub's public Releases API for
`Fallax-Vision/dirvexa`. The request contains the app version in its User-Agent
and never includes scan data, local paths, file metadata, settings, or identifiers.

The app reads filesystem metadata only for a root explicitly selected by the
user. It stores local paths, names, sizes, timestamps, attributes, categories,
summary totals, and recoverable scan errors in a SQLite database under
`%LOCALAPPDATA%\Dirvexa`. It never stores file contents.

Dirvexa does not delete, move, rename, upload, or edit scanned items. Open and
reveal actions are delegated to Windows only after the user requests them.
Junctions, symbolic links, and other reparse points are not followed.

Removing a saved scan changes only Dirvexa's derived snapshot. The uninstaller
defaults to preserving local data and asks separately before deleting it.
