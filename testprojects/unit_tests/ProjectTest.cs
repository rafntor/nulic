namespace unit_tests
{
    [TestClass]
    public class ProjectTest
    {
        [TestMethod]
        [DataRow(@"cppapp_no_nuget", 2)]
        [DataRow(@"netapp_no_nuget", 3)]
        [DataRow(@"..\", 7)]
        public void ProjectLoad(string path, int count)
        {
            path = Path.Join(@"..\..\..\..\", path); // solution root

            var projects = nulic.MSBuildProject.LoadFrom(path);

            Assert.AreEqual(count, projects.Count());
        }
    }
}