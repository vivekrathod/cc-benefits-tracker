using CardBenefits.Domain;

namespace CardBenefits.Application.Review;

/// <summary>A matcher flag as stored: one open question about one transaction.</summary>
public sealed record ReviewItem(Guid Id, Guid CardId, Guid TransactionId, ReviewReason Reason, Money Amount, string? BenefitId);
