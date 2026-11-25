using System.Xml.Linq;

namespace CardBenefits.Api.Tests.Architecture;

/// <summary>
/// Guards the dependency rule: references point inward, and the domain references nothing.
/// The checks read the project files, so a reference is caught the moment it is declared.
/// </summary>
public class LayerDependencyTests
{
    [Fact]
    public void Domain_references_no_project_and_no_package()
    {
        var domain = ProjectFile.Load("CardBenefits.Domain");

        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
    }

    [Fact]
    public void Application_references_only_the_domain()
    {
        var application = ProjectFile.Load("CardBenefits.Application");

        Assert.Equal(["CardBenefits.Domain"], application.ProjectReferences);
    }

    [Fact]
    public void Infrastructure_references_only_application_and_domain()
    {
        var infrastructure = ProjectFile.Load("CardBenefits.Infrastructure");

        Assert.Equal(["CardBenefits.Application", "CardBenefits.Domain"], infrastructure.ProjectReferences);
    }

    [Fact]
    public void Only_the_api_host_references_infrastructure()
    {
        var referrers = ProjectFile.LoadAllSourceProjects()
            .Where(project => project.ProjectReferences.Contains("CardBenefits.Infrastructure"))
            .Select(project => project.Name);

        Assert.Equal(["CardBenefits.Api"], referrers);
    }

    private sealed record ProjectFile(string Name, string[] ProjectReferences, string[] PackageReferences)
    {
        public static ProjectFile Load(string name) =>
            Parse(Path.Combine(SourceRoot, name, $"{name}.csproj"));

        public static IEnumerable<ProjectFile> LoadAllSourceProjects() =>
            Directory.EnumerateFiles(SourceRoot, "*.csproj", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(Parse);

        private static ProjectFile Parse(string path)
        {
            var document = XDocument.Load(path);
            return new ProjectFile(
                Path.GetFileNameWithoutExtension(path),
                Includes(document, "ProjectReference").Select(ProjectName).Order(StringComparer.Ordinal).ToArray(),
                Includes(document, "PackageReference").Order(StringComparer.Ordinal).ToArray());
        }

        private static IEnumerable<string> Includes(XDocument document, string element) =>
            document.Descendants(element).Select(reference => (string?)reference.Attribute("Include") ?? "");

        private static string ProjectName(string include) =>
            Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar));

        private static string SourceRoot => Path.Combine(RepositoryRoot, "src");

        private static string RepositoryRoot
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CardBenefits.slnx")))
                {
                    directory = directory.Parent;
                }

                return directory?.FullName
                    ?? throw new InvalidOperationException("CardBenefits.slnx was not found above the test output folder.");
            }
        }
    }
}
