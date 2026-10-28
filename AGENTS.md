# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers what the app does, how matching works,
the database schema and the reasons behind the package versions; this file covers how the code fits together and
the rules that keep it working.

## What this is

A desktop app that finds the same real-world records across two Excel spreadsheets with no shared key. Rows are
turned into hashed character-trigram vectors with ML.NET, stored in SQL Server through EF Core, and compared by
cosine similarity. C# on .NET 8 with Avalonia 11 (MVVM with CommunityToolkit.Mvvm and
Microsoft.Extensions.DependencyInjection). It runs on Windows, macOS and Linux, but needs a SQL Server to talk to.

## Commands

```bash
dotnet build VectorMatchEngine.sln             # the SDK version is in global.json (8.0.407, latestFeature)
dotnet run --project VectorMatchEngine.UI      # set the SQL Server connection string under Settings
dotnet ef migrations add <Name> --project VectorMatchEngine.Data --startup-project VectorMatchEngine.Data
dotnet restore -p:ContinuousIntegrationBuild=true    # fails if the packages drifted from the lock files
```

- There are no tests yet.
- `VECTORMATCH_CONNECTION` overrides the placeholder connection string that `DesignTimeDbContextFactory` gives
  `dotnet ef`. The app itself reads its connection string from its settings file.

## Layout

- `VectorMatchEngine.Core/`: pure logic, no UI or database.
  - `Services/ExcelService.cs` reads `.xlsx` files (ClosedXML); `ExcelExportService.cs` writes the results.
  - `Services/VectorizationService.cs`: the ML.NET trigram pipeline; `NumberOfBits` sets the vector size.
  - `Services/SimilarityService.cs`: the all-pairs dot-product comparison.
  - `Models/`: plain request and result types.
- `VectorMatchEngine.Data/`: EF Core.
  - `AppDbContext.cs`, `Entities/`, `Migrations/`, and `Repositories/` (datasets and match jobs).
  - `DataService.cs`: the orchestrator the UI calls (migrate, import, match, export, delete).
- `VectorMatchEngine.UI/`: the Avalonia app.
  - `App.axaml.cs` registers every service and view model in the DI container and applies migrations.
  - `AppSettings.cs` and `AppSettingsDbContextFactory.cs`: the settings file and a context factory that rereads
    the connection string on every call.
  - `Navigation/`, `Services/` (dialogs, `ExpandoPropertyAccessorPlugin`), `ViewModels/`, `Views/`, `Styles/`.

## Rules that keep it working

- **Settings live outside the repo**, in `SpecialFolder.ApplicationData` under `VectorMatchEngine/settings.json`.
  Never commit a real connection string.
- **Vectors must share one space.** Trigrams are hashed into a fixed space so datasets imported separately can be
  compared. Changing `NumberOfBits` makes old datasets unmatchable with new ones.
- **Repositories take `IDbContextFactory`, not `AppDbContext`.** They are singletons, so they create a context per
  call; this is also what lets a new connection string apply without a restart.
- **Schema changes need a migration** in `VectorMatchEngine.Data/Migrations/`; the app applies migrations at
  start-up and when settings are saved.
- **Package versions are deliberate.** Keep ClosedXML at 0.104 or later, Avalonia core at 11.3.20 and
  `Avalonia.Controls.DataGrid` at 11.3.13; the README explains the security advisories behind each.
  - `RestorePackagesWithLockFile` is on: commit the updated `packages.lock.json` files with any package change.
  - `NuGetAuditMode=all` reports vulnerable transitive packages as build warnings.
- **UI details that fail quietly:**
  - `Styles/AppStyles.axaml` must use `DynamicResource`, not `StaticResource`, or the app throws at start-up.
  - The results grid binds `ExpandoObject` rows through `ExpandoPropertyAccessorPlugin`, keyed `A0`/`B0`,
    not by column name.

## Making a change

- Build with no warnings, and run the full import, match and export path against a SQL Server by hand, since
  there are no automated tests.
- Update `README.md` when behaviour, the schema or a pinned version changes.

There is no CI workflow in this repository.
