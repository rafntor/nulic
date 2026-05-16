# nulic — Copilot Instructions

## What nulic is

nulic is a .NET CLI tool that **collects and produces a license disclosure package** for all NuGet
dependencies in a project tree. It handles:
- **SDK-style .NET projects** (`.csproj`, `.fsproj`, `.vbproj`) — reads `obj/project.assets.json`
- **Classic .NET Framework projects** — reads `packages.config`
- **Native C++ projects** (`.vcxproj`) — reads `packages.config` from the project directory
- **Solutions** (`.sln`) — expands to all constituent projects
- **Folder input** — auto-discovers the first solution or project type found

Given a path it:

1. Enumerates all packages (direct + transitive) from `project.assets.json` / `packages.config`
2. Downloads or copies the actual license text for every package
3. Writes a `licenses/` folder alongside the solution containing:
   - One license text file per unique license (shared across packages using the same license)
   - A `licenses.json` manifest with full metadata per package

The goal is a complete, shippable artifact — every package must have an actual license file, not
just an SPDX identifier.

## How it differs from nuget-license (sensslen/nuget-license)

nuget-license is a **CI compliance/validation** tool — it checks whether licenses are on an
allowlist and exits with a non-zero code if not. It can optionally download license files but:

- Packages that declare only `<license type="expression">` (the modern best practice) have no
  embedded file — nuget-license cannot produce a disclosure file for them.
- It does not scan for undeclared embedded license files.
- It does not decompose multi-license bundles into a composite SPDX expression.
- It does not detect GPL exception clauses.

nulic fills these gaps. **The intended end state is nulic doing both**: producing the disclosure
package AND providing allowlist validation — making it a superset of nuget-license.

## Architecture

```
Program.cs               CLI entry-point (System.CommandLine)
  └─ MSBuildProject      Discovers .sln/.csproj/.vcxproj etc; follows ProjectReferences
  └─ NugetMetadata       Per-package metadata + license collection pipeline
       └─ NulicLicense   Per-license-file object with SPDX ID detection
            └─ LicenseDownload   URL-based license fetching (handles transforms)
            └─ SpdxLookup        Downloads canonical SPDX license texts from spdx.org
            └─ CommonLicenses    Bundled text for the most common SPDX licenses
ProgramSettings          Settings loader (currently stubbed — planned for overrides)
```

### Package discovery (`NugetMetadata.GetNugetIdsFrom`)

Source priority per project:
1. **`packages.config`** (same directory as project file) — used by classic .NET Framework and
   native C++ (`.vcxproj`) projects. Read by `PackagesConfigReader`.
2. **`obj/project.assets.json`** — SDK-style projects. Read by `LockFileFormat`; all entries with
   `Type == "package"` included (full transitive closure).
3. If neither exists and the project is SDK-style, throws (missing `dotnet restore`).

### License collection pipeline (per package, `NugetMetadata.CollectLicenses`)

Priority order:
1. **SPDX expression** (`<license type="expression">MIT</license>`):
   Download the canonical text for each leaf identifier from spdx.org (via `SpdxLookup`).
   Standard licenses land directly in `licenses/` (shared). E.g. `licenses/MIT.txt`.

2. **Embedded file** (`<license type="file">LICENSE</license>`):
   Copy from the global NuGet packages cache into `licenses/<Id>.<Version>/LICENSE`.

3. **Legacy URL** (`<licenseUrl>https://...`):
   Download from the URL. GitHub blob URLs are rewritten to raw content.
   RTF/txt/md extensions are preserved. Filename derived from the URL path.

4. **Undeclared embedded files** (fallback when nothing else found):
   Scan the `.nupkg` for files matching `*license*`, `*thirdpartynotice*.*`, `*credit*.*`
   and copy them.

### SPDX ID detection (per license file, `NulicLicense.InitializeOnce`)

Two complementary strategies (both are needed — different failure modes):

**Cosine similarity** (primary):
- Compares the license file's word-frequency profile against profiles built from `CommonLicenses`
- Threshold: 0.9
- Fails on: short reference texts (too sparse), multi-license bundles (each license diluted)

**Keyword scan** (`LookupSpdxIDByKeywords`) (fallback):
- Collects ALL licenses present in the text into a `HashSet`
- Returns a composite SPDX `AND` expression for bundles
- Also calls `DetectSpdxException()` for single-GPL files to append `WITH <exception>`
- Handles: AGPL, LGPL, GPL (v2/v3), Apache-2.0, MIT, BSD-2/3, MPL-2.0, EPL-1/2, ISC, OpenSSL,
  MS-PL, Unlicense
- GPL exceptions detected: `Classpath-exception-2.0`, `LicenseRef-linking-exception`

### NulicLicense instance sharing

`NulicLicense` maintains a static `_licenses` list. Instances are shared when multiple packages
use the same license file (e.g. all MIT packages share one `MIT.txt`):
- `FindExisting` — match by file path (already downloaded)
- `PromoteExisting` — match by SPDX ID (standard license already known, assign file path)
- Otherwise create a new instance

Initialization is concurrent-safe via `Interlocked.CompareExchange` + `SemaphoreSlim`: only the
first caller runs `InitializeOnce`, others await the semaphore.

## Key design decisions

- **Always produce a file**: The output is not just metadata — every package in `licenses.json`
  has a corresponding file on disk. NOASSERTION means the file couldn't be obtained.
- **Shared license files**: Standard licenses (MIT, Apache-2.0, etc.) are stored once at the root
  of `licenses/`, not duplicated per package. Package-specific or unusual licenses go in
  `licenses/<Id>.<Version>/`.
- **NOASSERTION** is the SPDX-standard sentinel for "could not determine". It is used both for
  the `License` field in JSON and to identify problem packages in console output.
- **Composite SPDX expressions**: Multi-license bundles are represented as `A AND B AND C`.
  GPL exceptions are represented as `GPL-2.0-only WITH LicenseRef-linking-exception`. The `AND`
  combinator is used (worst-case/most restrictive) when the relationship is unclear.

## JSON output schema (`licenses.json`)

```jsonc
[
  {
    "Id": "Newtonsoft.Json",
    "Version": "13.0.3",
    "Authors": ["James Newton-King"],
    "ProjectUrl": "https://...",
    "Copyright": "Copyright © 2007 James Newton-King",
    "License": "MIT",                            // SPDX expression or NOASSERTION
    "LicenseUrl": "https://licenses.nuget.org/MIT",
    "LicenseFiles": ["MIT.txt"]                  // relative paths within licenses/
  }
]
```

## CLI

```
nulic [path] [--settings-folder <dir>] [--dump-settings]
```

- `path`: solution file, project file, or folder (default: `.`)
- `--settings-folder` / `-s`: custom settings directory (default: `settings/`)
- `--dump-settings` / `-d`: print built-in settings as JSON and exit

Output is always written to `<path>/licenses/`.

## ProgramSettings (planned — currently stubbed)

The settings system is intended to support:
- Manual license overrides for packages with dead/wrong URLs
- Allowlist validation (planned — to reach parity with nuget-license)
- Package exclusion/ignore lists
- Custom URL→SPDX mappings

`--dump-settings` will export the built-in defaults so users can copy and customize them.

## Packages and dependencies

| Package | Purpose |
|---|---|
| `NuGet.Commands` / `NuGet.Protocol` | Read `project.assets.json`, global cache, nuspec |
| `Microsoft.Build` | Load `.sln`/`.csproj` for project discovery |
| `System.CommandLine` | CLI argument parsing |
| `F23.StringSimilarity` | Cosine similarity for SPDX ID detection |
| `AngleSharp` | HTML parsing (e.g. license pages that are HTML not text) |
| `Serilog.Sinks.Console` | Structured console logging |
| `Textify` | Text utilities |

## Code conventions

- `async` is only used where the method actually needs to `await` mid-body (e.g. `using var stream`
  that must stay alive, try/catch around await, logic after await). Methods that just forward a
  task return `Task`/`Task<T>` directly without `async`/`await`.
- Internal types are exposed to `unit_tests` via `[assembly: InternalsVisibleTo("unit_tests")]`.
- `NulicLicense.NOASSERTION` is the canonical sentinel — do not use `null` or empty string for
  unknown licenses.

## Known gaps / future work

- `ProgramSettings.Load()` is stubbed — implement settings loading for manual overrides
- Allowlist validation (exit-code-based CI gate, like nuget-license)
- `NETStandard.Library` has a dead license URL — needs settings override
- Package exclusion / ignore-list support
- Output formats beyond JSON (table, markdown, CSV)
- `--error-only` flag to suppress valid packages from output
