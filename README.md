# Card Benefits Tracker

A self-hosted service that tells a household which credit card benefits have been
used, which are left, and which are about to expire, with as little manual entry as
the data allows.

Issuers do not publish a "benefits used" feed, so usage is inferred. When a benefit is
used, the issuer posts a statement credit on the card a few days later. The service
pulls transactions from a [SimpleFIN Bridge](https://beta-bridge.simplefin.org/)
connection once a day, matches credits against a catalog of each card's benefits, and
allocates them to the right benefit period. Benefits that never reach the statement
are marked used with one tap.

## What it shows

- **Soon**: unused or partly used benefits ending in the next days, for everyone, soonest first.
- **Cards**: per card, each benefit's current period with used and remaining.
- **Value**: benefits used this cardmember year against the annual fee.
- **Review**: new feed accounts waiting to be confirmed as cards, and credits the matcher could not settle.
- **Sync**: last successful sync per institution, and a rationed "sync now".
- **Manage**: people, cards, statement imports, product changes.

The web app is installable on a phone (Add to Home Screen).

## Run it

You need Docker, and a folder outside any git checkout for data and secrets.

```sh
cp .env.example .env          # set TRACKER_UID/GID (id -u, id -g), folders, time zone
mkdir -p data secrets         # or wherever TRACKER_DATA and TRACKER_SECRETS point
docker compose up -d --build
```

Then open `http://<host>:8080`.

The service has no login. Keep it on the home network or behind a VPN such as
Tailscale; do not expose the port to the internet.

### Connect SimpleFIN

1. In the Bridge, create a new app connection and copy its setup token. A token works once.
2. Claim it. The access URL is written to the secrets folder and never shown:

   ```sh
   docker compose run --rm -T -v "$PWD/secrets:/out" --entrypoint dotnet tracker \
     cli/tracker.dll claim /out/simplefin-access-url < token.txt
   ```

   Any other way of producing the file works too: it holds the access URL on one line,
   readable only by its owner.
3. Restart the container. The first sync runs at the next daily slot, or press **Sync now**.

The access URL is a credential to bank data. It lives only in that file: not in
configuration, not in the database, not in logs, and error messages never quote it.

### First run

1. **Manage**: add the people in the household.
2. **Review**: every account the feed reports waits here. Confirm the cards (the product
   is suggested from the account name, the anniversary from the last annual fee charge
   when one is in the data) and dismiss everything else. Dismissing removes the
   account's stored transactions and hides it for good. An account that can only be
   one of the cards already entered is linked to it by the sync and never waits; one
   that could be two cards, or is named after the cardholder only, needs a person.
3. For a card whose issuer is not connected in the Bridge, add it under **Manage** and
   mark its benefits by hand, or import the issuer's CSV export there.
4. To reach back further than the feed does (about 90 days), import CSV exports per card.
   Overlap with the feed is handled; importing a file twice changes nothing.

People and cards can also be loaded from a seed file:

```sh
docker compose exec tracker dotnet cli/tracker.dll import-cards /data/cards.json
```

```json
{
  "people": ["Alex", "Sam"],
  "cards": [
    { "person": "Alex", "product": "amex-gold", "openedOn": "2025-05-06", "anniversary": "05-06", "annualFee": 325 }
  ]
}
```

`product` is an id from `catalog/catalog.json` (the source's own ids). `anniversary` (MM-dd) and `annualFee`
are optional.

### Reminders

A nudge goes out 7 and 2 days before a period with value left ends, and a digest on
Sunday morning. Set the `SMTP_*` and `REMINDER_TO_*` values in `.env` and put the
password in `secrets/smtp-password`. Without SMTP settings, reminders are written to
the log.

### Settings

Everything is under the `Tracker` section (`Tracker__Name` as an environment variable).

| Setting | Default | Meaning |
| --- | --- | --- |
| `DataDirectory` | `data` | `tracker.db` and `backups/` (one copy a day, seven kept) |
| `TimeZone` | `America/New_York` | Periods, "today" and the schedule follow it |
| `TrackingStart` | one year back | Nothing before this date is tracked |
| `Catalog:RefreshDays` | `7` | Days between downloads of the catalog's source; `0` never downloads |
| `Catalog:SourceUrl` | the community dataset | Where the catalog's source is downloaded from |
| `CatalogPath` | none | A catalog file to use exactly as it is: no refresh, no corrections |
| `SimpleFin:AccessUrlFile` | none | File holding the access URL |
| `SimpleFin:DailyAt` | `05:17` | When the daily sync runs |
| `SimpleFin:WindowDays` | `14` | Days each sync asks for, overlapping the last |
| `SimpleFin:MinutesBetweenSyncs` | `120` | Spacing enforced on "sync now" |
| `Reminders:NudgeDaysBefore` | `7, 2` | Days before a period ends |
| `Reminders:DigestDay`, `DigestAt` | `Sunday`, `09:00` | Weekly digest |
| `Smtp:Host`, `Port`, `User`, `PasswordFile`, `From`, `To` | none | Email for reminders |

## Operator CLI

`tracker` prints counts and status, never transaction details or secrets.

```
tracker status | cards | replay | sync | catalog
tracker import-cards <seed.json>
tracker import-csv <card id or last four> <file.csv>
tracker import-pull <saved-simplefin-response.json>
tracker link-suggested
tracker claim <access-url-file>            (setup token on standard input)
tracker import-catalog <dataset.json> <catalog.json>
```

In the container: `docker compose exec tracker dotnet cli/tracker.dll <command>`.
From a checkout: `dotnet run --project tools/CardBenefits.Cli -- <command> --data <folder>`.

## How it is built

.NET 10, ASP.NET Core minimal API, EF Core on SQLite, Blazor WebAssembly, xUnit.

```
src/CardBenefits.Domain          periods, cards, the matcher. No dependencies.
src/CardBenefits.Application     use cases and ports (feed, store, catalog, clock, notifier)
src/CardBenefits.Contracts       the records the API returns and accepts
src/CardBenefits.Infrastructure  SQLite store, SimpleFIN and CSV feeds, catalog, email
src/CardBenefits.Api             composition root, HTTP routes, the daily worker
src/CardBenefits.Web             the web app, served by the API
tools/CardBenefits.Cli           the operator CLI
catalog/                         the source's card products and benefits, and this project's corrections
```

References point inward and tests enforce it. Matching is deterministic: clearing
what the matcher did and replaying every stored transaction gives the same result,
so anything that might affect matching simply replays. The reasoning behind the
design choices is in [docs/decisions.md](docs/decisions.md).

```sh
dotnet test          # everything
dotnet run --project src/CardBenefits.Api
```

The use-case tests run on a real SQLite file with only the feed, the clock and the
notifier faked. Test data is made up; no real statement or account belongs in this
repository.

## What it does not do

- Points and miles balances (phase 2; the schema reserves a balance snapshot table).
- Benefits on a cycle other than monthly, quarterly, semiannual or annual (a Global
  Entry credit every four years), and benefits that unlock after a spend threshold.
- Anything that needs an issuer login.

## Catalog

The card products and their benefits come from the community dataset
[us-credit-card-benefits](https://github.com/tiepnv2508/us-credit-card-benefits) (MIT,
see `catalog/NOTICE.md`), and the tracker stays in step with it:

- **Names and ids are the source's, untouched.** `catalog/catalog.json` is a plain
  conversion of the dataset and is never edited by hand; a test fails if it is. Other
  names for a card (the issuer's full legal name, what people call it) are recorded as
  aliases, which only the search uses.
- **Corrections live apart, in `catalog/overrides.json`**, keyed by the source's ids:
  a benefit added, replaced or hidden, each with its reason and a mark of the source
  row it was written against.
- **The running service refreshes itself.** Once a week it downloads the dataset,
  converts it, lays the corrections on top and replays the matching if anything
  changed. The copy is kept in `catalog-source.json` in the data folder. "Sync health"
  shows when it was last fetched and has a button to do it now.
- **A refresh flags what needs a person**, on "Sync health" and in `tracker catalog`: a
  correction whose source row has changed or gone since it was written (the correction
  still wins), and anything kept after the source dropped it.
- **A refresh never takes anything from a card.** A product the source dropped stays
  while a card has it, and a dropped benefit stays, as ended, while use is recorded
  against it. If the download fails or the file looks broken, the catalog in use stays
  and the problem is shown.

The dataset's keywords are single words, so every rule is capped at the benefit's
amount, and a rule nobody has verified against a real statement only suggests a match
for a person to confirm. The source is maintained by volunteers and its changes come in
unreviewed: check the entries for the cards you hold against the issuer's current terms.
