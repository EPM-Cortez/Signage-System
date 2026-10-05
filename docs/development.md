# Development and testing

## Toolchain

- Stable .NET SDK 10.x. `global.json` disables preview SDK selection.
- Node.js 22.x.
- PowerShell 7/Windows PowerShell or a POSIX shell.

`setup-local.ps1` and `setup-local.sh` use repository-local NuGet/npm caches, locked dependency graphs, and the committed converter/player lockfiles. The first run needs access to NuGet and npm. Later runs can use their caches.

## Useful commands

```powershell
.\setup-local.ps1
.\run-local.ps1 -SkipSetup
.\.dotnet\dotnet.exe test Signage.slnx --no-restore
Push-Location src/Signage.Converter; npm test; Pop-Location
Push-Location src/Signage.Player; npm run build; Pop-Location
```

The application migrates SQLite automatically only in Development. Production migration is explicit:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Production'
Set-Location <release>\app
dotnet .\Signage.Web.dll --migrate
```

Run a non-mutating cleanup report with `dotnet Signage.Web.dll --maintenance-dry-run`.

## Test layout

- `Signage.UnitTests`: timing, scheduling order, DST boundaries, tokens, content IDs, pairing, and retention policy.
- `Signage.IntegrationTests`: real temporary on-disk SQLite with migrations/WAL/foreign keys, data-root locking, transactional job records, development authentication, and pairing API.
- `Signage.Converter`: settings/adapter contract tests and rendered-pixel regressions for Office-font replacements, missing fonts, signed line charts, bar/pie charts, 4:3 dimensions, text-box fitting, font styles and inherited master/layout formatting. Internally authored OOXML fixtures are generated in memory; they contain no school data. Glyph/series thresholds tolerate anti-aliasing but reject missing text and blank charts; text-layout tests also check glyph bounds.
- `tests/Signage.E2ETests`: Playwright browser smoke tests and offline-player coverage.

The test-only `pptxgenjs` dependency uses an `image-size` 2.0.3 override to avoid known parser denial-of-service advisories. It generates only internally authored fixtures, is verified by the end-to-end suite, and is not shipped with the converter. The player build lockfile also pins patched PostCSS/Nano ID dependencies.

After packaging on Windows, run `scripts/test-release.ps1 -ReleaseRoot <absolute-release-directory>`. It renders the generated fixture using the packaged fonts/converter, applies migrations to a fresh isolated database, and probes the actual Production pipeline with synthetic AD configuration. It checks health, API protection, player assets, the AD login page, and absence of the development login; it never contacts a real directory or modifies existing signage data.

Tests never use EF Core's in-memory provider or Docker. Private school presentations must remain outside the repository; use the checklist in `docs/manual-renderer-corpus.md`.

## Repository data

`.local-data`, `backups`, SDK/package caches, test output, and production secret overrides are ignored. Do not commit real school content, device credentials, OIDC secrets, certificates, or data-protection keys.
