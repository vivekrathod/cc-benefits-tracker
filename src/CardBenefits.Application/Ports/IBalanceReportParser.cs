using CardBenefits.Application.Balances;

namespace CardBenefits.Application.Ports;

/// <summary>Reads what a balance automation printed, one account or several, into reports.</summary>
public interface IBalanceReportParser
{
    /// <summary>Thrown as <see cref="RequestRejectedException"/> when the text is not a result it understands.</summary>
    IReadOnlyList<BalanceReport> Parse(string report);
}
