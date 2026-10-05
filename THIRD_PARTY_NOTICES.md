# Third-party notices

This product includes third-party software. The repository lockfiles are the authoritative dependency inventory; deployment owners should preserve package licence files from the published output and repeat review on upgrades.

| Component | Version | Licence |
| --- | ---: | --- |
| ASP.NET Core / Microsoft.Extensions | 10.0.10 | MIT |
| Entity Framework Core SQLite | 10.0.10 | MIT |
| Microsoft.Data.Sqlite / SQLitePCLRaw bundle | 10.0.10 / 3.0.3 | MIT / Apache-2.0 |
| SQLite native library | bundled transitively | Public domain |
| DocumentFormat.OpenXml | 3.5.1 | MIT |
| pptx-glimpse | 3.2.8 | MIT |
| Carlito / Arimo / Tinos / Cousine / Caladea fonts | revisions in `fonts/README.md` | SIL Open Font License 1.1; per-family notices in `fonts/*/OFL.txt` |
| fflate | 0.8.3 | MIT |
| pngjs (test-only) | 7.0.0 | MIT |
| Vite | 8.1.5 | MIT |
| TypeScript | 7.0.2 | Apache-2.0 |
| xUnit and Microsoft test tooling | see NuGet lockfiles | Apache-2.0 / MIT |
| Playwright | see E2E lockfile | Apache-2.0 |

No Microsoft Office, PowerPoint, LibreOffice, proprietary school fonts, or third-party presentation files are redistributed.

The bundled `pptx-glimpse` installation carries local signed-line-chart scaling and text-layout patches. Their installer, version/checksum guards and validation scope are documented in `docs/renderer-compatibility.md`. Preserve the upstream MIT licence when distributing the modified bundles.
