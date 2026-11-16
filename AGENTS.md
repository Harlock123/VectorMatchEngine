# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers what the app does, how matching works,
the database schema and the reasons behind the package versions; this file covers how the code fits together and
the rules that keep it working.

## What this is

A desktop app that finds the same real-world records across two Excel spreadsheets with no shared key. Rows are
turned into hashed character-trigram vectors with ML.NET, stored in SQL Server through EF Core, and compared by
cosine similarity. C# on .NET 10 with Avalonia 11 (MVVM with CommunityToolkit.Mvvm and
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
  - `Services/VectorSheetExportService.cs` writes a dataset's vectors to an `.xlsx` instead of the database
    (Import, Step 2, "Another spreadsheet"). It writes the zip and sheet XML itself, streaming, because a
    500-plus-column sheet built in memory by ClosedXML or the Open XML SDK costs 25 to 75 KB a row.
  - `Services/VectorSheetImportService.cs` reads such a workbook back (the import wizard recognises one in
    Step 1), streaming too, and copes with Excel's re-saved form (shared strings, cell references). It
    refuses vectors whose size or the About sheet's `Method` (`VectorSheetExportService.MethodDescription`)
    differ from this version's; keep that string in step with the vectorizer.
  - `Services/VectorizationService.cs`: the ML.NET trigram pipeline; `NumberOfBits` sets the vector size. With
    `VectorizeOptions` (a matching key, sounds-alike columns) it runs the keyed encoding instead.
  - `Services/VectorKey.cs` (passphrase → key and fingerprint) and `Services/Metaphone.cs` (sounds-alike codes);
    `Models/VectorEncoding.cs` describes an encoding and when two can be matched.
  - `Services/SimilarityService.cs`: the all-pairs dot-product comparison.
  - `Models/`: plain request and result types.
- `VectorMatchEngine.Data/`: EF Core.
  - `AppDbContext.cs`, `Entities/`, `Migrations/`, and `Repositories/` (datasets and match jobs).
  - `SqlVectorStore.cs`: matching inside SQL Server 2025 / Azure SQL with `VECTOR` and `VECTOR_DISTANCE`. It
    detects support with a probe query and keeps its own table, `DatasetRecordVectors`, outside the EF model.
  - `DataService.cs`: the orchestrator the UI calls (migrate, import, match, export, delete).
    `IngestExcelAsync` (to the database) and `VectorizeToSpreadsheetAsync` (to a file, no database) share
    `ReadAndVectorizeAsync`; `IngestExcelAsync` and `LoadVectorWorkbookAsync` share `SaveDatasetAsync`.
- `VectorMatchEngine.UI/`: the Avalonia app.
  - `App.axaml.cs` registers every service and view model in the DI container and applies migrations.
  - `AppSettings.cs` and `AppSettingsDbContextFactory.cs`: the settings file and a context factory that rereads
    the connection string on every call.
  - `Navigation/`, `Services/` (dialogs, `ExpandoPropertyAccessorPlugin`), `ViewModels/`, `Views/`, `Styles/`.

- `SAMPLES/`: `ClaimsSample2025.xlsx`, contrived claims data to test with, and `GenerateClaimsSample.cs`,
  the .NET file-based script that makes it (fixed seed). Keep its identifiers unmistakably fake (9xx SSNs,
  555-01xx phones). It is a solution folder, not a project, so the build ignores it.

## Rules that keep it working

- **Settings live outside the repo**, in `SpecialFolder.ApplicationData` under `VectorMatchEngine/settings.json`.
  Never commit a real connection string.
- **Vectors must share one space.** Trigrams are hashed into a fixed space so datasets imported separately can be
  compared. Changing `NumberOfBits` makes old datasets unmatchable with new ones.
- **Encodings must match.** A dataset records how its vectors were made (`VectorEncoding`: `trigram`, the original
  ML.NET pipeline, or `keyed-trigram`, HMAC-SHA256 under a `VectorKey` plus Metaphone codes for sounds-alike
  columns), and `DataService.RunMatchJobAsync` refuses to compare different ones. Anything that changes the output
  of either encoding (normalization, the trigram markers, the HMAC feature prefixes `t|`/`p|`, the Metaphone rules,
  the code weighting, the PBKDF2 salt or rounds) makes existing keyed datasets and vectors workbooks unmatchable,
  so it needs a new encoding name rather than a silent change. Keep `VectorSheetExportService.MethodDescription`
  in step.
- **Never store the matching key.** Only `VectorKey.Fingerprint` goes into the database or a workbook.
- **Repositories take `IDbContextFactory`, not `AppDbContext`.** They are singletons, so they create a context per
  call; this is also what lets a new connection string apply without a restart.
- **Schema changes need a migration** in `VectorMatchEngine.Data/Migrations/`; the app applies migrations at
  start-up and when settings are saved. The one exception is `DatasetRecordVectors`, which
  `SqlVectorStore` creates at run time because a `VECTOR` column can't be created on older servers. It is a copy
  of `DatasetRecords.VectorData` sized `VECTOR(VectorDimensions)`: changing `NumberOfBits` means dropping it.
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
