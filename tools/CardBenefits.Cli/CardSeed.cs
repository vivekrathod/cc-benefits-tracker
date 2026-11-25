using System.Text.Json;
using CardBenefits.Application.Cards;
using CardBenefits.Application.Views;
using CardBenefits.Contracts;

namespace CardBenefits.Cli;

/// <summary>
/// The first-run import of people and cards from a JSON file. Running it again adds
/// nothing that is already there, so the file can be corrected and re-imported.
/// </summary>
internal sealed record CardSeed(IReadOnlyList<string> People, IReadOnlyList<CardSeed.SeedCard> Cards)
{
    internal sealed record SeedCard(
        string Person, string Product, DateOnly OpenedOn, string? Anniversary, string? LastFour, decimal? AnnualFee);

    public static CardSeed Read(string path) =>
        JsonSerializer.Deserialize<CardSeed>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("The seed file is empty.");

    public async Task<(int Added, int Skipped)> ApplyAsync(CardService cards, TrackerQueries queries, CancellationToken cancellationToken)
    {
        var people = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in People.Concat(Cards.Select(card => card.Person)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            people[name] = (await cards.AddPersonAsync(name, cancellationToken)).Id;
        }

        var existing = await queries.GetCardsAsync(cancellationToken);
        var added = 0;
        foreach (var card in Cards)
        {
            var personId = people[card.Person];
            if (existing.Any(known => known.PersonId == personId && known.ProductId == card.Product && known.OpenedOn == card.OpenedOn))
            {
                continue;
            }

            // "MM-dd", for a card whose annual fee does not fall on the day it was opened.
            var anniversary = card.Anniversary?.Split('-');
            await cards.AddCardAsync(
                new AddCardRequest(
                    personId,
                    card.Product,
                    card.OpenedOn,
                    anniversary is null ? null : int.Parse(anniversary[0]),
                    anniversary is null ? null : int.Parse(anniversary[1]),
                    card.LastFour,
                    card.AnnualFee),
                cancellationToken);
            added++;
        }

        return (added, Cards.Count - added);
    }
}
