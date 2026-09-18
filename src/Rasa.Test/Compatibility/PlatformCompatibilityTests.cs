using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Compatibility
{
    [TestClass]
    public class PlatformCompatibilityTests
    {
        [TestMethod]
        public void RepositoryPinsVerifiedDotNetSdk()
        {
            var repositoryRoot = FindRepositoryRoot();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
            var sdk = document.RootElement.GetProperty("sdk");

            Assert.AreEqual("10.0.401", sdk.GetProperty("version").GetString());
            Assert.AreEqual("disable", sdk.GetProperty("rollForward").GetString());
            Assert.IsFalse(sdk.GetProperty("allowPrerelease").GetBoolean());
        }

        [TestMethod]
        public void EverySolutionProjectTargetsDotNet10()
        {
            var repositoryRoot = FindRepositoryRoot();
            var solution = File.ReadAllText(Path.Combine(repositoryRoot, "Rasa.NET.sln"));
            var projectPaths = solution.Split('\n')
                .Where(line => line.StartsWith("Project(", System.StringComparison.Ordinal))
                .Select(line => line.Split(',')[1].Trim().Trim('"'))
                .Where(path => path.EndsWith(".csproj", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.AreEqual(10, projectPaths.Length);
            foreach (var projectPath in projectPaths)
            {
                var project = XDocument.Load(Path.Combine(
                    repositoryRoot,
                    projectPath.Replace('\\', Path.DirectorySeparatorChar)));
                Assert.AreEqual("net10.0", ReadProperty(project, "TargetFramework"), projectPath);
            }
        }

        [TestMethod]
        public void NavigationProjectsKeepPr95Dependencies()
        {
            var repositoryRoot = FindRepositoryRoot();
            var game = XDocument.Load(Path.Combine(repositoryRoot, "src", "Rasa.Game", "Rasa.Game.csproj"));
            var navigation = XDocument.Load(Path.Combine(repositoryRoot, "src", "Rasa.Navigation", "Rasa.Navigation.csproj"));
            var navMesh = XDocument.Load(Path.Combine(repositoryRoot, "src", "Rasa.NavMesh", "Rasa.NavMesh.csproj"));

            CollectionAssert.Contains(ReadProjectReferences(game).ToArray(), @"..\Rasa.Navigation\Rasa.Navigation.csproj");
            Assert.AreEqual("2026.3.1", ReadPackageVersion(navigation, "DotRecast.Detour"));
            Assert.AreEqual("2026.3.1", ReadPackageVersion(navMesh, "DotRecast.Recast"));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Rasa.NET.sln")))
                directory = directory.Parent;

            Assert.IsNotNull(directory, "Could not locate the repository root from the test output directory.");
            return directory.FullName;
        }

        private static string ReadProperty(XDocument project, string name)
        {
            return project.Descendants(name).Single().Value;
        }

        private static IEnumerable<string> ReadProjectReferences(XDocument project)
        {
            return project.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(reference => reference != null);
        }

        private static string ReadPackageVersion(XDocument project, string package)
        {
            return project.Descendants("PackageReference")
                .Single(reference => reference.Attribute("Include")?.Value == package)
                .Attribute("Version")?.Value;
        }
    }
}
