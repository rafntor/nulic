namespace unit_tests
{
    [TestClass]
    public class ProjectTest
    {
        [TestMethod]
        [DataRow(@"cppapp_no_nuget", 2)]
        [DataRow(@"netapp_no_nuget", 3)]
        public void ProjectLoad(string path, int count)
        {
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", path));

            var projects = nulic.MSBuildProject.LoadFrom(path);

            Assert.AreEqual(count, projects.Count());
        }
    }
}