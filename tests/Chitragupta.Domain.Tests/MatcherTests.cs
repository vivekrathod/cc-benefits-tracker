namespace Chitragupta.Domain.Tests;

public class MatcherTests
{
    private const string Dining = "amex-gold/dining-credit";
    private const string Resy = "amex-gold/resy-credit";

    private static readonly Guid Gold = Guid.NewGuid();
    private static readonly Guid OtherCard = Guid.NewGuid();

    private static DateOnly D(string date) => DateOnly.Parse(date);

    private static BenefitPeriod Period(string benefitId, string start, string end, decimal dollars, Guid? cardId = null) =>
        new(Guid.NewGuid(), cardId ?? Gold, benefitId, new DateRange(D(start), D(end)), Money.FromDollars(dollars));

    private static CardTransaction Credit(
        string postedOn, decimal dollars, string description, string feedId = "t1", Guid? cardId = null, bool pending = false) =>
        new(Guid.NewGuid(), cardId ?? Gold, feedId, D(postedOn), Money.FromDollars(dollars), description, pending);

    private static readonly MatchRule[] Rules =
    [
        new(Dining, "dining credit"),
        new(Resy, "resy"),
    ];

    private static MatchResult Match(
        IEnumerable<CardTransaction> transactions, IEnumerable<BenefitPeriod> periods, MatchOptions? options = null) =>
        Matcher.Match(transactions, periods, Rules, options ?? new MatchOptions());

    [Fact]
    public void A_credit_goes_to_the_period_containing_its_posting_date()
    {
        var january = Period(Dining, "2026-01-01", "2026-01-31", 10);
        var february = Period(Dining, "2026-02-01", "2026-02-28", 10);
        var credit = Credit("2026-02-14", 10, "AMEX Dining Credit");

        var result = Match([credit], [january, february]);

        Assert.Equal([new Allocation(credit.Id, february.Id, Money.FromDollars(10))], result.Allocations);
        Assert.Empty(result.Review);
    }

    [Fact]
    public void Keywords_ignore_case_and_repeated_spaces()
    {
        var period = Period(Dining, "2026-01-01", "2026-01-31", 10);

        var result = Match([Credit("2026-01-09", 10, "  amex   DINING    credit  ")], [period]);

        Assert.Single(result.Allocations);
    }

    [Fact]
    public void Purchases_and_pending_transactions_are_left_alone()
    {
        var period = Period(Dining, "2026-01-01", "2026-01-31", 10);

        var result = Match(
            [Credit("2026-01-09", -42, "GRUBHUB"), Credit("2026-01-10", 10, "AMEX Dining Credit", "t2", pending: true)],
            [period]);

        Assert.Empty(result.Allocations);
        Assert.Empty(result.Review);
    }

    [Fact]
    public void A_credit_matching_no_rule_is_flagged_for_review()
    {
        var credit = Credit("2026-01-09", 23.45m, "SOME MERCHANT REFUND");

        var result = Match([credit], [Period(Dining, "2026-01-01", "2026-01-31", 10)]);

        Assert.Empty(result.Allocations);
        Assert.Equal([new ReviewFlag(credit.Id, ReviewReason.Unmatched, Money.FromDollars(23.45m), null)], result.Review);
    }

    [Fact]
    public void Ignored_descriptions_such_as_payments_are_neither_matched_nor_flagged()
    {
        var options = new MatchOptions { IgnoreKeywords = ["payment"] };

        var result = Match(
            [Credit("2026-01-09", 500, "ONLINE PAYMENT - THANK YOU")], [Period(Dining, "2026-01-01", "2026-01-31", 10)], options);

        Assert.Empty(result.Allocations);
        Assert.Empty(result.Review);
    }

    [Fact]
    public void A_credit_above_a_rules_ceiling_does_not_match_it()
    {
        var period = Period(Dining, "2026-01-01", "2026-01-31", 10);
        MatchRule[] rules = [new(Dining, "credit", Money.FromDollars(10))];

        var result = Matcher.Match([Credit("2026-01-09", 250, "TRAVEL CREDIT")], [period], rules, new MatchOptions());

        Assert.Empty(result.Allocations);
        Assert.Equal(ReviewReason.Unmatched, Assert.Single(result.Review).Reason);
    }

    [Fact]
    public void The_longest_matching_keyword_wins()
    {
        var uberCash = Period("amex-platinum/uber-cash", "2026-01-01", "2026-01-31", 15);
        var uberOne = Period("amex-platinum/uber-one", "2026-01-01", "2026-12-31", 120);
        MatchRule[] rules = [new("amex-platinum/uber-cash", "uber"), new("amex-platinum/uber-one", "uber one")];
        var credit = Credit("2026-01-09", 9.99m, "PLATINUM UBER ONE CREDIT");

        var result = Matcher.Match([credit], [uberCash, uberOne], rules, new MatchOptions());

        Assert.Equal(uberOne.Id, Assert.Single(result.Allocations).PeriodId);
    }

    [Fact]
    public void A_benefit_the_card_does_not_have_is_not_a_candidate()
    {
        var otherCardsPeriod = Period(Resy, "2026-01-01", "2026-06-30", 50, OtherCard);
        var credit = Credit("2026-01-09", 50, "AMEX RESY CREDIT");

        var result = Match([credit], [otherCardsPeriod]);

        Assert.Empty(result.Allocations);
        Assert.Equal(ReviewReason.Unmatched, Assert.Single(result.Review).Reason);
    }

    public class GraceWindow
    {
        private static readonly BenefitPeriod[] NoPeriods = [];

        [Fact]
        public void A_credit_posting_just_after_a_period_ends_goes_to_that_period_when_it_has_value_left()
        {
            var january = Period(Dining, "2026-01-01", "2026-01-31", 10);
            var february = Period(Dining, "2026-02-01", "2026-02-28", 10);

            var result = Match([Credit("2026-02-03", 10, "AMEX Dining Credit")], [january, february]);

            Assert.Equal(january.Id, Assert.Single(result.Allocations).PeriodId);
        }

        [Fact]
        public void It_goes_to_the_current_period_when_the_previous_one_is_used_up()
        {
            var january = Period(Dining, "2026-01-01", "2026-01-31", 10);
            var february = Period(Dining, "2026-02-01", "2026-02-28", 10);

            var result = Match(
                [Credit("2026-01-20", 10, "AMEX Dining Credit", "t1"), Credit("2026-02-03", 10, "AMEX Dining Credit", "t2")],
                [january, february]);

            Assert.Equal([january.Id, february.Id], result.Allocations.Select(allocation => allocation.PeriodId));
        }

        [Fact]
        public void It_goes_to_the_current_period_once_the_grace_days_have_passed()
        {
            var january = Period(Dining, "2026-01-01", "2026-01-31", 10);
            var february = Period(Dining, "2026-02-01", "2026-02-28", 10);

            var result = Match([Credit("2026-02-06", 10, "AMEX Dining Credit")], [january, february]);

            Assert.Equal(february.Id, Assert.Single(result.Allocations).PeriodId);
        }

        [Fact]
        public void Grace_days_can_be_set_per_benefit()
        {
            var january = Period(Dining, "2026-01-01", "2026-01-31", 10);
            var february = Period(Dining, "2026-02-01", "2026-02-28", 10);
            var options = new MatchOptions { GraceDays = { [Dining] = 10 } };

            var result = Match([Credit("2026-02-09", 10, "AMEX Dining Credit")], [january, february], options);

            Assert.Equal(january.Id, Assert.Single(result.Allocations).PeriodId);
        }

        [Fact]
        public void A_credit_after_the_last_period_still_reaches_it_within_the_grace_days()
        {
            var december = Period(Dining, "2025-12-01", "2025-12-31", 10);

            var result = Match([Credit("2026-01-02", 10, "AMEX Dining Credit")], [december]);

            Assert.Equal(december.Id, Assert.Single(result.Allocations).PeriodId);
            Assert.Empty(NoPeriods);
        }
    }

    public class Caps
    {
        [Fact]
        public void A_credit_larger_than_what_remains_is_capped_and_the_excess_flagged()
        {
            var period = Period(Resy, "2026-01-01", "2026-06-30", 50);
            var credit = Credit("2026-03-01", 62.10m, "AMEX RESY CREDIT");

            var result = Match([credit], [period]);

            Assert.Equal([new Allocation(credit.Id, period.Id, Money.FromDollars(50))], result.Allocations);
            Assert.Equal([new ReviewFlag(credit.Id, ReviewReason.Excess, Money.FromDollars(12.10m), Resy)], result.Review);
        }

        [Fact]
        public void Several_credits_fill_a_period_in_posting_order()
        {
            var period = Period(Resy, "2026-01-01", "2026-06-30", 50);
            var first = Credit("2026-02-01", 30, "AMEX RESY CREDIT", "a");
            var second = Credit("2026-03-01", 30, "AMEX RESY CREDIT", "b");

            var result = Match([second, first], [period]);

            Assert.Equal(
                [new Allocation(first.Id, period.Id, Money.FromDollars(30)), new Allocation(second.Id, period.Id, Money.FromDollars(20))],
                result.Allocations);
            Assert.Equal(Money.FromDollars(10), Assert.Single(result.Review).Amount);
        }

        [Fact]
        public void A_credit_to_a_used_up_period_is_flagged_whole_with_no_empty_allocation()
        {
            var period = Period(Resy, "2026-01-01", "2026-06-30", 50);

            var result = Match(
                [Credit("2026-02-01", 50, "AMEX RESY CREDIT", "a"), Credit("2026-03-01", 20, "AMEX RESY CREDIT", "b")],
                [period]);

            Assert.Single(result.Allocations);
            Assert.Equal(Money.FromDollars(20), Assert.Single(result.Review).Amount);
        }

        [Fact]
        public void Manual_usage_counts_against_the_cap_but_earlier_automatic_usage_does_not()
        {
            var period = Period(Resy, "2026-01-01", "2026-06-30", 50);
            period.Use(Money.FromDollars(15), UsageSource.Manual);
            period.Use(Money.FromDollars(35), UsageSource.Automatic, Guid.NewGuid());

            var result = Match([Credit("2026-03-01", 50, "AMEX RESY CREDIT")], [period]);

            Assert.Equal(Money.FromDollars(35), Assert.Single(result.Allocations).Amount);
            Assert.Equal(Money.FromDollars(15), Assert.Single(result.Review).Amount);
        }

        [Fact]
        public void A_matching_credit_with_no_period_on_its_date_is_flagged()
        {
            var period = Period(Resy, "2025-01-01", "2025-06-30", 50);
            var credit = Credit("2026-03-01", 50, "AMEX RESY CREDIT");

            var result = Match([credit], [period]);

            Assert.Empty(result.Allocations);
            Assert.Equal([new ReviewFlag(credit.Id, ReviewReason.NoPeriod, Money.FromDollars(50), Resy)], result.Review);
        }
    }

    public class UnverifiedRules
    {
        private static readonly MatchRule[] Unverified = [new("chase-sapphire-preferred/hotel-credit", "hotel", Verified: false)];

        [Fact]
        public void A_credit_matching_only_an_unverified_rule_is_suggested_for_review_and_not_counted()
        {
            var period = Period("chase-sapphire-preferred/hotel-credit", "2026-01-01", "2026-12-31", 100);
            var credit = Credit("2026-03-01", 50, "HOTEL CREDIT");

            var result = Matcher.Match([credit], [period], Unverified, new MatchOptions());

            Assert.Empty(result.Allocations);
            Assert.Equal(
                [new ReviewFlag(credit.Id, ReviewReason.Suggested, Money.FromDollars(50), "chase-sapphire-preferred/hotel-credit")],
                result.Review);
        }

        [Fact]
        public void Once_a_person_confirms_it_the_credit_is_counted()
        {
            var period = Period("chase-sapphire-preferred/hotel-credit", "2026-01-01", "2026-12-31", 100);
            var credit = Credit("2026-03-01", 50, "HOTEL CREDIT");
            var options = new MatchOptions { Pins = { [credit.Id] = "chase-sapphire-preferred/hotel-credit" } };

            var result = Matcher.Match([credit], [period], Unverified, options);

            Assert.Equal(Money.FromDollars(50), Assert.Single(result.Allocations).Amount);
        }

        [Fact]
        public void A_debit_matching_only_an_unverified_rule_is_left_alone()
        {
            var period = Period("chase-sapphire-preferred/hotel-credit", "2026-01-01", "2026-12-31", 100);

            var result = Matcher.Match([Credit("2026-03-01", -180, "GRAND HOTEL")], [period], Unverified, new MatchOptions());

            Assert.Empty(result.Review);
        }
    }

    public class Decisions
    {
        [Fact]
        public void A_pinned_transaction_goes_to_its_benefit_whatever_its_description_says()
        {
            var period = Period(Resy, "2026-01-01", "2026-06-30", 50);
            var credit = Credit("2026-03-01", 18, "CREDIT ADJUSTMENT");
            var options = new MatchOptions { Pins = { [credit.Id] = Resy } };

            var result = Match([credit], [period], options);

            Assert.Equal(period.Id, Assert.Single(result.Allocations).PeriodId);
            Assert.Empty(result.Review);
        }

        [Fact]
        public void A_transaction_pinned_as_not_a_benefit_is_skipped()
        {
            var credit = Credit("2026-03-01", 50, "AMEX RESY CREDIT");
            var options = new MatchOptions { Pins = { [credit.Id] = null } };

            var result = Match([credit], [Period(Resy, "2026-01-01", "2026-06-30", 50)], options);

            Assert.Empty(result.Allocations);
            Assert.Empty(result.Review);
        }

        [Fact]
        public void A_debit_that_matches_a_rule_is_flagged_as_a_reversal()
        {
            var debit = Credit("2026-03-05", -50, "AMEX RESY CREDIT REVERSAL");

            var result = Match([debit], [Period(Resy, "2026-01-01", "2026-06-30", 50)]);

            Assert.Empty(result.Allocations);
            Assert.Equal([new ReviewFlag(debit.Id, ReviewReason.Reversal, Money.FromDollars(50), Resy)], result.Review);
        }
    }

    public class Determinism
    {
        [Fact]
        public void The_result_does_not_depend_on_the_order_transactions_arrive_in()
        {
            BenefitPeriod[] periods =
            [
                Period(Dining, "2026-01-01", "2026-01-31", 10),
                Period(Dining, "2026-02-01", "2026-02-28", 10),
                Period(Resy, "2026-01-01", "2026-06-30", 50),
            ];
            CardTransaction[] transactions =
            [
                Credit("2026-01-15", 10, "AMEX Dining Credit", "a"),
                Credit("2026-02-02", 7, "AMEX Dining Credit", "b"),
                Credit("2026-02-02", 7, "AMEX Dining Credit", "c"),
                Credit("2026-02-20", 40, "AMEX RESY CREDIT", "d"),
                Credit("2026-03-20", 40, "AMEX RESY CREDIT", "e"),
                Credit("2026-03-21", 12, "UNKNOWN REFUND", "f"),
            ];

            var expected = Match(transactions, periods);
            var random = new Random(7);
            for (var run = 0; run < 20; run++)
            {
                var shuffled = transactions.OrderBy(_ => random.Next()).ToArray();
                var actual = Match(shuffled, periods.Reverse());

                Assert.Equal(expected.Allocations, actual.Allocations);
                Assert.Equal(expected.Review, actual.Review);
            }
        }

        [Fact]
        public void Matching_reads_its_inputs_and_changes_nothing()
        {
            var period = Period(Resy, "2026-01-01", "2026-06-30", 50);

            Match([Credit("2026-03-01", 50, "AMEX RESY CREDIT")], [period]);

            Assert.Empty(period.Usage);
        }
    }
}
