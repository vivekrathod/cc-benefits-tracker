using System.Xml.Linq;

namespace Chitragupta.Api.Tests.Architecture;

/// <summary>
/// Guards the dependency rule: references point inward, and the domain references nothing.
/// The checks read the project files, so a reference is caught the moment it is declared.
/// </summary>
public class LayerDependencyTests
{
    [Fact]
    public void Domain_references_no_project_and_no_package()
    {
        var domain = ProjectFile.Load("Chitragupta.Domain");

        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
    }

    [Fact]
    public void Contracts_reference_no_project_and_no_package()
    {
        var contracts = ProjectFile.Load("Chitragupta.Contracts");

        Assert.Empty(contracts.ProjectReferences);
        Assert.Empty(contracts.PackageReferences);
    }

    [Fact]
    public void Application_references_only_the_domain_and_the_contracts_it_answers_with()
    {
        var application = ProjectFile.Load("Chitragupta.Application");

        Assert.Equal(["Chitragupta.Contracts", "Chitragupta.Domain"], application.ProjectReferences);
        Assert.Empty(application.PackageReferences);
    }

    [Fact]
    public void Infrastructure_references_only_application_and_domain()
    {
        var infrastructure = ProjectFile.Load("Chitragupta.Infrastructure");

        Assert.Equal(["Chitragupta.Application", "Chitragupta.Domain"], infrastructure.ProjectReferences);
    }

    [Fact]
    public void Only_the_api_host_references_infrastructure()
    {
        var referrers = ProjectFile.LoadAllSourceProjects()
            .Where(project => project.ProjectReferences.Contains("Chitragupta.Infrastructure"))
            .Select(project => project.Name);

        Assert.Equal(["Chitragupta.Api"], referrers);
    }

    [Fact]
    public void The_web_app_sees_the_contracts_and_nothing_else_of_the_tracker()
    {
        var web = ProjectFile.Load("Chitragupta.Web");

        Assert.Equal(["Chitragupta.Contracts"], web.ProjectReferences);
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
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Chitragupta.slnx")))
                {
                    directory = directory.Parent;
                }

                return directory?.FullName
                    ?? throw new InvalidOperationException("Chitragupta.slnx was not found above the test output folder.");
            }
        }
    }
}
