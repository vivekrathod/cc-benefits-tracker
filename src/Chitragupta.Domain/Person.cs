namespace Chitragupta.Domain;

/// <summary>A member of the household who holds cards.</summary>
public sealed record Person
{
    public Person(Guid id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name.Trim();
    }

    public Guid Id { get; }

    public string Name { get; }
}
