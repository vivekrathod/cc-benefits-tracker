using Chitragupta.Domain;

namespace Chitragupta.Application.Balances;

internal static class Owners
{
    /// <summary>
    /// An automation names an owner in its own words. That is a person's whole name or,
    /// while only one person has it, their first name. Anything else waits for a person
    /// to sort out: a result never creates somebody.
    /// </summary>
    public static Person? Find(IReadOnlyList<Person> people, string owner)
    {
        var name = owner.Trim();
        var whole = people.Where(person => person.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (whole.Length == 1)
        {
            return whole[0];
        }

        var first = people
            .Where(person => person.Name.Split(' ', 2)[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return first.Length == 1 ? first[0] : null;
    }
}
