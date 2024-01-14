using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Licenses;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Serilog;
using System.IO.Enumeration;

namespace nulic;

internal class NugetMetadata
{
    ManifestMetadata _manifest;
    // following properties are json-exported
    // https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices
    public string Id => _manifest.Id;
    public NuGetVersion Version => _manifest.Version;
    public IEnumerable<string> Authors => _manifest.Authors;
    // todo - copyrigth from license if empty
    public string? Copyright { get; private set; }
    public Uri? ProjectUrl => _manifest.ProjectUrl;
    // todo - license deep search
    // _manifest.LicenseUrl/_manifest.Repository.Url;
    public string? License => _manifest.LicenseMetadata?.License;
    // no more
    public override string ToString() => $"{Id}.{Version}";
    NugetMetadata(ManifestMetadata manifest)
    {
        _manifest = manifest;
        Copyright = manifest.Copyright;
    }
    NugetMetadata(PackageIdentity identity)
    {
        _manifest = new() { Id = identity.Id, Version = identity.Version };
    }
    async public Task DiscoverLicense(DirectoryInfo license_root)
    {

        //https://learn.microsoft.com/en-us/nuget/reference/nuspec#license
        //https://learn.microsoft.com/en-us/nuget/nuget-org/licenses.nuget.org

        // 'licenses' contain the relative filepaths from root of the nuget
        IEnumerable<string> licenses = await CopyEmbeddedLicenseFiles(license_root);

        var license_data = _manifest.LicenseMetadata;

        if (license_data != null)
        {
            if (license_data.Type == LicenseType.File)
            {
                if (!licenses.Contains(license_data.License))
                {
                    await CopyEmbeddedLicenseFile(license_data.License, license_root);

                    licenses.Append(license_data.License);
                }
            }
            else if (license_data.Type == LicenseType.Expression)
            {
                // download by expression goes to 'license_root' directly (not package-specific folder)

                await DownloadLicenses(_manifest.LicenseMetadata.LicenseExpression, license_root);
            }
        }
        else // legacy mode 'LicenceUrl' ?
        {
            if (_manifest.LicenseUrl is Uri url)
            {
                var dir = license_root.CreateSubdirectory(ToString());
                var file = new FileInfo(Path.Join(dir.FullName, "license.url.txt"));
                await LicenseDownload.DownloadFrom(url, file);
            }
        }



        //        if (license != null && string.IsNullOrEmpty(Copyright))
        //            Copyright = ExtractCopyright(license);
    }
    async Task DownloadLicenses(NuGetLicenseExpression license, DirectoryInfo destination)
    {
        List<Task> result = new();

        license.OnEachLeafNode( // licenses and license-exceptions
            (l) => result.Add(SpdxLookup.DownloadLicense(l.Identifier, destination)),
            (e) => result.Add(SpdxLookup.DownloadLicense(e.Identifier, destination))
            );

        await Task.WhenAll(result);
    }
    static string? ExtractCopyright(FileInfo fileInfo)
    {
        string? result = null;

        foreach (var line in File.ReadLines(fileInfo.FullName))
        {
            var idx = line.IndexOf("copyright (c)", StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                idx = line.IndexOf("copyright ©", StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                continue;

            var copyright = line.Substring(idx);

            if (result is null)
                result = copyright;
            else
                result = string.Join(Environment.NewLine, result, copyright);
        }

        return result;
    }
    async Task CopyEmbeddedLicenseFile(DownloadResourceResult package, string packagefile, DirectoryInfo destination)
    {
        using var source = package.PackageReader.GetStream(packagefile);

        var dest_dir = destination.CreateSubdirectory(Path.Join(ToString(), Path.GetDirectoryName(packagefile)));
        
        var filepath = Path.Join(dest_dir.FullName, Path.GetFileName(packagefile));

        using var dest = File.Create(filepath);

        await source.CopyToAsync(dest);
    }
    async Task CopyEmbeddedLicenseFile(string packagefile, DirectoryInfo destination)
    {
        var identity = new PackageIdentity(_manifest.Id, _manifest.Version);

        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        await CopyEmbeddedLicenseFile(package, packagefile, destination);
    }
    static bool NameMatch(string filepath, string pattern)
    {
        var file = new FileInfo(filepath);

        return FileSystemName.MatchesSimpleExpression(pattern, file.Name);
    }
    async Task<IEnumerable<string>> CopyEmbeddedLicenseFiles(DirectoryInfo destination)
    {
        var identity = new PackageIdentity(_manifest.Id, _manifest.Version);

        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        var files = await package.PackageReader.GetFilesAsync(CancellationToken.None);

        string[] candidates = { "license*.*", "thirdpartynotice*.*" };

        files = files.Where(f => candidates.Any(c => NameMatch(f, c)));

        var jobs = files.Select(async f => await CopyEmbeddedLicenseFile(package, f, destination));

        await Task.WhenAll(jobs);

        return files;
    }

    public static IEnumerable<NugetMetadata> GetFrom(MSBuildProject project)
    {
        var ids = GetNugetIdsFrom(project);

        return ids.Select(FromPackageId);
    }
    static NugetMetadata FromPackageId(PackageIdentity identity)
    {
        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        if (package?.PackageReader?.GetNuspec() is Stream stream)
        {
            var manifest = Manifest.ReadFrom(stream, true);

            return new NugetMetadata(manifest.Metadata);
        }

        return new NugetMetadata(identity);
    }

    static IEnumerable<PackageIdentity> GetNugetIdsFrom(MSBuildProject project)
    {
        var package_config = Path.Join(project.FilePath.DirectoryName, NuGetConstants.PackageReferenceFile);

        if (File.Exists(package_config))
        {
            using var stream = File.OpenRead(package_config);

            var reader = new PackagesConfigReader(stream);

            return reader.GetPackages().Select(p => p.PackageIdentity);
        }
        else
        {
            var project_assets = Path.Join(project.IntDir, LockFileFormat.AssetsFileName);

            if (File.Exists(project_assets))
            {
                var lock_file = new LockFileFormat().Read(project_assets);

                return lock_file.Libraries.Where(l => l.Type == "package").Select(l => new PackageIdentity(l.Name, l.Version));
            }
            else if (project.IsSdkStyle)
            {
                Log.Fatal($"'{project_assets}' not found (missing nuget restore?)");
                Environment.Exit(-1);
            }
        }

        return Enumerable.Empty<PackageIdentity>();
    }

    static string PackagesFolder = GetPackagesFolder();
    static string GetPackagesFolder()
    {
        var settings = Settings.LoadDefaultSettings(null);

        return SettingsUtility.GetGlobalPackagesFolder(settings);
    }
}
