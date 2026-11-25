namespace CardBenefits.Domain;

/// <summary>A card as an issuer sells it, for example the American Express Gold card.</summary>
public sealed record CardProduct(
    string Id, string Issuer, string Name, Money AnnualFee, bool IsBusiness = false, string? PointsProgram = null)
{
    public string DisplayName => $"{Issuer} {Name}";
}
