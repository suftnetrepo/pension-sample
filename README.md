# IntelliPen Import Service

Upload a member CSV from an agreed source, check it against that source's agreed schema version, transform it to the
IntelliPen import format, review it, then commit or discard. **Nothing is saved until a user commits.** On commit the
batch is saved (SQLite in this build), the IntelliPen file is produced and the reconciliation report is sent.

- Front end: React 18 + TypeScript (Vite) with TanStack Query, Tailwind CSS v4 and shadcn/ui: `frontend/`
- Back end: C# .NET 10 Web API in clean architecture: `backend/`
- Specification: *IntelliPen Import Service: Technical Specification* (the doc that comes with this code)

## How a file flows

```
CSV ──(source + version chosen by the user)──► schema config/schemas/{source}/{version}.json
    ──► canonical member (our fixed internal model) ──► validation rules (written once, for every source)
    ──► IntelliPen mapping config/intellipen/*.json ──► IntelliPen file, reconciled
```

- **A new agreed source or version** = a new schema file + a sample + a test. Nothing else changes.
- **A new IntelliPen layout** = a new IntelliPen mapping file. Sources are untouched.
- **The canonical model** (`Domain/Canonical/CanonicalMember.cs`) changes only when IntelliPen needs information no source
  sends today, and then the new field is optional, so every existing schema keeps working.

> **The IntelliPen layout is a placeholder** until the IntelliPen specification arrives. Rewrite
> `config/intellipen/member-import-v0.1.json`, regenerate the files in `tests/IntellipenImport.Tests/Expected/`, and check
> them line by line against the specification.

## Run it locally

Prerequisites: .NET 10 SDK, Node 20+.

```bash
dotnet dev-certs https --trust              # once

# Terminal 1: API on https://localhost:7101 (Development signs you in as dev.user with both roles)
cd backend/src/IntellipenImport.Api
dotnet run --launch-profile https

# Terminal 2: front end on http://localhost:5173 (/api is proxied to the API)
cd frontend
npm install                                 # first time
npm run dev
```

Open http://localhost:5173, choose **Source** and **Schema version**, upload the matching sample from `backend/samples/`:

| Sample | Source / version | Expected result |
| --- | --- | --- |
| `ashwood-payroll_v1_sample.csv` | Ashwood payroll v1 (comma, dd/mm/yyyy) | 31 lines · 26 accepted · 5 rejected · 10 warnings · balanced |
| `legacy-admin_v1_sample.csv` | Legacy admin export v1 (pipe, yyyy-mm-dd, own codes) | 30 lines · 25 accepted · 5 rejected · 10 warnings · balanced |

Try uploading the Legacy file as *Ashwood payroll v1*: the file is refused and the screen says it uses pipes and matches
*Legacy admin export v1*. After a commit, the batch appears under **Recently committed** (read from SQLite).

## Exceptions: what reviewers do

Open the **Exceptions** tab. Every issue says **who acts** (Employer, Payroll or Scheme administrator) and what to send back.

- **Errors** reject the line. The tool never fixes them: the owner corrects the value at source and the file is uploaded again.
  Nothing can be edited here.
- **Warnings** load the line but must be **checked** before the batch can be committed. Tick warnings (or **Select all shown
  warnings**), add an optional note, and press **Mark as checked**. Each checked warning shows who checked it and when, with
  **Undo** until the batch is committed. **Warnings not yet checked** filters the list. While any warning is unchecked the commit bar
  says "n warnings still to check" and links straight to that filter.
- Checks are saved with the batch at commit. The committed view, the reconciliation report ("Checked by X at Y: note", or "Not checked")
  and the exceptions CSV all show them. Committed batches stay read-only.
- Uploaders and committers can check warnings; the committer still has to commit. Switch the rule off with
  `Import:RequireWarningsAcknowledged` = `false` (default `true`).

### Automatic corrections

While reading a line the tool makes a small set of safe format fixes, and lists every one under **Automatic corrections** (line,
member, column, before, after, what was done), in the summary count, in the report, and as `corrections.csv`. A fix is listed only if
the fixed value is then accepted. Fixes never change which lines are accepted or what is written to the IntelliPen file.

| Fix | Example |
| --- | --- |
| Leading or trailing spaces removed | `" David Okafor "` → `David Okafor` |
| NI number: spaces removed, letters upper-cased | `ab 12 34 56 c` → `AB123456C` (shown masked, `AB12****C`) |
| £ sign and thousands separators removed from a number | `£52,000` → `52000` |
| A code matched ignoring case | `active` → `Active` |
| A day or month written with one digit, in the schema's own order | `1/2/1967` → `01/02/1967` (for `dd/MM/yyyy`) |

Nothing else is guessed: day and month are never swapped, two-digit years are not read, blanks are not filled, and an impossible date
such as `13/25/1990` or `31/2/1990` stays an error.

## Committed batches (read-only)

**Committed batches** in the top bar lists every committed batch, newest first, searchable by batch ID, file, source or person.
**View** opens one with the same four tabs as review (summary, transformed data, exceptions, file preview) and three exports:
the reconciliation report, the IntelliPen file and the exceptions CSV. There is no commit, discard or edit: the API behind it
(`/api/v1/committed`) has GET endpoints only. The committed view also shows the automatic corrections and who checked each warning.

- Everything shown is what was saved at commit: the figures, the report as sent, and the IntelliPen file byte for byte (its
  fingerprint matches the report). Later config changes don't alter a committed batch.
- The raw source line is not kept after commit, only the IntelliPen records it became; rejected lines appear in the exceptions.
- Role `Import.Viewer` can view and export committed batches (masked) and do nothing else. Uploaders and committers can view them too.
  Try it with the header `X-Dev-Roles: Import.Viewer`.
- A database created by an earlier build is upgraded at start-up (columns added to `import_batch` and `import_issue`, and the `import_correction` table created). Batches committed before the upgrade show
  their counts but have no stored IntelliPen file.

Committed data: `backend/data/intellipen-import.db` (SQLite, created on first run). Files: `backend/output/intellipen/`
(IntelliPen file) and `backend/output/reports/` (reconciliation report).

**Testing roles in development:** headers `X-Dev-User: someone` and `X-Dev-Roles: Import.Uploader` act as another user, or as
someone who may upload but not commit. **No database for a demo?** Set `Infrastructure:Persistence` to `InMemory`.

## Tests

```bash
cd backend
dotnet test
```

108 tests (97 unit/use-case in `IntellipenImport.Tests`, 11 over real HTTP in `IntellipenImport.Api.Tests`, which uses the `Microsoft.AspNetCore.Mvc.Testing` package): CSV reading, schema matching (missing / extra / duplicated columns, wrong delimiter), every validation rule, both
**golden sample files** (exact expected IntelliPen output for each source), the proof that the same member from two
different sources gives identical IntelliPen records, config checks, the batch status rules, the read-only committed view (it shows and exports exactly what was saved),
checking warnings (domain rules, the commit gate, commit refused with a pending warning, checks surviving into the committed view and the report),
each automatic correction (and the cases that must not be corrected), the SQLite save/read/upgrade of checks and corrections, and security: masking on every endpoint for held and committed batches, reveal and download roles and their audit entries, response headers, and an upload that never touches disk.
Front end: `npm run build` type-checks and builds.

## Back end: clean architecture

```
backend/
  src/IntellipenImport.Domain/          no dependencies
    Canonical/CanonicalMember.cs        the fixed internal member model + its fields and codes
    Schemas/SourceSchema.cs             an agreed source layout (one per source + version)
    Mapping/TargetMapping.cs            the IntelliPen mapping (reads canonical fields only)
    Batches/ImportBatch.cs              batch status and the rules for commit, discard, expiry, checking warnings (Acknowledgement)
    Processing/ImportResult.cs          what processing produces
  src/IntellipenImport.Application/     depends on Domain only
    Abstractions/Ports.cs               interfaces for everything outside (store, repository, delivery, user, audit)
    Processing/                         CSV reader → CanonicalReader → MemberRules → IntellipenWriter → ImportPipeline
    Imports/ImportCommands.cs           upload, commit, discard, check and un-check warnings (BatchLocks.cs: one lock per batch, shared with commit)
    Imports/ImportQueries.cs            sources, summary, rows, exceptions, file, report (held batches)
    Imports/CommittedQueries.cs         the read-only committed view: list, summary, rows, exceptions, file, report
    Reporting/ReconciliationReport.cs   the HTML report
  src/IntellipenImport.Infrastructure/  implements the ports
    Catalogs/                           loads and checks schema and mapping files at start-up
    Storage/                            encrypted temporary store (IDistributedCache + Data Protection)
    Persistence/                        SQLite repository (ADO.NET; SQL in SqliteSql.cs), in-memory option
    Delivery/                           IntelliPen file target, report sender, audit log
    DependencyInjection.cs              registers everything
  src/IntellipenImport.Api/             HTTP only: controllers, sign-in, problem details
  config/schemas/{source}/{version}.json, config/intellipen/*.json
  samples/, tests/
```

The only NuGet package outside tests is **Microsoft.Data.Sqlite** (used in one file, `Persistence/SqliteConnectionFactory.cs`).

## Front end: services → hooks → components

```
frontend/src/
  services/       http.ts (fetch + ApiError + upload with progress + downloads), importsService.ts (every endpoint), types.ts
  hooks/          queries.ts (TanStack Query reads, cached), mutations.ts (upload with progress/cancel, commit, discard, downloads)
  components/ui/  shadcn/ui components (button, card, select, tabs, table, alert, alert-dialog, checkbox, progress, ...)
  components/     presentation only, built from components/ui: common/ (errors, spinner, progress, steps, error boundary), upload/, review/
  pages/          UploadPage, ReviewPage, ClosedPage
  app/            App.tsx (screen chosen from the batch in the URL), queryClient.ts (retry and 410 handling)
  lib/utils.ts    shadcn's cn() helper
  index.css       Tailwind + the shadcn theme (colours from the agreed mock)
```

**Styling:** Tailwind CSS v4 (via `@tailwindcss/vite`, no tailwind.config file) and shadcn/ui on Radix primitives, set up
with `components.json` (style new-york, alias `@/`). Add more components the usual way, e.g. `npx shadcn@latest add dialog`.
The project is on React 18, so `components/ui/button.tsx` uses `forwardRef` (current shadcn code assumes React 19); keep that
if the CLI overwrites it, or move the project to React 19. Theme colours are CSS variables at the top of `index.css`; to use
Isio's own theme, replace those values.

**No component calls the API directly**: only hooks import the service. Error handling:

- Every failure is an `ApiError` with a plain-English message.
- Network errors and 5xx retry twice automatically; 4xx never retry.
- A 410 (batch expired, or closed in another tab) refreshes the batch and moves to the right screen.
- Download errors show beside their button.
- An error boundary catches unexpected rendering errors.

## API additions

| Endpoint | Purpose |
| --- | --- |
| `POST /api/v1/imports/{id}/acknowledgements` | body `{ "issueIds": [0, 3], "note": "optional" }`: mark warnings as checked by the signed-in user (all or nothing; errors are refused). Returns the batch summary. Policy: Upload |
| `DELETE /api/v1/imports/{id}/acknowledgements/{issueId}` | take back one check before commit. Returns the batch summary. Policy: Upload |
| `GET /api/v1/imports/{id}/corrections`, `.../corrections.csv` | the automatic corrections |
| `GET /api/v1/committed/{id}/corrections`, `.../corrections.csv` | the same, read-only, for a committed batch |

Issues now carry an `id` (their index in the batch's issue list), an `owner`, and `checked` (who and when, or null). The batch summary
has `warningsPending`, and `canCommit` is false while it is above zero (when the rule is on).

## Configuration

| Section | Setting | Default |
| --- | --- | --- |
| `Import` | `BatchExpiryHours`, `MarkerDays`, `MaxFileBytes`, `MaxLines` | 24, 7, 10 MB, 50,000 |
| | `RequireWarningsAcknowledged` | `true`: every warning must be checked before commit |
| `Infrastructure` | `ConfigPath` | `../../config` |
| | `IntellipenMapping` | the only file in `config/intellipen` |
| | `Persistence` | `Sqlite` (or `InMemory`) |
| | `SqliteConnectionString` | `Data Source=../../data/intellipen-import.db` |
| | `OutputPath` | `../../output` |
| | `Email:Enabled`, `Host`, `Port`, `From`, `To` | off |

## Security

**Where member data lives**

- Before commit: only in the temporary store, in memory, **encrypted** (ASP.NET Data Protection) and deleted when the batch is committed,
  discarded or expires (24 hours). Nothing is written to disk: the upload is streamed into memory (`MultipartReader`; there is no `IFormFile`,
  which would spill files over 64 KB to a temporary folder; a test points ASP.NET's temp folder at a folder that does not exist and uploads a
  200 KB file to prove it).
- After commit: SQLite (members, issues, corrections, who checked what, the report, the IntelliPen file), plus the IntelliPen file and the report in
  `output/` (the file is what IntelliPen receives, so it is complete).

**What is masked.** Wherever the API returns member values (transformed rows, the source line, file preview, exceptions, corrections, the report
and the exceptions/corrections CSVs; held and committed alike) these are masked by the server, so the browser never receives the real value:

| Data | Shown as |
| --- | --- |
| NI number | `AB12****C` |
| Date of birth | `**/**/1967` (year only) |
| Salary amounts | `****` |

Names, member IDs, dates joined/left and codes are not masked. The fields are marked in the domain (`Sensitivity` on `CanonicalFields`); the
mapping to source columns and IntelliPen fields is done in `Application/Processing/Masking.cs`. Search ignores sensitive columns, so it can't be used to probe them.

**Who can see or download real values**

| Action | Needs | Logged |
| --- | --- | --- |
| Reveal one member (`GET .../rows/{line}/reveal`, held or committed) | `Import.RevealSensitive` or `Import.Committer` | batch, line, user (never the values) |
| Download the full IntelliPen file (`GET .../file/download`, held or committed) | the `DownloadFile` policy in `Program.cs` (today `Import.Committer`); it is also what `/api/v1/me` reports as `downloadFile` | batch, file name, user |
| Everything else (rows, preview, exceptions, reports, CSV exports) | their usual role; masked values only | - |

The UI's **Reveal** button shows the real values for 60 seconds, or until another member is chosen.

**Responses.** Every response has `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` and `X-Frame-Options: DENY`; everything under
`/api` (data and downloads) also has `Cache-Control: no-store` and `Pragma: no-cache`. HSTS is added outside Development.

**Known limits.** A batch committed before masking existed has no masked file preview (it says so; a committer can still download the file), and
its member search finds only the member ID. Its saved records are masked by the current mapping, or outright if a field isn't recognised.

**Production to-do**

1. **Data Protection keys** in Azure Key Vault (and persisted, so held batches survive a restart and every server can read them).
2. **Database encryption at rest** (TDE on SQL Server, or an encrypted volume for SQLite).
3. **Folder permissions** on `output/intellipen/` and `output/reports/`: the service account writes, only the people and systems that need the files read.
4. **Content-Security-Policy** (and TLS settings) on the site hosting the front end; the API cannot set it for the static files.
5. **Retention periods** for committed data, the IntelliPen files and reports, and audit logs: to be agreed with Isio.
6. Sign-in with Entra ID and the app roles, including the new `Import.RevealSensitive` (see below).

## Before production

1. **IntelliPen mapping**: replace the placeholder (above).
2. **Sign-in**: `Program.cs` refuses to start outside Development until Entra ID is added
   (`Microsoft.AspNetCore.Authentication.JwtBearer`, app roles `Import.Uploader` / `Import.Committer` / `Import.Viewer` / `Import.RevealSensitive`). In the front end call
   `setTokenProvider` (services/http.ts) with an MSAL token function.
3. **Database**: SQLite suits one server. For the team's SQL Server, implement `IDbConnectionFactory` with
   `SqlConnection` and adjust the SQL in `SqliteSql.cs` (parameters and `LIMIT` → `TOP`).
4. **More than one server**: swap `AddDistributedMemoryCache()` for Redis or SQL Server cache and persist Data Protection keys.
5. **Delivery**: replace `FolderFileTarget` with IntelliPen's import folder or API once confirmed.
