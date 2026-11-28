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

## Miles and points

- **The tracker asks, and udaan signs in.** Reading a balance means signing in to a
  loyalty program with a real browser, a password and a one-time code. That lives in a
  separate tool (udaan) on a machine with a desktop: from a container the programs' sites
  turned its browser away. udaan answers checks over HTTP on its own machine, and the
  tracker asks it. The tracker still holds no login, and a transaction "sync now" can
  never set off a sign-in. Results can also be pushed in, as before the service existed.
- **The schedule is the tracker's, the safety rules are udaan's.** The tracker decides
  when an account is due, keeps the history and sends the alerts, because it is the part
  that is always up and has the screens. udaan decides whether a sign-in may happen now:
  one at a time, a quarter of an hour apart, never again after a rejected login. It
  answers "not now" with a time, and the tracker waits. Neither can talk the other out of
  its half.
- **What is due is worked out from the history, not remembered.** An account is due when
  nothing was heard about it today and the daily time has passed. So a restart neither
  repeats nor skips a check, a result somebody pushed in by hand counts, and there is no
  job table to keep in step with reality.
- **Sign-ins are rationed, and most failures are not retried.** Two a day for an account,
  everything included. Only a failure udaan marks as safe to repeat gets a retry, once,
  hours later. A locked account costs far more than a balance that is a day old.
- **A refusal is kept, a "not now" is not.** When udaan declines because an account is
  paused or held, that is the state of the account for the day and goes in the history,
  marked as no sign-in. "Not now" is only about timing.
- **One address for every account.** A result names its account, owner and program, so
  the address does not have to.
- **Everything known about udaan's results is in one parser**: its program keys, which
  number in a result is the balance, how it words a failure, and what a person can do
  about each kind of failure. A program it does not list is reported as unknown instead
  of guessed at. How to reach its service is in one adapter beside it.
- **Readings are only added, and the newest is the balance.** A result's own check time
  decides, so the same result sent twice, or an old one arriving late, changes nothing.
- **A failed check never touches a balance.** Attempts are stored apart from readings.
  Every attempt is kept for 90 days, and the newest is how the account is doing now.
- **Stale is about the reading, failing is about the check.** A balance udaan read is
  marked stale at two days old, whatever the reason, and the mark names the reason: the
  check that is failing, or that nothing was heard. A balance typed in is never stale.
- **An alert is said once.** Each is recorded with the failure it is about, so a failure
  that repeats daily is one email, then one more if it lasts two days, then one when it
  clears. Alerts are plain rules over the history. Nothing with judgement sits between a
  failure and the email about it.
- **Reminders go through the Gmail API with an approval that can only send.** No password
  is stored, and the approval cannot read mail. The way out is picked when a reminder is
  sent, so approving takes effect without a restart.
- **The service asks for that approval itself**, from a button under Manage, and keeps it
  in the data folder. It is the one that decides to send, so it owns the consent and the
  token; nothing is borrowed from udaan's mailbox access, which only reads. The message
  that udaan is down must also not need udaan.
- **Google sends the browser back to the app's own front page**, and the app hands the
  code to the API. A desktop OAuth client may only come back to this computer, on any
  port, and the front page is the one address that is certain to be accepted and that a
  saved copy of the app also answers. The sign-in is therefore started from
  `http://localhost` on the machine that runs the service, once.
- **An owner is matched to a person by name**, whole or first, and only when exactly one
  person fits. Anyone else waits: a result never creates a person, as a feed account
  never creates a card.
- **Program names are the catalog's where it has one**, so a balance reads like the
  program on the card. They are compared without regard to case and the first spelling
  seen is kept.
- **Miles get their own, much earlier reminders** (90 and 30 days), keyed by the day they
  lapse: activity in the program moves that day out, and the new day earns new reminders.

## Periods

- **Periods are regenerated, not edited.** They are computed for a window from the
  tracking start (one year back by default) to 60 days ahead. A stored period keeps
  its identity and manual usage while its benefit and first day stay the same.
- **A period keeps the terms in force on its first day**, and is cut short when a
  card opens, closes or changes product mid-period, at the full amount.
- **Cardmember-year boundaries are counted from the anniversary itself**, so a
  clamped date (the 31st in a 30-day month) never shifts the ones after it.
- **Card value** counts a period in the cardmember year it ends in.
- **A card with nothing recurring to track stays out of This period and Card value**
  unless asked for on those screens. It has something to track when its product still
  has a benefit, or a period from a product it had before is still running. Manage
  always lists every card.

## Catalog

- **The source's names and ids are kept as they are.** The catalog follows a community
  dataset that adds and renames cards all the time. Renaming a card here, even to the
  issuer's official name, would break that link, so other names are aliases for the
  search and nothing else.
- **Corrections are an overlay, never edits.** `catalog/overrides.json` adds, replaces
  or hides a benefit by the source's id and records a mark of the row it was written
  against. A correction keeps winning when the source changes that row, and is flagged
  so a person compares the two; silently taking the source's new row could undo a fix,
  and silently keeping ours could hide the source catching up.
- **The service refreshes the catalog itself, weekly**, instead of waiting for a new
  build. A copy that fails to download, to parse, or that lost half its cards is
  refused and the catalog in use stays.
- **A refresh is not allowed to delete history.** Periods are rebuilt from the catalog,
  so a benefit that vanished would take its recorded use with it. A dropped product
  stays while a card has it and a dropped benefit stays, as ended, while it has use.
  Renamed ids are not followed automatically: the old product is kept and flagged, and
  a person moves the card.

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
- **The points program** is on the card product. A balance is not: it belongs to a
  person and a program, with or without a card that earns into it.

## Operations

- **No login.** The service is meant for a home network or a VPN.
- **One background worker** wakes every ten minutes and runs whatever is due: the
  catalog refresh, the daily sync, rolling periods forward, reminders, a nightly database copy. Each step
  decides for itself whether it is due, so restarts neither repeat nor skip.
- **Reminders are recorded when sent**, so a nudge goes out once per threshold; a
  failed send is forgotten and retried.
- **Logs carry counts and status**, not descriptions or amounts.

## Names

- **Chitragupta is the name everywhere a person types or reads it**: the repository, the
  solution and its projects, the Docker image and service, the `chitragupta` command, the
  `Chitragupta` settings section and the `CHITRAGUPTA_*` variables in `.env`.
- **The word "tracker" stays inside.** Class names such as `TrackerQueries` say what the
  code does, not what the product is called. The database file is still `tracker.db` and
  the nightly copies are still `tracker-<date>.db`: renaming a household's data gains
  nothing and is the one rename that could lose something.
- **The balance runner is `tools/check-balances.sh`**, named for what it does and not for
  the kind of balance. It runs whatever accounts udaan lists, miles or points. It is for
  an install without udaan's service; with the service, the tracker does the asking.
