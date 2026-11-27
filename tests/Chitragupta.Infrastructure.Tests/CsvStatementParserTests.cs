using Chitragupta.Application;
using Chitragupta.Domain;
using Chitragupta.Infrastructure.Feeds.Csv;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>Every sample is made up, in the column layout the issuer exports.</summary>
public class CsvStatementParserTests
{
    private readonly CsvStatementParser _parser = new();

    [Fact]
    public void Chase_writes_purchases_as_negative_and_credits_as_positive()
    {
        var rows = _parser.Parse("""
            Transaction Date,Post Date,Description,Category,Type,Amount,Memo
            11/08/2026,11/09/2026,"TRAVEL CREDIT $300/YEAR",Travel,Adjustment,42.10,
            11/07/2026,11/08/2026,"CAFE, THE GOOD ONE",Food & Drink,Sale,-18.50,
            """);

        Assert.Equal(2, rows.Count);
        Assert.Equal(Money.FromDollars(42.10m), rows[0].Amount);
        Assert.Equal(DateOnly.Parse("2026-11-09"), rows[0].PostedOn);
        Assert.Equal(DateOnly.Parse("2026-11-08"), rows[0].TransactedOn);
        Assert.Equal("TRAVEL CREDIT $300/YEAR", rows[0].Description);
        Assert.Equal(Money.FromDollars(-18.50m), rows[1].Amount);
        Assert.Equal("CAFE, THE GOOD ONE", rows[1].Description);
        Assert.All(rows, row => Assert.False(row.Pending));
    }

    [Fact]
    public void Amex_writes_purchases_as_positive_so_the_sign_is_flipped()
    {
        var rows = _parser.Parse("""
            Date,Description,Card Member,Account #,Amount
            11/09/2026,AMEX Dining Credit,A CARDMEMBER,-00000,-10.00
            11/07/2026,RESTAURANT,A CARDMEMBER,-00000,54.20
            """);

        Assert.Equal([Money.FromDollars(10), Money.FromDollars(-54.20m)], rows.Select(row => row.Amount));
    }

    [Fact]
    public void A_three_column_amex_export_is_recognised_even_when_it_only_holds_credits()
    {
        var rows = _parser.Parse("Date,Description,Amount\n11/09/2026,AMEX Dining Credit,-10.00\n");

        Assert.Equal(Money.FromDollars(10), Assert.Single(rows).Amount);
    }

    [Fact]
    public void Discover_is_flipped_and_bank_of_america_is_not()
    {
        var discover = _parser.Parse("""
            Trans. Date,Post Date,Description,Amount,Category
            11/05/2026,11/06/2026,GROCERY STORE,25.00,Supermarkets
            11/10/2026,11/10/2026,CASHBACK BONUS REDEMPTION,-20.00,Awards and Rebate Credits
            """);
        var bankOfAmerica = _parser.Parse("""
            Posted Date,Reference Number,Payee,Address,Amount
            11/05/2026,0001,GROCERY STORE,CITY ST,-25.00
            11/10/2026,0002,LOUNGE CREDIT,,20.00
            """);

        Assert.Equal([Money.FromDollars(-25), Money.FromDollars(20)], discover.Select(row => row.Amount));
        Assert.Equal([Money.FromDollars(-25), Money.FromDollars(20)], bankOfAmerica.Select(row => row.Amount));
        Assert.Equal("LOUNGE CREDIT", bankOfAmerica[1].Description);
    }

    [Fact]
    public void Debit_and_credit_columns_are_combined_whatever_sign_the_credit_carries()
    {
        var capitalOne = _parser.Parse("""
            Transaction Date,Posted Date,Card No.,Description,Category,Debit,Credit
            2026-11-05,2026-11-06,0000,GROCERY STORE,Merchandise,25.00,
            2026-11-10,2026-11-11,0000,TRAVEL CREDIT,Other,,20.00
            """);
        var citi = _parser.Parse("""
            Status,Date,Description,Debit,Credit
            Cleared,11/05/2026,GROCERY STORE,25.00,
            Cleared,11/10/2026,HOTEL CREDIT,,-20.00
            """);

        Assert.Equal([Money.FromDollars(-25), Money.FromDollars(20)], capitalOne.Select(row => row.Amount));
        Assert.Equal(DateOnly.Parse("2026-11-11"), capitalOne[1].PostedOn);
        Assert.Equal([Money.FromDollars(-25), Money.FromDollars(20)], citi.Select(row => row.Amount));
    }

    [Fact]
    public void An_unknown_layout_is_read_by_assuming_purchases_outnumber_credits()
    {
        var rows = _parser.Parse("""
            Some Bank export
            Account ending 0000

            Transaction Date,Description,Category,Amount
            11/01/2026,SHOP ONE,Shopping,-12.00
            11/02/2026,SHOP TWO,Shopping,"-1,250.00"
            11/03/2026,STATEMENT CREDIT,Credit,$30.00
            """);

        Assert.Equal([Money.FromDollars(-12), Money.FromDollars(-1250), Money.FromDollars(30)], rows.Select(row => row.Amount));
    }

    [Fact]
    public void Ids_are_stable_across_files_and_identical_rows_stay_separate()
    {
        const string october = """
            Date,Description,Amount
            10/09/2026,COFFEE,4.50
            10/09/2026,COFFEE,4.50
            10/20/2026,AMEX Dining Credit,-10.00
            """;
        const string overlapping = """
            Date,Description,Amount
            10/09/2026,COFFEE,4.50
            10/09/2026,coffee,4.50
            10/20/2026,AMEX  Dining  Credit,-10.00
            11/02/2026,COFFEE,4.50
            """;

        var first = _parser.Parse(october).Select(row => row.Id).ToArray();
        var second = _parser.Parse(overlapping).Select(row => row.Id).ToArray();

        Assert.Equal(3, first.Distinct().Count());
        Assert.Equal(first, second[..3]);
        Assert.DoesNotContain(second[3], first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just some text\nwith no header")]
    [InlineData("Date,Description\n11/01/2026,SHOP")]
    public void A_file_that_is_not_a_statement_is_rejected(string csv)
    {
        Assert.Throws<RequestRejectedException>(() => _parser.Parse(csv));
    }
}
