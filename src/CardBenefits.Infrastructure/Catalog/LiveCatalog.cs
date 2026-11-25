using CardBenefits.Application.Ports;
using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Catalog;

/// <summary>
/// The catalog the running tracker uses. It is the latest copy of the source that a
/// refresh fetched, or the copy shipped inside the app until there is one, with this
/// project's corrections laid on top. A refresh swaps a new copy in while the service runs.
/// </summary>
public sealed class LiveCatalog : ICatalog
{
    private readonly CatalogOverrides _overrides;
    private readonly string? _file;
    private volatile Loaded _loaded;
    private volatile Attempt _attempt;

    private LiveCatalog(Loaded loaded, CatalogOverrides overrides, string? file, string? problem)
    {
        _loaded = loaded;
        _overrides = overrides;
        _file = file;
        _attempt = new Attempt(null, problem);
    }

    /// <summary>False when a fixed catalog file was configured: that file is then used as it is.</summary>
    public bool FollowsSource => _file is not null;

    public CatalogStatus Status
    {
        get
        {
            var (loaded, attempt) = (_loaded, _attempt);
            return new CatalogStatus(
                FollowsSource,
                loaded.Source.Source,
                loaded.Catalog.Products.Count,
                loaded.Source.RetrievedAt,
                attempt.At ?? loaded.Source.RetrievedAt,
                attempt.Problem,
                loaded.Flags);
        }
    }

    public IReadOnlyList<CardProduct> Products => _loaded.Catalog.Products;

    public IReadOnlyList<MatchRule> Rules => _loaded.Catalog.Rules;

    public IReadOnlyList<string> IgnoreKeywords => _loaded.Catalog.IgnoreKeywords;

    public IReadOnlyDictionary<string, int> GraceDays => _loaded.Catalog.GraceDays;

    public CardProduct? FindProduct(string productId) => _loaded.Catalog.FindProduct(productId);

    public IReadOnlyList<BenefitDefinition> BenefitsOf(string productId) => _loaded.Catalog.BenefitsOf(productId);

    public BenefitDefinition? FindBenefit(string benefitId) => _loaded.Catalog.FindBenefit(benefitId);

    public IReadOnlyList<string> AliasesOf(string productId) => _loaded.Catalog.AliasesOf(productId);

    public string? NotesOf(string benefitId) => _loaded.Catalog.NotesOf(benefitId);

    internal static LiveCatalog Open(TrackerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.CatalogPath))
        {
            using var stream = File.OpenRead(options.CatalogPath);
            return new LiveCatalog(Build(CatalogFile.Read(stream), CatalogOverrides.None), CatalogOverrides.None, null, null);
        }

        return Open(options.CatalogSourceFile, CatalogOverrides.BuiltIn());
    }

    internal static LiveCatalog Open(string file, CatalogOverrides overrides)
    {
        if (File.Exists(file))
        {
            try
            {
                using var stream = File.OpenRead(file);
                return new LiveCatalog(Build(CatalogFile.Read(stream), overrides), overrides, file, null);
            }
            catch (Exception unreadable) when (unreadable is not OutOfMemoryException)
            {
                // A damaged copy must not keep the tracker from starting. The shipped one takes over
                // and the next refresh writes a good copy again.
                return new LiveCatalog(
                    Build(CatalogFile.BuiltIn(), overrides), overrides, file,
                    $"The saved copy of the source could not be read ({unreadable.GetType().Name}), so the catalog shipped with the app is in use.");
            }
        }

        return new LiveCatalog(Build(CatalogFile.BuiltIn(), overrides), overrides, file, null);
    }

    /// <summary>The copy of the source in use, before corrections.</summary>
    internal CatalogFile Source => _loaded.Source;

    /// <summary>When the last refresh failed, if the latest one did.</summary>
    internal DateTimeOffset? FailedAt => _attempt is { Problem: not null, At: { } at } ? at : null;

    /// <summary>
    /// Takes a new copy of the source into use and saves it. It is built in full first,
    /// so a copy that does not hold together changes nothing.
    /// </summary>
    internal void Replace(CatalogFile source, DateTimeOffset at)
    {
        var loaded = Build(source, _overrides);

        var file = _file ?? throw new InvalidOperationException("A fixed catalog file is in use, so there is no source to follow.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var draft = file + ".new";
        File.WriteAllText(draft, source.ToJson());
        File.Move(draft, file, overwrite: true);

        _loaded = loaded;
        _attempt = new Attempt(at, null);
    }

    internal void RecordFailure(DateTimeOffset at, string problem) => _attempt = new Attempt(at, problem);

    private static Loaded Build(CatalogFile source, CatalogOverrides overrides)
    {
        var (corrected, flags) = overrides.Apply(source);
        return new Loaded(source, new JsonCatalog(corrected), flags);
    }

    private sealed record Loaded(CatalogFile Source, JsonCatalog Catalog, IReadOnlyList<string> Flags);

    private sealed record Attempt(DateTimeOffset? At, string? Problem);
}

/// <summary>Where the catalog stands against its source, for the Sync screen and the CLI.</summary>
public sealed record CatalogStatus(
    bool FollowsSource,
    string? Source,
    int Products,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset? CheckedAt,
    string? Problem,
    IReadOnlyList<string> Flags);
