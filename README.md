# nulic

A .NET global tool that collects and produces a **license disclosure package** for all NuGet dependencies in a project.

[![CI](https://github.com/rafntor/nulic/actions/workflows/ci.yml/badge.svg)](https://github.com/rafntor/nulic/actions/workflows/ci.yml)

## What it does

Given a solution, project, or folder, nulic:

1. Enumerates all NuGet packages — direct and transitive
2. Downloads or copies the actual license text for each package
3. Writes a `licenses/` folder next to the solution containing:
   - One license text file per unique license (shared across packages using the same SPDX id)
   - A `licenses.json` manifest with full metadata per package

The goal is a **complete, shippable artifact** — every package gets an actual license file on disk.

## Install

```
dotnet tool install -g nulic
```

## Usage

```
nulic [<path>] [options]
```

| Argument / Option | Description |
|---|---|
| `<path>` | Solution file, project file, or folder. Default: `.` |
| `-d`, `--show-defaults` | Print the default `nulic.json` to stdout and exit |
| `-l`, `--log-level` | Log verbosity: `Verbose`, `Debug`, `Information` (default), `Warning`, `Error` |
| `-m`, `--merge <dir>` | Merge a `licenses/` directory from another nulic-processed project (repeatable) |
| `-o`, `--output <dir>` | Output folder for license files and report. Default: `<path>/licenses` |

### Examples

```sh
# Run on the current directory (auto-discovers solution)
nulic

# Run on a specific solution
nulic path/to/MyApp.sln

# Only show warnings and errors
nulic --log-level Warning

# Combine licenses from two sub-projects into one disclosure package
nulic MyApp.sln --merge ../firmware/licenses --merge ../safety/licenses

# Write output to a custom folder
nulic MyApp.sln --output D:/artifacts/licenses
```

## nulic.json

On first run, nulic creates a `nulic.json` next to the solution as a starting template.  
Use `nulic --show-defaults` to print the default config.

```jsonc
{
  "exclude": [],      // glob patterns for project files to exclude from scanning
  "ignore":  [],      // packages to ignore — id:* or author:* patterns
  "allow":   [],      // SPDX allowlist; non-empty enables allowlist mode (exits 1 on violation)
  "overrides": []     // patch metadata or inject packages without a real NuGet entry
}
```

### Ignore patterns

Patterns are glob-style (`*` = any characters). The `id:` prefix is optional — a bare pattern also matches against package id.

```jsonc
"ignore": [
  "Tyrell.Corp.*",             // id glob — id: prefix is optional
  "id:*Nexus.Test*",           // id: prefix is explicit but equivalent
  "author:Rick Deckard",       // match on author name (glob)
  "developmentDependency",     // packages marked developmentDependency in packages.config
  "PrivateAssets"              // packages with PrivateAssets=all in PackageReference
]
```

### Overrides

Override or inject a package entry — useful for packages with dead license URLs or internal packages.
All fields except `id` are optional:

```jsonc
"overrides": [
  {
    // All available fields:
    "id":         "DeathStar.TurboLaser",
    "version":    "3.0.0",                             // pin to a specific version (omit = all versions)
    "license":    "LicenseRef-Imperial",               // SPDX expression or custom LicenseRef
    "licenseUrl": "licenses/IMPERIAL_LICENSE.txt",     // local path or https://
    "authors":    ["Emperor Palpatine"],
    "projectUrl": "https://empire.gov/deathstar",
    "copyright":  "Copyright © 19 BBY Galactic Empire. All sectors reserved."
  },
  {
    // Minimal — just fix the license URL for a package with a dead link:
    "id":         "HanSolo.Kessel",
    "licenseUrl": "https://raw.githubusercontent.com/hansolo/kessel/main/LICENSE"
  }
]
```

### Allowlist (CI gate)

When `allow` is non-empty, nulic validates every package against the list and **exits with code 1** if any violation is found — suitable for use as a CI gate:

```jsonc
"allow": [
  "MIT",
  "Apache-2.0",
  "BSD-3-Clause",
  "WITH LicenseRef-linking-exception"   // permits GPL files carrying a linking exception
]
```

Compound SPDX expressions (e.g. `MIT AND LicenseRef-foo`) must have every component covered by the allowlist.

## NOASSERTION

If nulic cannot obtain or identify a license for a package, the `license` field in `licenses.json` is set to `NOASSERTION` and a warning is printed. Common causes:

- The package has no license metadata and no recognizable license file
- The license URL is dead or returns an unrecognizable format
- The package is not in the local NuGet cache (run `dotnet restore` first)

**Fix with an override:**

```jsonc
"overrides": [
  {
    "id": "Some.Package",
    "license": "MIT",
    "licenseUrl": "https://raw.githubusercontent.com/org/repo/main/LICENSE"
  }
]
```

## Merging projects (`--merge`)

When a product bundles multiple independently-built components (e.g. a main application together with subsystems from different repos), each component should first be processed by nulic on its own. Then use `--merge` to produce a combined disclosure package:

```sh
# Step 1: process each component independently (in their own CI)
nulic HAL9000/           # produces HAL9000/licenses/
nulic AE35Unit/          # produces AE35Unit/licenses/

# Step 2: produce the combined disclosure for the product
nulic Discovery/ --merge ../HAL9000/licenses --merge ../AE35Unit/licenses
```

The merge step:
- Reads `licenses.json` from each merged directory
- Copies license files into the target `licenses/` folder (skips identical files, **errors on content mismatch**)
- Unions all packages, deduplicating by `Id + Version`
- Validates the combined set against the target project's `allow` list
- Regenerates `licenses.md` from the combined data

Running `nulic` on the nulic repo itself produces (abbreviated):

```
licenses/
  MIT.txt                          # shared — AngleSharp, System.CommandLine, Textify, ...
  Apache-2.0.txt                   # shared — all NuGet.* packages, Serilog, ...
  Microsoft.Build.18.6.3/
    notices/THIRDPARTYNOTICES.txt  # supplementary file from the package
  ...                              # one entry per unique package
  licenses.json
```

`licenses.json` entry (real example from nulic's own dependencies):

```jsonc
{
  "id": "AngleSharp",
  "version": "1.4.0",
  "authors": ["AngleSharp"],
  "projectUrl": "https://anglesharp.github.io/",
  "copyright": "Copyright 2013-2025, AngleSharp.",
  "license": "MIT",
  "licenseUrl": "https://licenses.nuget.org/MIT",
  "licenseFiles": ["MIT.txt"]
}
```

## Supported project types

| Project type | Package source |
|---|---|
| SDK-style `.csproj` / `.fsproj` / `.vbproj` | `obj/project.assets.json` (requires `dotnet restore`) |
| Classic .NET Framework `.csproj` | `packages.config` |
| Native C++ `.vcxproj` | `packages.config` |
| Solutions `.sln` | All constituent projects |
| Folder | Auto-discovers first `.sln`, then `.csproj`, etc. |
