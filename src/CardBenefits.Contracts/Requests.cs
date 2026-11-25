namespace CardBenefits.Contracts;

public sealed record AddPersonRequest(string Name);

/// <summary>Turns a new feed account into a card. Only the anniversary is required.</summary>
public sealed record ConfirmCardRequest(
    Guid PersonId, string ProductId, int AnniversaryMonth, int AnniversaryDay, DateOnly? OpenedOn, decimal? AnnualFee);

/// <summary>Adds a card that has no feed account, for issuers the Bridge is not connected to.</summary>
public sealed record AddCardRequest(
    Guid PersonId,
    string ProductId,
    DateOnly OpenedOn,
    int? AnniversaryMonth,
    int? AnniversaryDay,
    string? LastFour,
    decimal? AnnualFee);

public sealed record MapAccountRequest(Guid CardId);

public sealed record DismissAccountsRequest(IReadOnlyList<Guid> Ids);

public sealed record ChangeProductRequest(string ProductId, DateOnly On);

public sealed record CloseCardRequest(DateOnly On);

public sealed record UpdateCardRequest(int AnniversaryMonth, int AnniversaryDay, decimal? AnnualFee);

/// <summary>Marks a period used by hand. No amount means everything that is left.</summary>
public sealed record UsePeriodRequest(decimal? Amount, string? Note);

/// <summary>Counts a credit for a benefit. A keyword also teaches a rule for next time.</summary>
public sealed record AssignReviewRequest(string BenefitId, string? Keyword);

/// <summary>Says a credit is not a benefit. A keyword also ignores similar descriptions from now on.</summary>
public sealed record DismissReviewRequest(string? IgnoreKeyword);
