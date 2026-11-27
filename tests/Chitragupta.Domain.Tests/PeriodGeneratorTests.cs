namespace Chitragupta.Domain.Tests;

public class PeriodGeneratorTests
{
    private static readonly Money Fifty = Money.FromDollars(50);
    private static readonly Anniversary MidMay = new(5, 15);

    private static DateOnly D(string date) => DateOnly.Parse(date);

    private static DateRange Range(string start, string end) => new(D(start), D(end));

    private static DateRange Year(int year) => new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    private static BenefitTerms Terms(Cadence cadence, YearBasis basis, string effectiveFrom = "2020-01-01", decimal dollars = 50) =>
        new(D(effectiveFrom), Money.FromDollars(dollars), cadence, basis);

    private static string[] Dates(IEnumerable<PeriodSpec> periods) =>
        periods.Select(period => $"{period.Range.Start:O}..{period.Range.End:O}").ToArray();

    private static IReadOnlyList<PeriodSpec> Generate(
        BenefitTerms terms, DateRange window, Anniversary? anniversary = null, DateRange? eligibility = null) =>
        PeriodGenerator.Generate([terms], anniversary ?? MidMay, eligibility ?? DateRange.From(D("2020-01-01")), window);

    public class CalendarYear
    {
        [Fact]
        public void Annual_is_one_period_from_1_January_to_31_December()
        {
            var periods = Generate(Terms(Cadence.Annual, YearBasis.Calendar), Year(2026));

            Assert.Equal(["2026-01-01..2026-12-31"], Dates(periods));
            Assert.Equal(Fifty, periods[0].Amount);
        }

        [Fact]
        public void Semiannual_splits_at_30_June()
        {
            var periods = Generate(Terms(Cadence.Semiannual, YearBasis.Calendar), Year(2026));

            Assert.Equal(["2026-01-01..2026-06-30", "2026-07-01..2026-12-31"], Dates(periods));
        }

        [Fact]
        public void Quarterly_ends_on_each_quarter_end()
        {
            var periods = Generate(Terms(Cadence.Quarterly, YearBasis.Calendar), Year(2026));

            Assert.Equal(
                [
                    "2026-01-01..2026-03-31",
                    "2026-04-01..2026-06-30",
                    "2026-07-01..2026-09-30",
                    "2026-10-01..2026-12-31",
                ],
                Dates(periods));
        }

        [Fact]
        public void Monthly_gives_twelve_periods_ending_on_each_month_end()
        {
            var periods = Generate(Terms(Cadence.Monthly, YearBasis.Calendar), Year(2026));

            Assert.Equal(12, periods.Count);
            Assert.Equal("2026-01-01..2026-01-31", Dates(periods)[0]);
            Assert.Equal("2026-02-01..2026-02-28", Dates(periods)[1]);
            Assert.Equal("2026-12-01..2026-12-31", Dates(periods)[11]);
        }

        [Fact]
        public void February_has_29_days_in_a_leap_year()
        {
            var periods = Generate(Terms(Cadence.Monthly, YearBasis.Calendar), Range("2028-02-01", "2028-02-29"));

            Assert.Equal(["2028-02-01..2028-02-29"], Dates(periods));
        }

        [Fact]
        public void The_anniversary_does_not_move_calendar_periods()
        {
            var periods = Generate(Terms(Cadence.Quarterly, YearBasis.Calendar), Year(2026), new Anniversary(8, 31));

            Assert.Equal("2026-01-01..2026-03-31", Dates(periods)[0]);
        }
    }

    public class CardmemberYear
    {
        [Fact]
        public void Annual_runs_from_the_anniversary_to_the_day_before_the_next()
        {
            var periods = Generate(Terms(Cadence.Annual, YearBasis.Cardmember), Range("2025-05-15", "2026-05-14"));

            Assert.Equal(["2025-05-15..2026-05-14"], Dates(periods));
        }

        [Fact]
        public void Quarterly_steps_three_months_from_the_anniversary()
        {
            var periods = Generate(Terms(Cadence.Quarterly, YearBasis.Cardmember), Range("2025-05-15", "2026-05-14"));

            Assert.Equal(
                [
                    "2025-05-15..2025-08-14",
                    "2025-08-15..2025-11-14",
                    "2025-11-15..2026-02-14",
                    "2026-02-15..2026-05-14",
                ],
                Dates(periods));
        }

        [Fact]
        public void An_anniversary_on_the_31st_clamps_in_short_months_and_returns_to_the_31st()
        {
            var periods = Generate(
                Terms(Cadence.Monthly, YearBasis.Cardmember), Range("2026-01-31", "2026-06-29"), new Anniversary(1, 31));

            Assert.Equal(
                [
                    "2026-01-31..2026-02-27",
                    "2026-02-28..2026-03-30",
                    "2026-03-31..2026-04-29",
                    "2026-04-30..2026-05-30",
                    "2026-05-31..2026-06-29",
                ],
                Dates(periods));
        }

        [Fact]
        public void An_anniversary_on_the_30th_clamps_only_in_February()
        {
            var periods = Generate(
                Terms(Cadence.Monthly, YearBasis.Cardmember), Range("2026-01-30", "2026-04-29"), new Anniversary(1, 30));

            Assert.Equal(
                ["2026-01-30..2026-02-27", "2026-02-28..2026-03-29", "2026-03-30..2026-04-29"],
                Dates(periods));
        }

        [Fact]
        public void A_leap_day_anniversary_starts_on_28_February_in_other_years()
        {
            var periods = Generate(
                Terms(Cadence.Annual, YearBasis.Cardmember), Range("2024-02-29", "2029-02-27"), new Anniversary(2, 29));

            Assert.Equal(
                [
                    "2024-02-29..2025-02-27",
                    "2025-02-28..2026-02-27",
                    "2026-02-28..2027-02-27",
                    "2027-02-28..2028-02-28",
                    "2028-02-29..2029-02-27",
                ],
                Dates(periods));
        }

        [Fact]
        public void A_semiannual_period_from_31_August_reaches_the_end_of_February()
        {
            var periods = Generate(
                Terms(Cadence.Semiannual, YearBasis.Cardmember), Range("2025-08-31", "2026-08-30"), new Anniversary(8, 31));

            Assert.Equal(["2025-08-31..2026-02-27", "2026-02-28..2026-08-30"], Dates(periods));
        }

        [Fact]
        public void A_date_before_this_years_anniversary_belongs_to_the_year_that_started_last_year()
        {
            var periods = Generate(Terms(Cadence.Annual, YearBasis.Cardmember), Range("2026-03-01", "2026-03-01"));

            Assert.Equal(["2025-05-15..2026-05-14"], Dates(periods));
        }
    }

    public class CardLifetime
    {
        [Fact]
        public void A_card_opened_mid_period_gets_a_first_period_from_the_opening_date_at_the_full_amount()
        {
            var periods = Generate(
                Terms(Cadence.Semiannual, YearBasis.Calendar), Year(2026), eligibility: DateRange.From(D("2026-03-10")));

            Assert.Equal(["2026-03-10..2026-06-30", "2026-07-01..2026-12-31"], Dates(periods));
            Assert.Equal(Fifty, periods[0].Amount);
        }

        [Fact]
        public void A_card_closed_mid_period_ends_its_last_period_on_the_closing_date()
        {
            var periods = Generate(
                Terms(Cadence.Quarterly, YearBasis.Calendar), Year(2026), eligibility: Range("2020-01-01", "2026-05-20"));

            Assert.Equal(["2026-01-01..2026-03-31", "2026-04-01..2026-05-20"], Dates(periods));
        }

        [Fact]
        public void A_card_opened_and_closed_inside_one_period_gets_that_one_period()
        {
            var periods = Generate(
                Terms(Cadence.Annual, YearBasis.Calendar), Year(2026), eligibility: Range("2026-02-01", "2026-09-30"));

            Assert.Equal(["2026-02-01..2026-09-30"], Dates(periods));
        }

        [Fact]
        public void No_periods_exist_outside_the_cards_lifetime()
        {
            var periods = Generate(
                Terms(Cadence.Monthly, YearBasis.Calendar), Year(2026), eligibility: Range("2024-01-01", "2025-06-30"));

            Assert.Empty(periods);
        }
    }

    public class ChangingTerms
    {
        [Fact]
        public void A_new_amount_on_a_period_boundary_applies_from_that_period()
        {
            var periods = PeriodGenerator.Generate(
                [
                    Terms(Cadence.Semiannual, YearBasis.Calendar, "2020-01-01", 50),
                    Terms(Cadence.Semiannual, YearBasis.Calendar, "2026-07-01", 75),
                ],
                MidMay,
                DateRange.From(D("2020-01-01")),
                Year(2026));

            Assert.Equal([Money.FromDollars(50), Money.FromDollars(75)], periods.Select(period => period.Amount));
        }

        [Fact]
        public void A_change_inside_a_period_leaves_that_period_on_the_old_terms()
        {
            var periods = PeriodGenerator.Generate(
                [
                    Terms(Cadence.Quarterly, YearBasis.Calendar, "2020-01-01", 50),
                    Terms(Cadence.Quarterly, YearBasis.Calendar, "2026-05-10", 75),
                ],
                MidMay,
                DateRange.From(D("2020-01-01")),
                Year(2026));

            Assert.Equal(
                [Money.FromDollars(50), Money.FromDollars(50), Money.FromDollars(75), Money.FromDollars(75)],
                periods.Select(period => period.Amount));
        }

        [Fact]
        public void A_cadence_change_takes_effect_when_the_running_period_ends()
        {
            var periods = PeriodGenerator.Generate(
                [
                    Terms(Cadence.Annual, YearBasis.Calendar, "2020-01-01", 200),
                    Terms(Cadence.Quarterly, YearBasis.Calendar, "2026-07-01", 50),
                ],
                MidMay,
                DateRange.From(D("2020-01-01")),
                Range("2026-01-01", "2027-06-30"));

            Assert.Equal(
                ["2026-01-01..2026-12-31", "2027-01-01..2027-03-31", "2027-04-01..2027-06-30"],
                Dates(periods));
        }

        [Fact]
        public void A_benefit_introduced_after_the_card_opened_starts_on_its_effective_date()
        {
            var periods = Generate(Terms(Cadence.Annual, YearBasis.Calendar, effectiveFrom: "2026-04-01"), Year(2026));

            Assert.Equal(["2026-04-01..2026-12-31"], Dates(periods));
        }

        [Fact]
        public void Terms_may_be_given_in_any_order()
        {
            var later = Terms(Cadence.Semiannual, YearBasis.Calendar, "2026-07-01", 75);
            var earlier = Terms(Cadence.Semiannual, YearBasis.Calendar, "2020-01-01", 50);

            var periods = PeriodGenerator.Generate([later, earlier], MidMay, DateRange.From(D("2020-01-01")), Year(2026));

            Assert.Equal([Money.FromDollars(50), Money.FromDollars(75)], periods.Select(period => period.Amount));
        }

        [Fact]
        public void No_terms_means_no_periods()
        {
            Assert.Empty(PeriodGenerator.Generate([], MidMay, DateRange.From(D("2020-01-01")), Year(2026)));
        }
    }

    public class Window
    {
        [Fact]
        public void Returns_every_period_that_touches_the_window()
        {
            var periods = Generate(Terms(Cadence.Quarterly, YearBasis.Calendar), Range("2026-03-31", "2026-07-01"));

            Assert.Equal(
                ["2026-01-01..2026-03-31", "2026-04-01..2026-06-30", "2026-07-01..2026-09-30"],
                Dates(periods));
        }

        [Fact]
        public void A_periods_dates_do_not_depend_on_the_window_asked_for()
        {
            var terms = Terms(Cadence.Monthly, YearBasis.Cardmember);
            var anniversary = new Anniversary(1, 31);
            var eligibility = DateRange.From(D("2021-01-31"));
            var narrow = Range("2026-02-10", "2026-04-10");

            var fromWideWindow = PeriodGenerator
                .Generate([terms], anniversary, eligibility, Range("2021-01-01", "2030-12-31"))
                .Where(period => period.Range.Overlaps(narrow));
            var fromNarrowWindow = PeriodGenerator.Generate([terms], anniversary, eligibility, narrow);

            Assert.Equal(Dates(fromWideWindow), Dates(fromNarrowWindow));
        }

        [Fact]
        public void An_open_ended_window_is_refused()
        {
            Assert.Throws<ArgumentException>(
                () => Generate(Terms(Cadence.Monthly, YearBasis.Calendar), DateRange.From(D("2026-01-01"))));
        }
    }

    public class Coverage
    {
        public static TheoryData<Cadence, YearBasis, int, int> Combinations()
        {
            var data = new TheoryData<Cadence, YearBasis, int, int>();
            (int Month, int Day)[] anniversaries = [(1, 1), (1, 31), (2, 28), (2, 29), (5, 15), (8, 31), (12, 31)];
            foreach (var cadence in Enum.GetValues<Cadence>())
            {
                foreach (var basis in Enum.GetValues<YearBasis>())
                {
                    foreach (var (month, day) in anniversaries)
                    {
                        data.Add(cadence, basis, month, day);
                    }
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Combinations))]
        public void Periods_cover_every_day_exactly_once_and_come_at_the_cadence(
            Cadence cadence, YearBasis basis, int month, int day)
        {
            var window = Range("2023-06-01", "2029-05-31");

            var periods = PeriodGenerator.Generate(
                [Terms(cadence, basis)], new Anniversary(month, day), DateRange.From(D("2020-01-01")), window);

            Assert.True(periods[0].Range.Start <= window.Start);
            Assert.True(periods[^1].Range.End >= window.End);
            for (var index = 1; index < periods.Count; index++)
            {
                Assert.Equal(periods[index - 1].Range.End.AddDays(1), periods[index].Range.Start);
            }

            var perYear = 12 / cadence.Months();
            var inside = periods.Where(period => period.Range.Start >= D("2024-01-01") && period.Range.Start <= D("2027-12-31"));
            Assert.All(inside.GroupBy(period => period.Range.Start.Year), year => Assert.Equal(perYear, year.Count()));
        }
    }
}
