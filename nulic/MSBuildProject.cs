using System.Xml.Linq;
using Microsoft.Build.Construction;
using Serilog;

namespace nulic;

internal class MSBuildProject
{
    readonly ProjectRootElement _msbuild_project;
    public FileInfo FilePath => new (_msbuild_project.FullPath);
    public string IntDir
    {
        get
        {
            // if we need to support non-default IntDir maybe lookup 
            // MSBuildProjectExtensionsPath from any Directory.Build.props
            return Path.Join(_msbuild_project.DirectoryPath, "obj");
        }
    }
    public bool IsSdkStyle=> _msbuild_project.Sdk.Any();
    MSBuildProject(ProjectRootElement msbuild_project)
    {
        _msbuild_project = msbuild_project;
    }
    public static IEnumerable<MSBuildProject> LoadFrom(string path, IEnumerable<string>? excludePatterns = null)
    {
        List<MSBuildProject> result = new();

        if (File.Exists(path))
            LoadFrom(new FileInfo(path), result);
        else
            LoadFrom(new DirectoryInfo(path), result);

        if (excludePatterns?.Any() == true)
        {
            result.RemoveAll(p =>
            {
                if (!IsExcluded(p.FilePath.FullName, excludePatterns)) return false;
                Log.Information("Excluded: {path}", Path.GetRelativePath(Environment.CurrentDirectory, p.FilePath.FullName));
                return true;
            });
        }

        return result;
    }
    static bool IsExcluded(string fullPath, IEnumerable<string> patterns)
    {
        // Normalize to forward slashes so patterns work cross-platform
        var normalized = fullPath.Replace('\\', '/');
        return patterns.Any(p => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
            p.Replace('\\', '/'), normalized, ignoreCase: true));
    }
    static void LoadFrom(DirectoryInfo dir, IList<MSBuildProject> list)
    {
        string[] extensions = { "*.sln", "*.slnx", "*.csproj", "*.vbproj", "*.fsproj", "*.vcxproj" };

        foreach (var ext in extensions)
        {
            var files = dir.EnumerateFiles(ext, SearchOption.TopDirectoryOnly).ToList();
            if (files.Count > 0) { LoadFrom(files, list); return; }
        }
    }
    static void LoadFrom(IEnumerable<FileInfo> files, IList<MSBuildProject> list)
    {
        foreach (var file in files)
            LoadFrom(file, list);
    }
    static void LoadFrom(FileInfo file, IList<MSBuildProject> list)
    {
        if (string.Compare(file.Extension, ".sln", true) == 0)
        {
            var sln = SolutionFile.Parse(file.FullName);

            foreach (var project in sln.ProjectsInOrder)
                if (project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
                    LoadFrom(new FileInfo(project.AbsolutePath), list);
        }
        else if (string.Compare(file.Extension, ".slnx", true) == 0)
        {
            // .slnx is XML: <Solution><Project Path="..." /><Folder><Project Path="..." /></Folder></Solution>
            var xml = XDocument.Load(file.FullName);
            foreach (var proj in xml.Descendants("Project"))
            {
                var relPath = proj.Attribute("Path")?.Value;
                if (relPath != null)
                    LoadFrom(new FileInfo(Path.Join(file.DirectoryName, relPath)), list);
            }
        }
        else if (!list.Any(p => p._msbuild_project.FullPath == file.FullName))
        {
            var project = ProjectRootElement.Open(file.FullName);

            list.Add(new MSBuildProject(project));

            var project_elements = project.ItemGroups.SelectMany(g => g.Children);

            foreach (var element in project_elements.Where(c => c.ElementName == "ProjectReference"))
            {
                if (element is ProjectItemElement project_element)
                {
                    var path = Path.Join(file.DirectoryName, project_element.Include);

                    LoadFrom(new FileInfo(path), list);
                }
            }
        }
    }
}
