namespace CardBenefits.Web;

/// <summary>
/// What the person chose to see, kept while the app stays open so that moving between
/// screens or marking a benefit does not fold everything back up.
/// </summary>
internal sealed class ViewChoices
{
    /// <summary>The cards opened on This period.</summary>
    public HashSet<Guid> OpenCards { get; } = [];

    /// <summary>Whether cards with nothing recurring to track are listed too.</summary>
    public bool ShowCardsWithoutBenefits { get; set; }

    public void Toggle(Guid cardId)
    {
        if (!OpenCards.Remove(cardId))
        {
            OpenCards.Add(cardId);
        }
    }
}
