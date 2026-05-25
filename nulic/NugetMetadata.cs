using NuGet.Configuration;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Licenses;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Serilog;
using System.Globalization;
using System.IO.Enumeration;

namespace nulic;

internal class NugetMetadata
{
    ManifestMetadata _manifest;
    List<NulicLicense> _licenses = new();
    Uri? _apiLicenseUrl;
    Uri? _apiProjectUrl;
    PackageOverride? _override;
    //
    //
    // *** following properties are json-exported
    //
    // https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices
    //
    // *** first the unmodified info from manifest
    //
    public string Id => _manifest.Id!;
    public NuGetVersion Version => _manifest.Version!;
    public IEnumerable<string> Authors => _override?.Authors ?? _manifest.Authors.Select(a => a.Trim());
    public Uri? ProjectUrl => _override?.ProjectUrl is string p ? new Uri(p) : _manifest.ProjectUrl ?? _apiProjectUrl;
    //
    // *** next the potentially augmented info from discovery
    //
    public string Copyright => _override?.Copyright ?? _manifest.Copyright ?? string.Join(", ", _licenses.SelectMany(l => l.Copyright).Distinct());
    public string License
    {
        get
        {
            if (_override?.License is string expr) return expr;

            if (_manifest.LicenseMetadata?.Type == LicenseType.Expression)
                return _manifest.LicenseMetadata.License;

            if (_licenses.Any()) // https://spdx.github.io/spdx-spec/v2.3/SPDX-license-expressions/
                return string.Join(" AND ", _licenses.Select(l => l.SpdxID).Distinct()); // 'AND' is worst-case, so pick that

            return NulicLicense.NOASSERTION;
        }
    }
    public Uri? LicenseUrl
    {
        get
        {
            if (_manifest.LicenseUrl is Uri uri && uri != LicenseMetadata.LicenseFileDeprecationUrl)
                return uri;

            if (_apiLicenseUrl is Uri apiUri)
                return apiUri;

            if (License == NulicLicense.NOASSERTION)
                return null;

            // LicenseRef-* are user-defined identifiers — no public page exists
            if (License.Contains("LicenseRef-", StringComparison.OrdinalIgnoreCase))
                return null;

            // AND/OR compound expressions have no single canonical URL
            if (License.Contains(" AND ") || License.Contains(" OR "))
                return null;

            // Single ID or WITH expression — licenses.nuget.org handles both
            return new Uri($"https://licenses.nuget.org/{Uri.EscapeDataString(License)}");
        } 
    }
    public IEnumerable<string> LicenseFiles { get; private set; } = Enumerable.Empty<string>();
    //
    // *** end of json-properties
    //
    public override string ToString() => $"{Id}.{Version}";
    public static async Task<IEnumerable<NugetMetadata>> GetFrom(MSBuildProject project)
    {
        var ids = GetNugetIdsFrom(project);

        return await Task.WhenAll(ids.Select(FromPackageId));
    }

    public static HashSet<string> GetIgnoredIds(IEnumerable<MSBuildProject> projects, bool devDependency, bool privateAssets)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!devDependency && !privateAssets) return result;

        foreach (var project in projects)
        {
            var package_config = Path.Join(project.FilePath.DirectoryName, NuGetConstants.PackageReferenceFile);

            if (File.Exists(package_config) && devDependency)
            {
                List<PackageReference> packages;
                using (var stream = File.OpenRead(package_config))
                    packages = new PackagesConfigReader(stream).GetPackages().ToList();

                foreach (var p in packages.Where(p => p.IsDevelopmentDependency))
                {
                    Log.Debug("Will ignore developmentDependency {Id}", p.PackageIdentity.Id);
                    result.Add(p.PackageIdentity.Id);
                }
            }

            var project_assets = Path.Join(project.IntDir, LockFileFormat.AssetsFileName);

            if (File.Exists(project_assets) && privateAssets)
            {
                var lock_file = new LockFileFormat().Read(project_assets);
                var deps = lock_file.PackageSpec?.TargetFrameworks
                    .SelectMany(tf => tf.Dependencies)
                    .Where(d => d.SuppressParent == LibraryIncludeFlags.All)
                    .Select(d => d.Name) ?? Enumerable.Empty<string>();

                foreach (var name in deps)
                {
                    Log.Debug("Will ignore PrivateAssets {Id}", name);
                    result.Add(name);
                }
            }
        }
        return result;
    }
    public static Task CollectInformation(IEnumerable<NugetMetadata> nugets, DirectoryInfo license_root)
    {
        return Task.WhenAll(nugets.Select(nuget => nuget.CollectInformation(license_root)));
    }

    NugetMetadata(ManifestMetadata manifest)
    {
        _manifest = manifest;
    }
    NugetMetadata(PackageIdentity identity)
    {
        _manifest = new() { Id = identity.Id, Version = identity.Version };
    }
    async Task CollectInformation(DirectoryInfo license_root)
    {
        var licenses = await CollectLicenses(license_root);

        foreach (var license in licenses)
            LogException(license.InitException, license.LicenseUrl);

        _licenses.AddRange(licenses.Where(l => !l.IsNotFound));

        LicenseFiles = _licenses.Select(l => Path.GetRelativePath(license_root.FullName, l.Filepath.FullName));
    }
    void LogException(Exception? ex, Uri? url)
    {
        if (ex is HttpRequestException hex)
            Log.Error("{pkg} : Download failed ({status}) - {url}", ToString(), hex.StatusCode, url);

        else if (ex is LicenseDownload.UnknownUrlException)
            Log.Error("{pkg} : Unknown URL (dont know how to download) - {url}", ToString(), url);

        else if (ex != null)
            Log.Fatal(ex, "{pkg} : License Init failed ({url})", ToString(), url);
    }
    async Task<IEnumerable<NulicLicense>> CollectLicenses(DirectoryInfo license_root)
    {
        //https://learn.microsoft.com/en-us/nuget/reference/nuspec#license
        //https://learn.microsoft.com/en-us/nuget/nuget-org/licenses.nuget.org

        // 'licenses' contain the relative filepaths from root of the nuget
        IEnumerable<NulicLicense> licenses = Enumerable.Empty<NulicLicense>();

        // Override: explicit licenseUrl or SPDX expression — fetch override, then also copy any embedded package files
        if (_override?.LicenseUrl != null || _override?.License != null)
        {
            var overrideLicenses = _override.LicenseUrl != null
                ? await FetchOverrideLicense(license_root)
                : await DownloadOverrideExpression(_override.License!, license_root);
            var embedded = await CopyEmbeddedFiles(license_root, ["*license*", "*thirdpartynotice*.*", "*notice*.*", "*credit*.*"]);
            return overrideLicenses.Concat(embedded);
        }

        var license_data = _manifest.LicenseMetadata;

        if (license_data?.Type == LicenseType.Expression)
        {
            // When a package declares an SPDX expression, prefer any embedded license file
            // (more authentic, may include project-specific wording) over the canonical spdx.org text.
            var embedded = await CopyEmbeddedFiles(license_root, ["*license*"], warnIfMissing: true);

            // LicenseRef-* are user-defined — no public SPDX text exists; don't attempt download.
            // Leave licenses as the embedded result (possibly empty) so the fallback scan can run.
            bool isLicenseRef = license_data.License.Contains("LicenseRef-", StringComparison.OrdinalIgnoreCase);

            licenses = embedded.Any() || isLicenseRef
                ? embedded
                : await DownloadLicenses(license_data.LicenseExpression!, license_root);

            // also collect any supplementary NOTICE / THIRD_PARTY_NOTICES files
            licenses = licenses.Concat(await CopyEmbeddedFiles(license_root, ["*thirdpartynotice*.*", "*notice*.*", "*credit*.*"]));
        }
        else if (license_data?.Type == LicenseType.File)
        {
            var license = await CopyEmbeddedLicenseFile(license_data.License, license_root);

            licenses = licenses.Append(license);

            // also collect any supplementary NOTICE / THIRD_PARTY_NOTICES files
            licenses = licenses.Concat(await CopyEmbeddedFiles(license_root, ["*thirdpartynotice*.*", "*notice*.*", "*credit*.*"]));
        }
        else if ((_manifest.LicenseUrl ?? _apiLicenseUrl) is Uri url) // legacy mode 'LicenceUrl' ?
        {
            var urlpath = url.AbsolutePath.TrimEnd('/');
            var filename = Path.GetFileNameWithoutExtension(urlpath);
            if (string.IsNullOrEmpty(filename)) filename = "license";
            var ext = Path.GetExtension(urlpath) is { Length: > 0 } e ? e : ".txt";
            var file = new FileInfo(Path.Join(license_root.FullName, ToString(), $"{filename}{ext}"));
            // .. but may be redirected if url is recognized as a standard license
            var url_license = await LicenseDownload.DownloadFrom(url, file);
                
            licenses = licenses.Append(url_license);
        }

        if (!licenses.Any()) // fallback: scan package for undeclared license files
        {
            licenses = await CopyEmbeddedFiles(license_root, ["*license*", "*thirdpartynotice*.*", "*credit*.*"], warnIfMissing: true);
        }

        if (!licenses.Any()) // last resort: try GitHub LICENSE file from ProjectUrl
        {
            licenses = await TryDownloadFromProjectUrl(license_root);
        }

        return licenses;
    }
    Task<NulicLicense[]> DownloadLicenses(NuGetLicenseExpression license, DirectoryInfo destination)
    {
        List<Task<NulicLicense>> result = new();

        license.OnEachLeafNode( // licenses and license-exceptions
            (l) => result.Add(SpdxLookup.DownloadLicense(l.Identifier, destination)),
            (e) => result.Add(SpdxLookup.DownloadException(e.Identifier, destination))
            );

        return Task.WhenAll(result);
    }
    async Task<NulicLicense> CopyEmbeddedLicenseFile(DownloadResourceResult package, string packagefile, DirectoryInfo destination)
    {
        var dest = new FileInfo(Path.Join(destination.FullName, ToString(), packagefile));

        using var source = await package.PackageReader.GetStreamAsync(packagefile, CancellationToken.None);
        var text_getter = () => new StreamReader(source).ReadToEndAsync();

        return await NulicLicense.FindOrCreate(text_getter, dest);
    }
    Task<NulicLicense> CopyEmbeddedLicenseFile(string packagefile, DirectoryInfo destination)
    {
        var identity = new PackageIdentity(_manifest.Id!, _manifest.Version);

        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        if (package is null)
        {
            Log.Warning("{pkg} : package not found in local cache, cannot copy embedded license file '{file}'", this, packagefile);
            return Task.FromResult(NulicLicense.NotFound);
        }

        return CopyEmbeddedLicenseFile(package, packagefile, destination);
    }
    static bool NameMatch(string filepath, string pattern)
    {
        var file = new FileInfo(filepath);

        return FileSystemName.MatchesSimpleExpression(pattern, file.Name, ignoreCase: true);
    }
    async Task<IEnumerable<NulicLicense>> CopyEmbeddedFiles(DirectoryInfo destination, string[] candidates, bool warnIfMissing = false)
    {
        var identity = new PackageIdentity(_manifest.Id!, _manifest.Version);

        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        if (package is null)
        {
            if (warnIfMissing)
                Log.Warning("{pkg} : package not found in local cache, skipping embedded file scan", this);
            return Enumerable.Empty<NulicLicense>();
        }

        var files = await package.PackageReader.GetFilesAsync(CancellationToken.None);

        files = files.Where(f => candidates.Any(c => NameMatch(f, c)));

        return await Task.WhenAll(files.Select(f => CopyEmbeddedLicenseFile(package, f, destination)));
    }
    async Task<IEnumerable<NulicLicense>> TryDownloadFromProjectUrl(DirectoryInfo license_root)
    {
        if (_manifest.ProjectUrl is not Uri projectUrl)
            return Enumerable.Empty<NulicLicense>();

        var host = LicenseDownload.NormalizeHost(projectUrl);

        if (host != "github.com")
            return Enumerable.Empty<NulicLicense>();

        // Try common default branch names and common license filenames
        var repo = projectUrl.AbsolutePath.TrimEnd('/');
        string[] branches = { "main", "master" };
        string[] filenames = { "LICENSE", "LICENSE.txt", "LICENSE.md", "COPYING" };

        foreach (var branch in branches)
        {
            foreach (var filename in filenames)
            {
                var rawUrl = new Uri($"https://raw.githubusercontent.com{repo}/{branch}/{filename}");
                var dest = new FileInfo(Path.Join(license_root.FullName, ToString(), filename));

                try
                {
                    var rsp = await Program.HttpClient.GetAsync(rawUrl);

                    if (!rsp.IsSuccessStatusCode)
                        continue;

                    var text_getter = () => rsp.Content.ReadAsStringAsync();
                    var license = await NulicLicense.FindOrCreate(text_getter, dest, rawUrl);

                    if (!license.IsNotFound)
                        return [license];
                }
                catch { /* try next */ }
            }
        }

        return Enumerable.Empty<NulicLicense>();
    }

    static async Task<NugetMetadata> FromPackageId(PackageIdentity identity)
    {
        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        if (package?.PackageReader?.GetNuspec() is Stream stream)
        {
            var manifest = Manifest.ReadFrom(stream, true);

            return new NugetMetadata(manifest.Metadata);
        }

        // Package not in local cache — try NuGet API
        var meta = await TryFetchFromNuGetApi(identity);
        return meta ?? new NugetMetadata(identity);
    }

    static async Task<NugetMetadata?> TryFetchFromNuGetApi(PackageIdentity identity)
    {
        var id = Uri.EscapeDataString(identity.Id.ToLowerInvariant());
        var version = Uri.EscapeDataString(identity.Version.ToNormalizedString().ToLowerInvariant());
        var url = $"https://api.nuget.org/v3/registration5-gz-semver2/{id}/{version}.json";

        try
        {
            using var rsp = await Program.HttpClient.GetAsync(url);

            if (!rsp.IsSuccessStatusCode)
                return null;

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await rsp.Content.ReadAsStreamAsync());
            var root = doc.RootElement;

            if (!root.TryGetProperty("catalogEntry", out var entry))
                return null;

            var stub = new NugetMetadata(identity);

            if (entry.TryGetProperty("licenseExpression", out var exprEl) &&
                exprEl.GetString() is { Length: > 0 } expr)
            {
                stub._manifest.LicenseMetadata = new LicenseMetadata(
                    LicenseType.Expression, expr,
                    NuGetLicenseExpression.Parse(expr), null, LicenseMetadata.EmptyVersion);
            }

            if (entry.TryGetProperty("licenseUrl", out var urlEl) &&
                urlEl.GetString() is { Length: > 0 } licUrl &&
                stub._manifest.LicenseMetadata is null)
            {
                stub._apiLicenseUrl = new Uri(licUrl);
            }

            if (entry.TryGetProperty("projectUrl", out var projEl) &&
                projEl.GetString() is { Length: > 0 } projUrl &&
                stub._manifest.ProjectUrl is null)
            {
                stub._apiProjectUrl = new Uri(projUrl);
            }

            return stub;
        }
        catch
        {
            return null;
        }
    }

    async Task<IEnumerable<NulicLicense>> FetchOverrideLicense(DirectoryInfo license_root)
    {
        var urlStr = _override!.LicenseUrl!;

        if (urlStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            urlStr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var url = new Uri(urlStr);
            var urlpath = url.AbsolutePath.TrimEnd('/');
            var filename = Path.GetFileNameWithoutExtension(urlpath);
            if (string.IsNullOrEmpty(filename)) filename = "license";
            var ext = Path.GetExtension(urlpath) is { Length: > 0 } e ? e : ".txt";
            var file = new FileInfo(Path.Join(license_root.FullName, ToString(), $"{filename}{ext}"));
            var license = await LicenseDownload.DownloadFrom(url, file);
            return [license];
        }
        else
        {
            // Local file — resolve relative to the directory containing nulic.json
            var fullPath = Path.IsPathRooted(urlStr)
                ? urlStr
                : Path.Join(ProgramSettings.SettingsDir.FullName, urlStr);

            if (!File.Exists(fullPath))
            {
                Log.Warning("{this}: Override license file not found: {path}", ToString(), fullPath);
                return Enumerable.Empty<NulicLicense>();
            }

            var sourceFile = new FileInfo(fullPath);
            var destFile = new FileInfo(Path.Join(license_root.FullName, ToString(), sourceFile.Name));
            var license = await NulicLicense.FindOrCreate(() => File.ReadAllTextAsync(fullPath), destFile);
            return [license];
        }
    }

    async Task<IEnumerable<NulicLicense>> DownloadOverrideExpression(string expression, DirectoryInfo license_root)
    {
        try
        {
            var parsed = NuGetLicenseExpression.Parse(expression);
            var licenses = await DownloadLicenses(parsed, license_root);
            return licenses.Where(l => !l.IsNotFound);
        }
        catch
        {
            // Non-standard or unparseable expression (e.g. LicenseRef-*) — label only, no file
            Log.Warning("{this}: No license file for expression '{expression}' — label only", ToString(), expression);
            return Enumerable.Empty<NulicLicense>();
        }
    }

    public void ApplyOverride(PackageOverride o) => _override = o;

    public static NugetMetadata FromOverride(PackageOverride o)
    {
        var version = o.Version != null ? NuGetVersion.Parse(o.Version) : new NuGetVersion(0, 0, 0);
        var meta = new NugetMetadata(new ManifestMetadata { Id = o.Id, Version = version });
        meta.ApplyOverride(o);
        return meta;
    }

    static IEnumerable<PackageIdentity> GetNugetIdsFrom(MSBuildProject project)
    {
        var package_config = Path.Join(project.FilePath.DirectoryName, NuGetConstants.PackageReferenceFile);

        if (File.Exists(package_config))
        {
            List<PackageReference> packages;
            using (var stream = File.OpenRead(package_config))
                packages = new PackagesConfigReader(stream).GetPackages().ToList();

            return packages.Select(p => p.PackageIdentity);
        }
        else
        {
            var project_assets = Path.Join(project.IntDir, LockFileFormat.AssetsFileName);

            if (File.Exists(project_assets))
            {
                var lock_file = new LockFileFormat().Read(project_assets);

                return lock_file.Libraries
                    .Where(l => l.Type == "package")
                    .Select(l => new PackageIdentity(l.Name, l.Version));
            }
            else if (project.IsSdkStyle)
            {
                throw new Exception($"'{project_assets}' not found (missing nuget restore?)");
            }
        }

        return Enumerable.Empty<PackageIdentity>();
    }

    static readonly string PackagesFolder = GetPackagesFolder();
    static string GetPackagesFolder()
    {
        var settings = Settings.LoadDefaultSettings(null);

        return SettingsUtility.GetGlobalPackagesFolder(settings);
    }
}
