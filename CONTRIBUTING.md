# Contributing to Dirvexa

Read `AGENTS.md` before making changes. Keep the scanner read-only, offline,
bounded, and independent of the UI thread.

Before opening a pull request:

```powershell
dotnet restore Dirvexa.slnx
dotnet build Dirvexa.slnx -c Release
dotnet test Dirvexa.slnx -c Release
dotnet list Dirvexa.slnx package --vulnerable --include-transitive
```

UI changes must be checked at minimum, default, and large window sizes in
System, Light, and Dark themes. Do not add drop shadows or translate-on-hover
motion.

Never include private scanned paths, real user databases, logs, signing
credentials, or built artifacts in a pull request.
