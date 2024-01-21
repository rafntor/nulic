using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Licenses;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Serilog;
using System.ComponentModel;
using System.Globalization;
using System.IO.Enumeration;
using System.Linq;

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
    string _license_expression = "NOASSERTION"; // https://github.com/spdx/spdx-spec/issues/49
    public string LicenseExpression => _license_expression; // todo .. from nuliclicense
    public Uri LicenseUrl
    {
        get
        {
            if (_manifest.LicenseUrl is Uri uri)
            {
                // return uri; // suppress deprecated licenseurl (legacy package?)
            }

            return new Uri(string.Format(CultureInfo.InvariantCulture, 
                LicenseMetadata.LicenseServiceLinkTemplate, LicenseExpression));
        } 
    }
    // no more
    public override string ToString() => $"{Id}.{Version}";
    public static IEnumerable<NugetMetadata> GetFrom(MSBuildProject project)
    {
        var ids = GetNugetIdsFrom(project);

        return ids.Select(FromPackageId);
    }
    public static async Task CollectInformation(IEnumerable<NugetMetadata> nugets, DirectoryInfo license_root)
    {
        var tasks = nugets.Select(async nuget => await nuget.CollectInformation(license_root));

        await Task.WhenAll(tasks);
    }

    NugetMetadata(ManifestMetadata manifest)
    {
        _manifest = manifest;
        Copyright = manifest.Copyright;
    }
    NugetMetadata(PackageIdentity identity)
    {
        _manifest = new() { Id = identity.Id, Version = identity.Version };
    }
    async Task CollectInformation(DirectoryInfo license_root)
    {
        var licenses = await CollectLicenses(license_root);

        // * remove redundant standard-license-files

        // * detect missing expression(s) from files

        // * detect missing copyright(s) from files

        //        if (license != null && string.IsNullOrEmpty(Copyright))
        //            Copyright = ExtractCopyright(license);

    }
    async Task<IEnumerable<NulicLicense>> CollectLicenses(DirectoryInfo license_root)
    {
        //https://learn.microsoft.com/en-us/nuget/reference/nuspec#license
        //https://learn.microsoft.com/en-us/nuget/nuget-org/licenses.nuget.org

        // 'licenses' contain the relative filepaths from root of the nuget
        IEnumerable<NulicLicense> licenses = await CopyEmbeddedLicenseFiles(license_root);

        var license_data = _manifest.LicenseMetadata;

        if (license_data != null)
        {
            if (license_data.Type == LicenseType.File)
            {
                if (!licenses.Any(l => l.Filepath.Name == license_data.License))
                {
                    var license = await CopyEmbeddedLicenseFile(license_data.License, license_root);

                    licenses = licenses.Append(license);
                }
            }
            else if (license_data.Type == LicenseType.Expression)
            {
                // download by expression goes to 'license_root' directly (not package-specific folder)

                var exp_licenses = await DownloadLicenses(_manifest.LicenseMetadata.LicenseExpression, license_root);

                licenses = licenses.Union(exp_licenses);
            }
        }
        else // legacy mode 'LicenceUrl' ?
        {
            if (_manifest.LicenseUrl is Uri url)
            {
                // create initial path to file in package-specific folder ..
                var dir = license_root.CreateSubdirectory(ToString());
                var file = new FileInfo(Path.Join(dir.FullName, "license.url.txt"));

                // .. but may be redirected if url is recognized as a standard license
                var url_license = await LicenseDownload.DownloadFrom(url, file);
                
                licenses = licenses.Append(url_license);
            }
        }

        return licenses;
    }
    async Task<IEnumerable<NulicLicense>> DownloadLicenses(NuGetLicenseExpression license, DirectoryInfo destination)
    {
        List<Task<NulicLicense>> result = new();

        license.OnEachLeafNode( // licenses and license-exceptions
            (l) => result.Add(SpdxLookup.DownloadLicense(l.Identifier, destination)),
            (e) => result.Add(SpdxLookup.DownloadLicense(e.Identifier, destination))
            );

        return await Task.WhenAll(result);
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
    async Task<NulicLicense> CopyEmbeddedLicenseFile(DownloadResourceResult package, string packagefile, DirectoryInfo destination)
    {
        using var source = package.PackageReader.GetStream(packagefile);

        var relative_path = Path.Join(ToString(), packagefile);

        destination.CreateSubdirectory(Path.GetDirectoryName(relative_path)!);

        var dest = new FileInfo(Path.Join(destination.FullName, relative_path));

        using (var stream = dest.OpenWrite())
            await source.CopyToAsync(stream);

        return await NulicLicense.Create(dest, stream:source);
    }
    async Task<NulicLicense> CopyEmbeddedLicenseFile(string packagefile, DirectoryInfo destination)
    {
        var identity = new PackageIdentity(_manifest.Id, _manifest.Version);

        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        return await CopyEmbeddedLicenseFile(package, packagefile, destination);
    }
    static bool NameMatch(string filepath, string pattern)
    {
        var file = new FileInfo(filepath);

        return FileSystemName.MatchesSimpleExpression(pattern, file.Name);
    }
    async Task<IEnumerable<NulicLicense>> CopyEmbeddedLicenseFiles(DirectoryInfo destination)
    {
        var identity = new PackageIdentity(_manifest.Id, _manifest.Version);

        var package = GlobalPackagesFolderUtility.GetPackage(identity, PackagesFolder);

        var files = await package.PackageReader.GetFilesAsync(CancellationToken.None);

        string[] candidates = { "*license*.*", "*thirdpartynotice*.*", "*credit*.*" };

        files = files.Where(f => candidates.Any(c => NameMatch(f, c)));

        var jobs = files.Select(async f => await CopyEmbeddedLicenseFile(package, f, destination));

        return await Task.WhenAll(jobs);
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
                throw new Exception($"'{project_assets}' not found (missing nuget restore?)");
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
