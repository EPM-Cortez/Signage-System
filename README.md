# School Signage Publisher

A self-hosted PowerPoint signage system for schools. Teachers upload a normal `.pptx`, choose an authorised screen group, and leave; a durable background job renders the slides to an immutable, hash-verified package. Paired Chromium or Edge players download the complete package before switching and keep looping it while offline.

The supported stack is stable .NET 10, Node.js 22, SQLite, and local disk. There is no Docker, Office, LibreOffice, paid conversion SaaS, public asset CDN, or separately hosted converter service. Optional YouTube playback uses YouTube's official online player/API.

Embedded MP4/WebM clips are extracted, cached, and played to completion before advancing. Recognised YouTube embeds play online through a privacy-enhanced iframe and are skipped when unavailable/offline. Playback is muted; unsupported formats retain a static preview with a warning. Reload existing display browsers after upgrading and re-upload media-containing PowerPoints. See [media compatibility](docs/renderer-compatibility.md#video-playback) for limits and configuration.

## Start locally

Prerequisites: stable .NET 10 SDK and Node.js 22.

```powershell
.\run-local.ps1
```

or:

```bash
./run-local.sh
```

Open `http://localhost:5179/dev-login`. Development provides teacher and administrator links, two groups (`Reception`, `Staff Room`), teacher access to Reception, and an already-rendered example package. Runtime data is written to the ignored `.local-data` directory. No secrets are required for development.

## Main workflows

- Teacher: sign in, open Publish, select a permitted group, upload a `.pptx`, and watch the durable status page.
- Administrator: monitor failures/devices, approve a six-digit pairing code, move or revoke displays, retry conversions, and roll back a group to a ready version.
- Staff access: assign Administrator/Teacher/Pending roles, enable or disable accounts, and choose each teacher's permitted screen groups. Active Directory supports username/password sign-in over LDAPS plus an admin-only connection test.
- Player: open `/player/`, pair once, then run full-screen. Its credential is returned once, stored in IndexedDB, and represented server-side only by a SHA-256 hash.

## Verify and release

```powershell
.\setup-local.ps1
.\publish-release.ps1
```

`setup-local` performs locked restores, builds the converter/player/solution, and runs the automated tests. `publish-release` creates `artifacts/school-signage` plus a compressed package with the published application, production converter dependencies, deployment examples, scripts, and documentation. Existing Windows release directories are never overwritten; choose a fresh `-OutputDirectory` to rebuild.

Download the Windows x64 production ZIP and SHA-256 checksum from [GitHub Releases](https://github.com/EPM-Cortez/Signage-System/releases). It requires the ASP.NET Core 10.0.10+ runtime and Node.js 22.23.1+ (22.x); follow the included `INSTALL.md` or [production quickstart](docs/production-quickstart.md). Pushes to `main` run the Windows build/tests and retain an Actions artifact; `v*` tags publish the successfully tested package as a GitHub Release.

## Documentation

- [Architecture](docs/architecture.md)
- [Active Directory sign-in and staff permissions](docs/active-directory.md)
- [Development and testing](docs/development.md)
- [Operations, deployment, backup, and restore](docs/operations.md)
- [Chromium/Edge kiosk setup](docs/kiosk-player.md)
- [Renderer compatibility](docs/renderer-compatibility.md)
- [API and converter contract](docs/api.md)
- [Security and known limitations](docs/security.md)
- [Release checklist and trade-offs](docs/release-checklist.md)

This repository does not claim complete PowerPoint fidelity. See the compatibility guide and run the private school presentation corpus before production rollout.
