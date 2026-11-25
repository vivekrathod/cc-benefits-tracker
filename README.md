# Card Benefits Tracker

A self-hosted service that shows which credit card benefits (statement credits) have been used,
which are left, and which are about to expire. Usage is inferred from the transaction feed, so
manual entry is the exception.

- ASP.NET Core API with SQLite storage, in a clean-architecture split.
- Transactions arrive through an `ITransactionFeed` port: SimpleFIN Bridge or issuer CSV exports.
- The domain (cards, benefits, periods, matching) has no dependency on the feed or the database.

## Build and test

```
dotnet test
```

Requires the .NET 10 SDK.

## Data and secrets

No real statements, databases or credentials belong in this repository. The SimpleFIN access URL
is a credential: it is read from a file outside the repository and is never logged.
