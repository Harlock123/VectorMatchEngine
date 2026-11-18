# Vector Match Engine

**Fuzzy record matching between Excel spreadsheets.** Find the same real-world entity across two
datasets when the names are spelled differently and there is no shared key.

Built with .NET 10 · Avalonia UI 11 · ML.NET 3 · EF Core 10 · SQL Server

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
  and matched many times — or **write the vectors to another spreadsheet** instead, with no database
  involved, and **load that workbook into the database** later without vectorizing again.
- **Clean up** values by field type before vectorizing (people's and company names, addresses, dates,
  phones, ZIP codes, ID numbers), so Bob and Robert, or 3/4/1980 and 1980-03-04, encode the same.
- **Match** two datasets at a similarity threshold you control, with live progress: on the whole row, or
  **field by field** with weights and must-match fields; every pair, or **one match per record**; every
  record against every other, or only the candidates **blocking** picks (the same value in a key field,
  or the same LSH bucket).
- **Review** matched pairs side-by-side in a grid whose columns adapt to whatever each dataset
  preserved, with each pair's **field scores** and its **runner-up and gap**, so an ambiguous match
  stands out.
- **Export** results to a formatted Excel workbook.

---

## Requirements

| | |
|---|---|
| **.NET 10 SDK** | `dotnet --version` should report 8.0 or later. Pinned via `global.json`. |
| **SQL Server** | Local (Express / Developer / LocalDB) or remote. The app creates its own database and schema. |

Runs on Windows, macOS, and Linux.

## Quick start

```bash
git clone <your-repo-url>
cd VECTORIZER

dotnet build VectorMatchEngine.sln
dotnet run --project VectorMatchEngine.UI
```

The connection string starts out as a default that points at a local SQL Server (`Server=.`,
database `VectorMatchDb`, Windows authentication), and on launch the app applies the EF Core
migrations against it. If that server is reachable, the database and its four tables are created and
the app opens on **Home**. If not, it opens **Settings** with a "Could not reach the database"
message. To point it at a different server:

1. Open **Settings** and paste a SQL Server connection string, for example:
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
  A vectors workbook (see below) is recognised instead, and Step 2 then only asks for a name.
- *Step 2* — name the dataset, then tick **Vectorize** and/or **Preserve** for each column. Ticking
  Vectorize unticks Preserve (a column can still be both, and the page warns which are). Tick **Sounds
  alike** on name columns to encode them by how they sound, and pick a **Field type** for columns whose
  values are written in more than one way (see *Field types* below). Enter a **Matching key** (see
  *Privacy* below). At least one column must be vectorized. Under **Write the vectors to**, keep *The database*
  (needed for matching) or choose *Another spreadsheet* and pick the `.xlsx` file to write.
- *Step 3* — watch progress; the summary reports rows ingested and vector dimensions.

**Vectors to a spreadsheet.** Choosing *Another spreadsheet* in Step 2 vectorizes the rows exactly as an
import would, but writes them to a new workbook and saves nothing to the database (so no SQL Server is
needed, and those rows can't be used in a match job):

- `Vectors` sheet — one row per source row: `Row` (1-based), the preserved columns, then `V0` … `V511`,
  one number per dimension. Zeros are written as `0`, so every row is a complete vector. The header row
  and the readable columns are frozen. A `Vectorized Text` column (the readable text each vector was made
  from) goes before `V0` only if you tick *Also write the vectorized text*.
- `Field Vectors` column — after `V511`: each row's field vectors (one small vector per vectorized
  column, base64), so a dataset loaded from the workbook can be scored, explained and blocked field by
  field. It isn't meant to be read or edited; a row whose cell is missing or edited makes the loaded
  dataset whole-row only.
- `About` sheet — the dataset name, source file, time, columns, dimensions, method, encoding, the matching
  key's fingerprint, the sounds-alike columns, and the field types and clean-up version.

Vectors have unit length, so the cosine similarity of two rows is the sum of their products — in Excel,
`=SUMPRODUCT(Vectors!E2:SV2, Vectors!E3:SV3)` when two columns are preserved (the `About` sheet names the
column `V0` starts at). The workbook is streamed to disk as it is written, so memory stays flat: 60,000
rows take a few seconds. A worksheet holds at most 1,048,575 rows and 16,384 columns, so the source can
have at most that many rows, and the preserved columns plus 514 must fit.

**Loading a vectors workbook.** Import a vectors workbook like any other file: Step 1 recognises it by its
`Vectors` sheet (columns `V0`, `V1` …), Step 2 shows what it holds and takes
the name its `About` sheet records, and Process stores its rows and vectors as a dataset, ready for match
jobs. Nothing is vectorized again, so matching against it gives the same scores as importing the source.

- The columns between `Row` and the vectors (or `Vectorized Text`, if it's there) become the dataset's
  preserved columns. The dataset keeps the workbook's encoding and key fingerprint, so it matches only
  datasets made with the same key; no key is needed to load it.
- It reads the workbook as Excel or another program saves it again too, and rows may be sorted or deleted
  (hidden rows still load). Vectors that were edited are rescaled to length 1, since matching relies on that.
- It refuses vectors of another size, or made another way (the `About` sheet's `Method`), since they
  couldn't be compared with this version's; import the source workbook again instead. A cell in `V0` …
  that isn't a number stops the load with its address, and nothing is saved.
- Like the writer it streams the file, so 60,000 rows load in a few seconds without holding the sheet in
  memory.

### Privacy: matching keys and sounds-alike names

The vectors are fingerprints for fuzzy matching, not encryption. On their own they don't hide what they
were made from: the original encoding hashes trigrams with a fixed, public function, so anyone with this
app (or ML.NET) can vectorize guesses and see which one matches. Knowing a name, trying every date of birth
from 1920 to 2025 finds the right one exactly, in seconds. Names, dates, ZIP codes and phone numbers all
come from small enough sets for that.

- **Matching key.** With a passphrase in *Matching key*, every trigram is hashed with HMAC-SHA256 under a
  key derived from it (PBKDF2, 600,000 rounds), so nobody without the passphrase can rebuild the vectors
  or test guesses against them; the same attack scores 0.2 on the wrong date. Fuzzy matching works as
  before (John Smith ~ Jon Smyth: 0.73 keyed, 0.70 without). Both datasets must be vectorized with the
  same passphrase. Only its fingerprint (e.g. `24DC-B380-8FBC`) is stored, with the dataset or in a vectors
  workbook's `About` sheet, so the app can tell when two datasets used different keys; the passphrase
  itself is never saved, so keep it safe.
- **Sounds alike.** A column ticked *Sounds alike* contributes each word's Metaphone code (Smith and Smyth
  are both `SM0`; Jon and John `JN`; Catherine and Kathryn `K0RN`) instead of its spelling, so spellings
  that sound the same encode identically. Codes are weighted to carry about as much of the vector as the
  spelled word would.
- **Plaintext.** Vectorizing doesn't hide a column that is also preserved, so ticking Vectorize unticks
  Preserve and the page lists any column still ticked for both. A vectors workbook leaves out the readable
  `Vectorized Text` column unless asked.
- **Field vectors.** Each record also keeps one small vector per vectorized column, made the same way
  (keyed when there's a key). A single field is easier to guess than a whole row, so without a key a
  dataset's field vectors give away its dates of birth or phone numbers even faster than its row vectors:
  for identifying data, use a key.
- **Compatibility.** Without a key, sounds-alike column or field type, the original encoding is used, so datasets
  imported before this still match new ones made the same way. A match job refuses two datasets encoded
  differently (different keys, keyed against plain, or different numbers of sounds-alike columns) and
  says why. The Datasets list shows each one's encoding.

With a key, the vectors are pseudonymised rather than anonymous: someone holding the passphrase can still
run the guessing attack, and a large set of vectors can leak patterns (common names repeat). Treat the
passphrase like a password, share it only with whoever vectorizes the other dataset, and choose a long one.

### Field types

A column's **Field type** cleans its values up before they're vectorized, so the same thing written two
ways gives the same trigrams. *Text (as is)*, the default, changes nothing.

| Field type | What it does | Example |
|---|---|---|
| Person's name | Drops titles and suffixes, turns *Last, First* round, maps common nicknames to the formal name (names short for more than one, such as Chris or Alex, are left alone) | `Smith, Bob J. Jr` → `robert j smith` |
| Company name | `&` to *and*, drops a leading *The* and legal forms at the end (Inc, Corp, LLC, Ltd, GmbH …) | `The Acme Corp., Inc.` → `acme` |
| Street address | USPS abbreviations for street types, directions, units and ordinals; P.O. Box in any form | `123 North Main Street, Apartment 4` → `123 n main st apt 4` |
| Date (month first) / (day first) | Any common format, or an Excel serial number, to ISO; the variant settles 03/04 | `March 4, 1980` → `1980-03-04` |
| Phone number | Digits only, without an extension or a leading US `+1` | `+1 (555) 010-1234 x12` → `5550101234` |
| ZIP or postal code | A ZIP+4 cut to five digits, a ZIP that lost its leading zero given it back | `02134-1234` and `2134` → `02134` |
| Code or ID number | Letters and digits only | `900-12-3456` → `900123456` |

Give the other dataset's matching column the same type. The rules are versioned (`clean-1`) and recorded
with the dataset and in a vectors workbook, and a match job refuses two datasets cleaned by different
versions or with different field types. A field type uses the keyed encoding (with no key, an empty one),
so a dataset with field types can't be matched against one imported without them. On the 500-member
claims sample matched against a messy copy of 400 of them (nicknames, typos, other date and phone
formats), cleaning raised whole-row recall at 0.80 from 87% to 99.5%, with no false matches.

**2. Import Dataset B** — same flow. Vectorize the *same kinds of columns* you chose for Dataset A,
so the two are semantically comparable.

**3. New Match Job** — pick the two datasets, set a threshold, and choose how to compare:

- **Fields.** When both datasets were imported with this version (they have field vectors), each of A's
  vectorized columns is listed with the B column it's paired with: by name where the names agree,
  otherwise by position. Change or clear a pairing as needed. Paired fields always explain the score (see
  *View Results*); they can also:
  - **Weight** — the field's share of a field-by-field score.
  - **Must match** — a pair only counts if this field's own score reaches the threshold too; a blank on
    either side rules the pair out.
  - **Block** — only compare records with exactly the same value in this field, after clean-up (encoded
    by sound, the same codes). With several block fields, records that agree on any one of them are
    compared, so a typo in one is caught by another. Blanks never block together.
- **Score.** *Whole row* is the cosine of the rows' vectors, as before. *Field by field* is the weighted
  average of the paired fields' scores, leaving out fields blank on either side, so a long address can't
  outweigh a wrong name, and a wrong date of birth isn't diluted by everything else.
- **One match per record.** Keeps each record's best match only: pairs are taken highest score first, and
  one is dropped if either record is already paired. Without it, one record can match several.
- **Approximate blocking (LSH).** Hashes the whole-row vectors with random hyperplanes and only compares
  pairs that share a bucket in one of the hash tables; the tables are sized so that pairs scoring 0.1 below
  the threshold are caught about 99% of the time. It needs no field vectors, so it works on older datasets
  too, but it cuts less than field blocking does: rows of the same kind share a lot of trigrams.

On the claims sample above, blocking on date of birth, last name (by sound) or phone compared 1,697 of
the 200,000 pairs and found the same matches; LSH compared about a third of them. Field-by-field
scoring, must-match fields, one match per record and blocking need the app's own matcher, so a job using
any of them runs in the app even on SQL Server 2025. A plain whole-row job that SQL Server compares has no
runner-up or field score columns; untick *Compare inside SQL Server* to get them.

**4. View Results** — pairs are listed highest-similarity first, with each dataset's preserved
columns prefixed `A:` and `B:`. Two columns explain each score:

- **Runner-up** is the best score either record had with any other record it was compared with (above
  the threshold or not), and **Gap** is the pair's lead over it. A large gap is a clear match; a small or
  negative one means one of the records nearly matches, or better matches, someone else. On the claims
  sample, right pairs averaged a gap of +0.56 and wrong ones −0.37.
- **≈ field** columns give each paired field's own score, blank where the field is blank on either side,
  so you can see *why* two records scored as they did.

The header also shows how the job compared records and, with blocking, how many pairs it scored.

**5. Export Excel** — writes a `Match Results` sheet: a `Similarity` column, then `Runner-up`, `Gap` and
`Score: <field>` columns when the job has them, followed by `A_<column>` and `B_<column>` values for
every pair.

Datasets and match jobs can be deleted from their list views. A dataset still referenced by a match
job cannot be deleted — delete the job first.

---

## Sample data

`SAMPLES/ClaimsSample2025.xlsx` is a contrived medical claims extract to try the app on: 10,000 claim lines
in 4,743 claims for 500 members, with service dates from January to December 2025. Each line carries the
member's identifying columns (first, last and middle initial, gender, DOB, street, city, state, ZIP, SSN,
insurance ID, phone) and the claim's (claim ID and line, type and status, service start and end dates,
HCPCS/CPT code, primary and secondary ICD-10 diagnoses, units, unit price, total cost, allowed, deductible,
coinsurance, copay, plan paid, place of service, rendering and billing provider, received and processed
dates). Members have anywhere from 1 to over 100 lines.

**Every person and identifier in it is invented.** SSNs start 900-999, which are never issued; phone
numbers are in the 555-0100 to 555-0199 range reserved for fiction; insurance, claim and provider IDs are
random with made-up prefixes. The codes are real code values, so the lines look like real claims. Its
`About` sheet says the same, and how the amounts add up.

To try it, import the sheet and vectorize the member columns (name, DOB, street, ZIP, for instance): every
line for the same member gets the same vector, so matching the sheet against itself, or against a second
extract of the same members, finds them.

`SAMPLES/ClaimsSample2025 vectors.xlsx` is that sheet already vectorized the plain way (Import, *Another
spreadsheet*, no matching key, no *Sounds alike*, with *Also write the vectorized text* ticked): all 10,000
lines, the twelve member columns (first, last and middle initial, gender, DOB, street, city, state, ZIP, SSN,
insurance ID, phone) in the vectors and, readably, in the `Vectorized Text` column, beside the 33 claim
columns. It shows what the vectors and text look like without the privacy options; a real extract should use
a matching key and leave the text column off (see *Privacy* above). Importing it loads those vectors into the
database as they are; to match another dataset against it, vectorize that one the same plain way.

`SAMPLES/GenerateClaimsSample.cs` makes the claims workbook
(`dotnet run SAMPLES/GenerateClaimsSample.cs` from the repository root); its seed is fixed, so it makes
the same rows every time.

## How it works

### Vectorization

The original encoding (no matching key, no sounds-alike column): each row's selected columns are joined
with spaces and lowercased, then pushed through an ML.NET pipeline:

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

With a matching key or a sounds-alike column, `VectorizationService` does the same steps itself instead
(same normalization, marker-wrapped character trigrams, 512 buckets, L2), except that each trigram's
bucket comes from HMAC-SHA256 under the key, and each sounds-alike word adds the trigrams of its Metaphone
code. That is the `keyed-trigram` encoding; see *Privacy* above.

Vectors are stored as `VARBINARY(MAX)` — 512 floats, 2 KB per record. Dimensionality is controlled
by `VectorizationService.NumberOfBits`; datasets imported under different settings cannot be matched
against each other, and a job that tries fails with a clear message rather than silently misbehaving.

### Matching

Because vectors are L2-normalized, cosine similarity reduces to a dot product. Without blocking a match
job compares **every** record in A against every record in B (`Parallel.For` over A, SIMD dot product
inner loop); with it, only the candidates the block fields and LSH tables give each A record. It keeps
pairs at or above the threshold, sorted highest first, noting each record's two best partners along the
way so every pair gets its runner-up. Field scores are dot products of the records' sparse field vectors.

Scores run 0.0 (nothing in common) to 1.0 (identical normalized text). Picking a threshold:

| Threshold | Behaviour |
|---|---|
| `1.00` | Identical normalized text only. |
| `0.90` | Very close — minor typos, punctuation, casing. |
| `0.85` *(default)* | Conservative. Catches small variants, misses heavier ones. |
| `0.60`–`0.75` | Catches real-world name variation. `John Smith` ↔ `Jon Smyth` scores ≈ **0.67**. |
| `< 0.55` | Expect false positives. |

**The 0.85 default is stricter than it looks.** Start there, then lower it until recall matches your
data. Without blocking matching is O(A × B) — two 10,000-row datasets is 100 million comparisons.
Field-by-field scores run differently from whole-row ones (a typo costs its field more), so set the
threshold for the scoring you use.

#### Matching inside SQL Server 2025

On a server with the native `VECTOR` type (SQL Server 2025, Azure SQL Database), the server can do the
comparing. *New Match Job* checks the server and, when it qualifies, offers **Compare inside SQL Server
when it has the VECTOR type** (on by default). The job then:

1. creates `dbo.DatasetRecordVectors` the first time (one `VECTOR(512)` per record), and copies in the
   vectors of any dataset not already there, from `DatasetRecords.VectorData`;
2. scores the pairs with `VECTOR_DISTANCE('dot', …)` in chunks of 500 records of A, and inserts those at
   or above the threshold straight into `MatchResults`.

No vectors come back to the app, and the scores equal the app's own (the dot product of normalized
vectors). On any other server, or with the box unticked, the app compares the vectors as above. It is
still every A against every B; `VECTOR_DISTANCE` does exact comparisons, not an approximate index.

---

## Project layout

```
VectorMatchEngine.Core/     Excel I/O, ML.NET vectorization, cosine similarity, vectors to/from .xlsx
                            Pure logic — no UI, no database dependencies
VectorMatchEngine.Data/     EF Core entities, DbContext, repositories, migrations,
                            and the DataService orchestrator the UI calls
VectorMatchEngine.UI/       Avalonia MVVM desktop app — views, view models, DI, navigation
```

### Database schema

| Table | Contents |
|---|---|
| `Datasets` | One row per imported workbook: name, source file, row count, column selections, dimensions, how its vectors were made (`Encoding`, `KeyFingerprint`, `SoundsAlikeColumnsJson`, `Cleanup`, `FieldKindsJson`), and whether its records have field vectors (`HasFieldVectors`). |
| `DatasetRecords` | One row per spreadsheet row: serialized vector, field vectors (`FieldVectorData`: per field a count, then index and value pairs; null for older records) and preserved values as JSON. |
| `MatchJobs` | One row per run: the two datasets, threshold, status, match count, its settings as JSON (`SettingsJson`: paired fields, scoring, one per record, blocking) and the pairs it scored (`PairsCompared`). |
| `MatchResults` | One row per matched pair: both record IDs, the score, the runner-up score and the field scores (`\|`-separated, in the job's field order). |
| `DatasetRecordVectors` | SQL Server 2025 / Azure SQL only: each record's vector as `VECTOR(512)`, for matching inside the server. Created and filled at run time, not by a migration, because older servers can't create the column. Its rows go when their record does. |

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

### Dependency management

**Restores are deterministic.** `RestorePackagesWithLockFile` is on, so each project has a
committed `packages.lock.json` recording the exact resolved graph, transitive packages included.
Locally the lock file updates when you change a version; on CI, `ContinuousIntegrationBuild=true`
turns on `RestoreLockedMode`, so a graph that no longer matches the lock file fails the restore
(`NU1004`) instead of silently drifting.

```bash
dotnet restore                                    # normal: updates the lock file if ranges changed
dotnet restore -p:ContinuousIntegrationBuild=true # CI: fails if the graph drifted
dotnet restore --force-evaluate                   # deliberately re-resolve and rewrite the lock file
```

**Transitive packages are audited at build time.** NuGet audits only *direct* packages by default;
`NuGetAuditMode=all` extends that to the whole graph, so a vulnerable package at any depth appears
as an `NU1903` build warning. On demand:

```bash
dotnet list package --vulnerable --include-transitive
```

**Two version choices are deliberate — please read before changing them.**

- **ClosedXML is held at `0.105.*`.** The 0.102.x line pins `System.IO.Packaging 6.0.0`, which
  carries two high-severity DoS advisories (CVE-2024-43483, CVE-2024-43484). ClosedXML 0.104+
  dropped that dependency and reaches the patched `8.0.1` via `DocumentFormat.OpenXml 3.x`.
  Do not downgrade below 0.104.

- **Avalonia core is pinned to `11.3.20`, but `Avalonia.Controls.DataGrid` to `11.3.13`.** This
  mismatch is intentional: 11.3.13 is the DataGrid package's newest release — it ships on its own
  cadence and declares `Avalonia >= 11.3.13`, so the pairing is supported. Do *not* "tidy" this by
  dropping the core packages to 11.3.13, because `Avalonia.FreeDesktop 11.3.13` pulls
  `Tmds.DBus.Protocol 0.21.2`, which has a high-severity advisory (CVE-2026-39959 — D-Bus signal
  spoofing and file-descriptor exhaustion on Linux). 11.3.20 pulls the patched 0.21.3.

Every package is pinned or range-bounded rather than floated on a bare wildcard, so restores do not
quietly change what gets built.

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

- Without blocking, matching is O(A × B), so very large dataset pairs are slow; LSH alone cuts the pairs
  compared by about a third on rows of the same kind, block fields by far more.
- One match per record is greedy (best score first), not an optimal assignment.
- Hash collisions are possible by design; raising `NumberOfBits` reduces them at the cost of storage
  and match time.
- The SQL Server persistence path has been exercised against Azure SQL Edge (SQL Server engine, no
  `VECTOR` type): migrating an older database, importing, loading a vectors workbook, matching with every
  option and exporting.
- Matching inside SQL Server 2025 (`SqlVectorStore`) builds clean but has not yet been run against a
  SQL Server 2025 or Azure SQL instance.
