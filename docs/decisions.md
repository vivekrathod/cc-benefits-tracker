# Decisions

Short notes on the choices that are not obvious from the code, newest concerns first.

## Matching

- **Replay instead of incremental updates.** The matcher is a pure function of the
  transactions, periods, rules and a person's decisions. Every change that could
  affect a match (a sync, a confirmed card, a correction, a new rule) clears the
  matched usage and replays everything. At household scale this is instant, and it
  means a crash between two writes repairs itself on the next replay.
- **Decisions are stored, not results.** Assigning a credit, saying it is not a
  benefit, or acknowledging a flag is recorded against the transaction. The replay
  reads those decisions, so it stays deterministic.
- **Unverified rules only suggest.** The community dataset's keywords are single
  words such as "hotel". A rule carries the benefit amount as a ceiling, and a rule
  not verified against a real statement puts its matches in review as suggestions.
  Confirming one with a keyword stores a verified rule, which outranks the catalog's.
- **Grace window.** A credit that posts within five days after a period ended counts
  for that period while it has value left, because credits trail purchases.
- **Only posted credits count.** Pending transactions are stored but ignored, and a
  stored pending transaction that disappears from the feed is removed, since some
  issuers post under a new id.
- **Manual usage counts against the cap** and survives a replay; matched usage is
  rebuilt around it.

## Feeds

- **Overlapping sources.** A card can have a CSV backfill next to the live feed, or
  the old and new account of a re-linked bank connection. A better source owns every
  day from its earliest transaction onward; a lesser one is used only before that.
  Live outranks CSV, and within a feed the account seen last outranks the older one.
  This avoids fuzzy de-duplication. One live account per card is assumed: an
  authorised-user account that shows up separately should be dismissed.
- **Feed account ids are not identity.** Cards have their own ids; feed accounts map
  to them. An unknown account always waits in the queue: it is never dropped and
  never becomes a new card by itself.
- **An account is linked without asking only when nothing else fits.** After each
  sync, a new account whose name gives a product is linked to an existing card when
  exactly one open card of that product fits and no other new account wants it. With
  a last four on both sides the digits must match; without, the card must not have a
  live account yet. Linking copies the account's last four to a card that has none.
  Two cards of one product, two accounts for one card, or an account named only after
  the cardholder wait for a person. A one-word product name ("Gold", "Cash+") is
  believed only when the connection or the account names the issuer.
- **Transactions of undecided accounts are kept** so that confirming a card matches
  what is already there. Dismissing an account deletes its transactions.
- **Posted dates are local dates.** The Bridge sends epoch seconds, mostly at noon
  UTC but sometimes a real timestamp, so they are converted in the household's time
  zone.
- **The request window** is the last 14 days, 45 on the first run, and widens after
  a gap so nothing between two successful syncs is skipped. The Bridge caps requests
  near 90 days and disables a token after about 24 requests a day, so "sync now" is
  spaced two hours apart.
- **CSV rows have no ids**, so one is derived from date, amount and description, and
  identical rows on one day are numbered. Sign conventions differ by issuer and are
  recognised from the header row.
- **The access URL** is read from a file each time, split into endpoint and
  credentials immediately, and never placed in a URL, a log line or an exception.

## Periods

- **Periods are regenerated, not edited.** They are computed for a window from the
  tracking start (one year back by default) to 60 days ahead. A stored period keeps
  its identity and manual usage while its benefit and first day stay the same.
- **A period keeps the terms in force on its first day**, and is cut short when a
  card opens, closes or changes product mid-period, at the full amount.
- **Cardmember-year boundaries are counted from the anniversary itself**, so a
  clamped date (the 31st in a 30-day month) never shifts the ones after it.
- **Card value** counts a period in the cardmember year it ends in.

## Structure

- **One persistence port** (`ITrackerStore`) rather than a repository per aggregate:
  the use cases read and write a handful of things together and the single port keeps
  the database a detail of Infrastructure.
- **Contracts are a leaf project.** The query services answer with the records the
  API returns, so the web app shares them without referencing the domain. The
  application layer depends on that leaf and on the domain, nothing else.
- **Use-case tests use the real SQLite store.** Fakes exist only for the feed, the
  clock and the notifier. The store is the part most likely to disagree with a fake.
- **Money is whole cents**, dates are `DateOnly`, enums are stored by name.
- **The points program** is on the card product, and the balance snapshot table is
  reserved for phase 2; nothing writes to it yet.

## Operations

- **No login.** The service is meant for a home network or a VPN.
- **One background worker** wakes every ten minutes and runs whatever is due: the
  daily sync, rolling periods forward, reminders, a nightly database copy. Each step
  decides for itself whether it is due, so restarts neither repeat nor skip.
- **Reminders are recorded when sent**, so a nudge goes out once per threshold; a
  failed send is forgotten and retried.
- **Logs carry counts and status**, not descriptions or amounts.
