# Chitragupta

A self-hosted service that tells a household which credit card benefits have been
used, which are left, and which are about to expire, with as little manual entry as
the data allows.

It is named after Chitragupta, the record keeper of Hindu tradition who keeps the
account of every deed.

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
- **Points**: miles and points per person and program, how old each reading is, and when they lapse.
- **Review**: new feed accounts waiting to be confirmed as cards, and credits the matcher could not settle.
- **Sync**: last successful sync per institution, and a rationed "sync now".
- **Manage**: people, cards, statement imports, product changes.

The web app is installable on a phone (Add to Home Screen). It keeps a copy of itself for fast starts and picks up a new build on its own: when the server has one, an open app reloads once.

## Run it

You need Docker, and a folder outside any git checkout for data and secrets.

```sh
cp .env.example .env          # set CHITRAGUPTA_UID/GID (id -u, id -g), folders, time zone
mkdir -p data secrets         # or wherever CHITRAGUPTA_DATA and CHITRAGUPTA_SECRETS point
docker compose up -d --build
```

Then open `http://<host>:8080`.

The service has no login. Keep it on the home network or behind a VPN such as
Tailscale; do not expose the port to the internet.

### Connect SimpleFIN

1. In the Bridge, create a new app connection and copy its setup token. A token works once.
2. Claim it. The access URL is written to the secrets folder and never shown:

   ```sh
   docker compose run --rm -T -v "$PWD/secrets:/out" --entrypoint dotnet chitragupta \
     cli/chitragupta.dll claim /out/simplefin-access-url < token.txt
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
docker compose exec chitragupta dotnet cli/chitragupta.dll import-cards /data/cards.json
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
Sunday morning. An alert goes out when an automatic balance check needs somebody; see
"Miles and points". Each is sent once.

Reminders go out the first way that is set up:

1. **Gmail.** Put a send-only Gmail token in the secrets folder as `gmail-send-token.json`.
   udaan makes one with a single consent click:
   `udaan gmail-login --send --email you@gmail.com --out <secrets>/gmail-send-token.json`.
   The token can send as that account and can read nothing. It is picked up without a restart.
2. **SMTP.** Set the `SMTP_*` values in `.env` and put the password in `secrets/smtp-password`.
3. **The log**, when neither is set up.

`REMINDER_TO_1` and `REMINDER_TO_2` say who is written to. With Gmail and neither set,
reminders go to the Gmail account itself.

### Miles and points

The Points screen keeps a balance per person and loyalty program, with how old the reading
is and the day the miles lapse when the program gives one. The tracker never signs in to
a loyalty program and holds no login. A balance is typed in on that screen, or read by
udaan, a separate tool that signs in to loyalty programs with a real browser on a desktop.

**Asking udaan.** `udaan serve` answers on its own machine only, behind a token. Point the
tracker at it and the tracker runs the schedule:

```
# on the machine with udaan, once
udaan token --copy-to <secrets>/udaan-token
# in .env
UDAAN_URL=http://host.docker.internal:8791
```

- Each account is checked once a day from `BALANCE_CHECK_AT` (12:30), one account at a
  time, with two accounts of the same program kept apart. A reading udaan already took
  that day counts, so nothing signs in twice for it.
- A failure that may clear by itself (a code that did not arrive, a browser that did not
  start) is tried once more four hours later. Nothing else is retried: a rejected login
  is never sent again, and a site that turned the browser away is left alone until udaan's
  own pause is over.
- A sign-in is tried at most twice a day for an account, the scheduled one and "Check now"
  on the Points screen included. "Check now" takes a reading udaan made in the last hour as
  it is, so one just taken by hand on the desktop arrives without a second sign-in.
- udaan keeps the rules that protect an account (one sign-in at a time, a quarter of an
  hour between two, a hold after a rejected login) and answers "not now" when one applies.
  The tracker waits for the time it is given.

**What you see.** Every attempt is kept for 90 days. The Points screen shows each account's
recent attempts, what went wrong in plain words, and what to do about it. A balance the
automation has not renewed for two days is marked stale, on Points and on Soon, and keeps
the last number that was read: a failed check never erases a balance. The count beside
"Points" is everything there that wants a look.

**What you are told.** One email when a check needs you (a rejected login, a page udaan
does not know), one more if any failure lasts two days, one when it works again, one when
an account has not been heard about for 26 hours, and one when udaan's service has not
answered for half an hour. The Sunday digest says how the checks are doing.

**Pushing results in** still works, with or without the service. udaan's output is
accepted as it is, one result or several, for every account on the same address:

```bash
udaan check aa alex --max-age 72000 | curl -sS -X POST -H 'content-type: application/json' \
  --data-binary @- http://localhost:8080/api/balances/udaan
```

`tools/check-balances.sh` does that for every account udaan knows, for an install that
would sooner run a script from cron than keep the service up. Use one or the other.

Each result says whose it is. Its owner is matched to a person by name, or by first name
while only one person has it; an owner nobody goes by waits on the Points screen and never
creates a person. Sending the same result again changes nothing.

Programs udaan knows that the tracker can read: American AAdvantage, Alaska Atmos Rewards
and United MileagePlus. Miles that lapse show on the Soon screen once they are inside its
window, and reminders go out 90 and 30 days before.

### Settings

Everything is under the `Chitragupta` section (`Chitragupta__Name` as an environment variable).

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
| `Reminders:BalanceNudgeDaysBefore` | `90, 30` | Days before miles or points lapse |
| `Reminders:DigestDay`, `DigestAt` | `Sunday`, `09:00` | Weekly digest |
| `Reminders:To` | none | Who reminders are written to |
| `Gmail:TokenFile` | none | A send-only Gmail token; reminders go this way while the file exists |
| `Smtp:Host`, `Port`, `User`, `PasswordFile`, `From` | none | Reminders by SMTP |
| `Udaan:Url`, `TokenFile` | none | Where `udaan serve` answers, and the file holding its token |
| `Balances:DailyAt` | `12:30` | From when each account is checked once a day |
| `Balances:SignInsPerDay` | `2` | The most sign-ins tried for one account in a day |
| `Balances:RetryAfterHours` | `4` | How long the one retry of the day waits |
| `Balances:StaleAfterDays` | `2` | Age at which a balance udaan read is marked stale |

## Operator CLI

`chitragupta` prints counts and status, never transaction details or secrets.

```
chitragupta status | cards | replay | sync | catalog
chitragupta backup <name>                      (a copy in backups/, safe while the service runs)
chitragupta import-cards <seed.json>
chitragupta import-csv <card id or last four> <file.csv>
chitragupta import-pull <saved-simplefin-response.json>
chitragupta link-suggested
chitragupta claim <access-url-file>            (setup token on standard input)
chitragupta import-catalog <dataset.json> <catalog.json>
```

In the container: `docker compose exec chitragupta dotnet cli/chitragupta.dll <command>`.

Take a copy of the database with `chitragupta backup`, inside the container, or stop the
container first. Do not open or copy `tracker.db` from the host while the container
runs: the host cannot see the container's locks, and SQLite can then drop what the
service wrote last.
From a checkout: `dotnet run --project tools/Chitragupta.Cli -- <command> --data <folder>`.

## How it is built

.NET 10, ASP.NET Core minimal API, EF Core on SQLite, Blazor WebAssembly, xUnit.

```
src/Chitragupta.Domain          periods, cards, the matcher. No dependencies.
src/Chitragupta.Application     use cases and ports (feed, store, catalog, clock, notifier)
src/Chitragupta.Contracts       the records the API returns and accepts
src/Chitragupta.Infrastructure  SQLite store, SimpleFIN and CSV feeds, catalog, email, udaan
src/Chitragupta.Api             composition root, HTTP routes, the daily worker
src/Chitragupta.Web             the web app, served by the API
tools/Chitragupta.Cli           the operator CLI
catalog/                         the source's card products and benefits, and this project's corrections
```

References point inward and tests enforce it. Matching is deterministic: clearing
what the matcher did and replaying every stored transaction gives the same result,
so anything that might affect matching simply replays. The reasoning behind the
design choices is in [docs/decisions.md](docs/decisions.md).

```sh
dotnet test          # everything
dotnet run --project src/Chitragupta.Api
```

The use-case tests run on a real SQLite file with only the feed, the clock and the
notifier faked. Test data is made up; no real statement or account belongs in this
repository.

## What it does not do

- Sign in to a loyalty program. udaan does that, on a desktop, when the tracker asks; see "Miles and points".
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
- **A refresh flags what needs a person**, on "Sync health" and in `chitragupta catalog`: a
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
