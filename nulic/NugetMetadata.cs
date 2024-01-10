using NuGet.Packaging.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Protocol;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Microsoft.Build.Experimental.ProjectCache;
using NuGet.Common;
using NuGet.Commands;
using NuGet.ProjectModel;
using System.Security.Principal;

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
    public string? Copyright => _manifest.Copyright;
    public Uri? ProjectUrl => _manifest.ProjectUrl;
    // todo - license deep search
    // _manifest.LicenseUrl/_manifest.Repository.Url;
    public string? License => _manifest.LicenseMetadata?.License;
    // no more
    public override string ToString() => $"{Id}:{Version}";
    NugetMetadata(ManifestMetadata manifest)
    {
        _manifest = manifest;
    }
    NugetMetadata(PackageIdentity identity)
    {
        _manifest = new() { Id = identity.Id, Version = identity.Version };
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

            if (project.IsSdkStyle || File.Exists(project_assets))
            {
                var lock_file = new LockFileFormat().Read(project_assets);

                return lock_file.Libraries.Where(l => l.Type == "package").Select(l => new PackageIdentity(l.Name, l.Version));
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
