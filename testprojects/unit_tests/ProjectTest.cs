namespace unit_tests
{
    [TestClass]
    public class ProjectTest
    {
        static string TestProjectPath(string name) =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", name));

        [TestMethod]
        [DataRow(@"cppapp_no_nuget",    2)]
        [DataRow(@"netapp_no_nuget",    3)]
        [DataRow(@"netlib_with_nuget",  1)]
        [DataRow(@"cpplib_with_nuget",  1)]
        [DataRow(@"netfxlib_with_nuget", 1)]
        public void ProjectLoad(string path, int count)
        {
            var projects = nulic.MSBuildProject.LoadFrom(TestProjectPath(path));
            Assert.AreEqual(count, projects.Count());
        }

        // ── IsSdkStyle ─────────────────────────────────────────────────────

        [TestMethod]
        public void IsSdkStyle_true_for_sdk_project()
        {
            var projects = nulic.MSBuildProject.LoadFrom(TestProjectPath("netlib_with_nuget")).ToArray();
            Assert.AreEqual(1, projects.Length);
            Assert.IsTrue(projects[0].IsSdkStyle, "SDK-style .csproj should report IsSdkStyle=true");
        }

        [TestMethod]
        public void IsSdkStyle_false_for_classic_netfx_project()
        {
            var projects = nulic.MSBuildProject.LoadFrom(TestProjectPath("netfxlib_with_nuget")).ToArray();
            Assert.AreEqual(1, projects.Length);
            Assert.IsFalse(projects[0].IsSdkStyle, "Classic .NET Framework .csproj should report IsSdkStyle=false");
        }

        [TestMethod]
        public void IsSdkStyle_false_for_vcxproj()
        {
            var projects = nulic.MSBuildProject.LoadFrom(TestProjectPath("cpplib_with_nuget")).ToArray();
            Assert.AreEqual(1, projects.Length);
            Assert.IsFalse(projects[0].IsSdkStyle, ".vcxproj should report IsSdkStyle=false");
        }

        // ── Exclude patterns ───────────────────────────────────────────────

        [TestMethod]
        public void ExcludePatterns_reduces_project_count()
        {
            // netapp_no_nuget has 3 projects (itself + netfxlib + netlib)
            // Excluding the netfxlib reference should drop to 2
            var all = nulic.MSBuildProject.LoadFrom(TestProjectPath("netapp_no_nuget"));
            Assert.AreEqual(3, all.Count(), "baseline: expect 3 projects");

            var filtered = nulic.MSBuildProject.LoadFrom(
                TestProjectPath("netapp_no_nuget"),
                excludePatterns: ["*netfxlib*"]);
            Assert.AreEqual(2, filtered.Count(), "after excluding *netfxlib*, expect 2 projects");
        }

        [TestMethod]
        public void ExcludePatterns_null_returns_all()
        {
            var projects = nulic.MSBuildProject.LoadFrom(TestProjectPath("netapp_no_nuget"), excludePatterns: null);
            Assert.AreEqual(3, projects.Count());
        }

        [TestMethod]
        public void ExcludePatterns_empty_returns_all()
        {
            var projects = nulic.MSBuildProject.LoadFrom(TestProjectPath("netapp_no_nuget"), excludePatterns: []);
            Assert.AreEqual(3, projects.Count());
        }
    }
}