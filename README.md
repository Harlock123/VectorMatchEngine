# Vector Match Engine

**Fuzzy record matching between Excel spreadsheets.** Find the same real-world entity across two
datasets when the names are spelled differently and there is no shared key.

Built with .NET 8 · Avalonia UI 11 · ML.NET 3 · EF Core 8 · SQL Server

---

## The problem it solves

You have two lists of the same people, companies, or products — exported from different systems,
maintained by different teams. There is no common ID, and the text does not line up:

| Dataset A | Dataset B |
|---|---|
| John Smith, Boston | Jon Smyth, Boston |
| Priya Raghunathan, Austin | Priya Ragunathan, Austin |

A join on name finds nothing. Vector Match Engine embeds each record as a character-trigram vector,
compares every row in A against every row in B by cosine similarity, and surfaces the pairs that
are close enough — ranked, reviewable side-by-side, and exportable to Excel.

## What it does

- **Import** any `.xlsx` file and choose, per column, whether it is *vectorized* (fed into the
  similarity vector), *preserved* (stored verbatim for display and export), or both.
- **Store** every row's vector and preserved values in SQL Server, so datasets are imported once
  and matched many times.
- **Match** two datasets at a similarity threshold you control, with live progress.
- **Review** matched pairs side-by-side in a grid whose columns adapt to whatever each dataset
  preserved.
- **Export** results to a formatted Excel workbook.

---

## Requirements

| | |
|---|---|
| **.NET 8 SDK** | `dotnet --version` should report 8.0 or later. Pinned via `global.json`. |
| **SQL Server** | Local (Express / Developer / LocalDB) or remote. The app creates its own database and schema. |

Runs on Windows, macOS, and Linux.

## Quick start

```bash
git clone <your-repo-url>
cd VECTORIZER

dotnet build VectorMatchEngine.sln
dotnet run --project VectorMatchEngine.UI
```

On first launch the app opens directly to **Settings**, because no database is configured yet:

1. Paste a SQL Server connection string, for example:
   ```
   Server=.;Database=VectorMatchDb;Trusted_Connection=True;TrustServerCertificate=True;
   ```
2. Click **Test Connection** to confirm the server is reachable.
3. Click **Save & Apply** — this applies the EF Core migrations, creating the database and its four
   tables if they do not already exist.

Settings are stored outside the repo, at:

| OS | Path |
|---|---|
| Windows | `%APPDATA%\VectorMatchEngine\settings.json` |
| macOS | `~/Library/Application Support/VectorMatchEngine/settings.json` |
| Linux | `~/.config/VectorMatchEngine/settings.json` |

The connection string is re-read on every database call, so changing it takes effect immediately —
no restart needed.

---

## Using it

**1. Import Dataset A** — *Home → Import Dataset*

- *Step 1* — pick an `.xlsx` file. The first worksheet is used; row 1 is treated as the header row.
- *Step 2* — name the dataset, then tick **Vectorize** and/or **Preserve** for each column. A column
  can be both. At least one column must be vectorized.
- *Step 3* — watch progress; the summary reports rows ingested and vector dimensions.

**2. Import Dataset B** — same flow. Vectorize the *same kinds of columns* you chose for Dataset A,
so the two are semantically comparable.

**3. New Match Job** — pick the two datasets, set a threshold, run.

**4. View Results** — pairs are listed highest-similarity first, with each dataset's preserved
columns prefixed `A:` and `B:`.

**5. Export Excel** — writes a `Match Results` sheet: a `Similarity` column followed by
`A_<column>` and `B_<column>` values for every pair.

Datasets and match jobs can be deleted from their list views. A dataset still referenced by a match
job cannot be deleted — delete the job first.

---

## How it works

### Vectorization

Each row's selected columns are joined with spaces and lowercased, then pushed through an ML.NET
pipeline:

```
NormalizeText                  lowercase, strip punctuation and diacritics
  → TokenizeIntoCharactersAsKeys
  → ProduceHashedNgrams        character trigrams hashed into 2^9 = 512 buckets
  → NormalizeLpNorm            L2 normalization
```

Using **character trigrams** rather than whole words is what makes matching robust to misspellings:
`smith` and `smyth` differ as tokens but share most of their surrounding trigram context, so the
vectors stay close.

Trigrams are **hashed into a fixed 512-dimensional space** rather than fitted into a learned
per-dataset vocabulary. This matters: a fitted vocabulary assigns different meanings to the same
vector index in each dataset, so vectors from A and B could not be meaningfully compared. Hashing
gives every dataset identical axes, at the cost of occasional collisions between unrelated trigrams.

Vectors are stored as `VARBINARY(MAX)` — 512 floats, 2 KB per record. Dimensionality is controlled
by `VectorizationService.NumberOfBits`; datasets imported under different settings cannot be matched
against each other, and a job that tries fails with a clear message rather than silently misbehaving.

### Matching

Because vectors are L2-normalized, cosine similarity reduces to a dot product. A match job compares
**every** record in A against every record in B (`Parallel.For` over A, SIMD dot product inner loop)
and keeps pairs at or above the threshold, sorted highest first.

Scores run 0.0 (nothing in common) to 1.0 (identical normalized text). Picking a threshold:

| Threshold | Behaviour |
|---|---|
| `1.00` | Identical normalized text only. |
| `0.90` | Very close — minor typos, punctuation, casing. |
| `0.85` *(default)* | Conservative. Catches small variants, misses heavier ones. |
| `0.60`–`0.75` | Catches real-world name variation. `John Smith` ↔ `Jon Smyth` scores ≈ **0.67**. |
| `< 0.55` | Expect false positives. |

**The 0.85 default is stricter than it looks.** Start there, then lower it until recall matches your
data. Matching is O(A × B) — two 10,000-row datasets is 100 million comparisons.

---

## Project layout

```
VectorMatchEngine.Core/     Excel I/O, ML.NET vectorization, cosine similarity
                            Pure logic — no UI, no database dependencies
VectorMatchEngine.Data/     EF Core entities, DbContext, repositories, migrations,
                            and the DataService orchestrator the UI calls
VectorMatchEngine.UI/       Avalonia MVVM desktop app — views, view models, DI, navigation
```

### Database schema

| Table | Contents |
|---|---|
| `Datasets` | One row per imported workbook: name, source file, row count, column selections, dimensions. |
| `DatasetRecords` | One row per spreadsheet row: serialized vector plus preserved values as JSON. |
| `MatchJobs` | One row per run: the two datasets, threshold, status, match count. |
| `MatchResults` | One row per matched pair: both record IDs and the similarity score. |

Deleting a dataset cascades to its records; deleting a match job cascades to its results. References
from `MatchJobs` → `Datasets` and `MatchResults` → `DatasetRecords` are **restricted** — both to
satisfy SQL Server's multiple-cascade-path rule and to stop a dataset being deleted out from under a
job that still cites it.

---

## Development

Regenerate the EF Core migration after changing an entity:

```bash
dotnet ef migrations add <Name> \
  --project VectorMatchEngine.Data \
  --startup-project VectorMatchEngine.Data
```

`DesignTimeDbContextFactory` supplies a placeholder connection string, so scaffolding works without
a running server. Override it with the `VECTORMATCH_CONNECTION` environment variable if needed.

A few implementation notes worth knowing before editing:

- **`Styles/AppStyles.axaml` must use `DynamicResource`, not `StaticResource`.** Application styles
  are constructed before `Application.Resources` is populated; a `StaticResource` reference there
  throws at startup, before any window appears.
- **`ExpandoPropertyAccessorPlugin` is required for the results grid.** Avalonia's default binding
  accessor resolves paths by CLR reflection, which finds nothing on an `ExpandoObject` — without the
  plugin every dynamic cell renders blank rather than erroring.
- **Results grid rows are keyed `A0`/`B0`, not by column name.** Both datasets commonly preserve a
  column of the same name (`FNAME`); keying by name would silently overwrite A's value with B's.
- **Repositories take `IDbContextFactory`, not `AppDbContext`.** They are registered as singletons,
  so they must not capture a scoped context — and this is what lets a connection-string change apply
  without a restart.

## Status

Working end-to-end and building clean (0 errors, 0 warnings). Verified: the vectorizer produces
comparable spaces across separately-imported datasets, all seven views render and resolve through
DI, and the full Excel → vectorize → match → export path round-trips correctly against real files.

Known limitations:

- Matching is O(A × B) with no blocking or indexing strategy, so very large dataset pairs are slow.
- Hash collisions are possible by design; raising `NumberOfBits` reduces them at the cost of storage
  and match time.
- The SQL Server persistence path has not yet been exercised against a live server in this
  repository — the schema and migration are sound, but first run against your own instance is the
  proving step.
