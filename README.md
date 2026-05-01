# MS SQL to MariaDB 2.1.1 (Dj-MsSql2Maria)

> **Convert Microsoft SQL Server `.SQL` scripts and `.BAK` backup files — or plain `.CSV` data files — into MariaDB-compatible SQL — offline, instantly, with no SQL Server instance required.**

[![Version](https://img.shields.io/badge/version-2.1.1-blue)](https://github.com/DonaldJamesCompany/Dj-MsSql2Maria/releases)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4?logo=windows)](https://github.com/DonaldJamesCompany/Dj-MsSql2Maria/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

---

## What Is It?

**Dj-MsSql2Maria** is a portable, standalone Windows desktop application (.NET 9 WPF).
Drop one or more SQL Server `.sql` files, a `.bak` backup file, or one or more `.csv` data files into Dj-MsSql2Maria,
point it at an output folder, enter your **DB Name**, click **GO**, and receive MariaDB-ready `.sql` output.

Scripts that create tables begin with `CREATE DATABASE IF NOT EXISTS` and `USE` statements for the database name you provide. Data-only scripts begin with `USE` only. This means every output file targets the correct database and can be imported directly into a fresh MariaDB instance without manual editing.

When converting a BAK or CSV file you can independently choose whether to generate scripts for
Tables and/or Data, and whether each should be written as a **single consolidated file** or as
**individual per-source `.sql` files** (one script per table creation / one per source file's data).

No installer. No SQL Server. No internet connection. One `.exe`.

---

## Screenshots

> *(Add screenshots here once the UI is finalised.)*

---

## Quick Start

1. Download `Dj-MsSql2Maria.exe` from [Releases](https://github.com/DonaldJamesCompany/Dj-MsSql2Maria/releases).
2. Double-click to run — no installation needed.
3. In Dj-MsSql2Maria, choose your input type, browse for your file(s), pick an output folder, enter your **DB Name**, and click **GO**.

To build from source:

```powershell
git clone https://github.com/DonaldJamesCompany/Dj-MsSql2Maria.git
cd Dj-MsSql2Maria
dotnet publish -c Release
```

Output: `bin\Release\net9.0-windows\win-x64\publish\Dj-MsSql2Maria.exe`

---

## Features

| Feature | Detail |
|---|---|
| **Input SQL File(s)** | Select one or more MS SQL Server `.sql` files; all are converted and merged into one MariaDB output |
| **Import .CSV File(s)** | Select one or more `.csv` files; column types are inferred automatically; shares the same Tables/Data and consolidation options as BAK mode. Full RFC 4180 quoted-field parsing — commas inside quoted fields (e.g. `"13,10"`) are preserved correctly as string values. Embedded `""` escaped quotes inside fields are also handled. |
| **BAK file** | Best-effort SQL text extraction (no SQL Server needed) |
| **Tables / Data toggle** | Available for both BAK and CSV: choose Tables only, Data only, or both |
| **Consolidate CREATE TABLE** | When checked: all CREATE TABLE scripts go into one consolidated `.sql` file. When unchecked (default): one `.sql` file per source. |
| **Consolidate INSERT DATA** | When checked: all INSERT DATA scripts go into one consolidated `.sql` file. When unchecked (default): one `.sql` file per source. |
| **Filename suffix** | Optionally append a suffix (default `_MariaDb`) to the output filename |
| **Real-time log** | Scrollable black-background panel (yellow text) showing each file processed |
| **Status panel** | Scrollable black-background panel (green text) showing current operation state |
| **Cancellable** | STOP button halts processing immediately |
| **Portable EXE** | Single self-contained file, no runtime install required |

---

## Input / Output Modes

| Input Mode | Example input | Example output |
|---|---|---|
| Input SQL File(s) | `Customers.sql` (single) or `Customers.sql` + `Orders.sql` (multi) | Single: `Customers_MariaDb.sql` · Multi: `output_MariaDb.sql` |
| Import .CSV File(s) | `Customers.csv`, `Orders.csv` | Single CSV: `Customers_MariaDb.sql` (combined). Multiple CSVs: `_CreateTables_MariaDb.sql` + `_Data_MariaDb.sql` (consolidated) **or** `Customers_CreateTable_MariaDb.sql` etc. (per-file) |
| MS SQL Server .BAK File | `MyDatabase.bak` | `MyDatabase_MariaDb_tables.sql`, `MyDatabase_MariaDb_data.sql` |

---

## Partial Conversion Reference

> 📋 **This is a partial list** — a representative sample of the most common conversions.  
> The **complete, grouped reference** (REMOVE / ADDED / CONVERT) is in
> [`docs/CONVERSION_REFERENCE.md`](Dj-MsSql2Maria/Docs/CONVERSION_REFERENCE.md).

### 🗑️ Removed (sample)

These MS SQL tokens are **deleted** — nothing replaces them.

| Removed from output |
|---|
| `USE [DatabaseName]` |
| `SET ANSI_NULLS ON/OFF` |
| `SET QUOTED_IDENTIFIER ON/OFF` |
| `SET NOCOUNT ON/OFF` |
| `CLUSTERED` / `NONCLUSTERED` |
| `ON [PRIMARY]` / `TEXTIMAGE_ON [PRIMARY]` |
| `FILLFACTOR = n`, `PAD_INDEX = ON/OFF` |
| `ALLOW_ROW_LOCKS`, `ALLOW_PAGE_LOCKS` |

*...and more. See the full list →* [`docs/CONVERSION_REFERENCE.md`](Dj-MsSql2Maria/Docs/CONVERSION_REFERENCE.md)

---

### ➕ Added (sample)

These items are **inserted** into the output where they did not exist before.

| What is added | Why |
|---|---|
| Semicolons after `CREATE TABLE` blocks | MariaDB requires statement terminators |
| Backtick quoting on identifiers | MariaDB standard quoting style |

*...and more. See the full list →* [`docs/CONVERSION_REFERENCE.md`](Dj-MsSql2Maria/Docs/CONVERSION_REFERENCE.md)

---

### 🔄 Converted (sample)

A small representative sample of MS SQL → MariaDB replacements:

| MS SQL | → | MariaDB |
|---|:---:|---|
| `NVARCHAR(n)` | → | `VARCHAR(n)` |
| `NVARCHAR(MAX)` / `VARCHAR(MAX)` | → | `LONGTEXT` |
| `UNIQUEIDENTIFIER` | → | `CHAR(36)` |
| `BIT` | → | `TINYINT(1)` |
| `MONEY` | → | `DECIMAL(19,4)` |
| `DATETIME2` | → | `DATETIME(6)` |
| `IDENTITY(1,1)` | → | `AUTO_INCREMENT` |
| `[BracketedName]` | → | `` `BacktickName` `` |
| `dbo.TableName` | → | `TableName` |
| `GETDATE()` | → | `NOW()` |
| `GETUTCDATE()` | → | `UTC_TIMESTAMP()` |
| `DATEADD(part, n, date)` | → | `DATE_ADD(date, INTERVAL n part)` |
| `DATEPART(part, date)` | → | `EXTRACT(part FROM date)` |
| `LEN(expr)` | → | `CHAR_LENGTH(expr)` |
| `ISNULL(a, b)` | → | `IFNULL(a, b)` |
| `NEWID()` | → | `UUID()` |
| `N'string'` | → | `'string'` |
| `DEFAULT (getdate())` | → | `DEFAULT CURRENT_TIMESTAMP` |
| `IF EXISTS (…) DROP TABLE t` | → | `DROP TABLE IF EXISTS t` |
| `WITH (NOLOCK)` | → | *(removed entirely)* |

> 📋 **This is a partial list.** For every conversion including all data types, all functions,
> all removed statements, and the "not converted / manual review" list, see the full reference:
>
> **[`docs/CONVERSION_REFERENCE.md`](Dj-MsSql2Maria/Docs/CONVERSION_REFERENCE.md)**

---

## Known Limitations

- **BAK extraction** is best-effort (UTF-16/UTF-8 text scan). Compressed or encrypted BAK files
  yield little or no SQL. For guaranteed results, script the database with SSMS and use SQL file mode.
- **`SELECT TOP n`** — `TOP n` is removed during conversion. Add `LIMIT n` manually at the end of the query.
- **`DATEDIFF` with non-day units** is not fully converted — use `TIMESTAMPDIFF` manually.
- **Complex T-SQL** (`MERGE`, `PIVOT`, `TRY/CATCH`, cursors, etc.) is not converted and
  requires manual rewriting.

Full list → [`docs/CONVERSION_REFERENCE.md — NOT CONVERTED section`](Dj-MsSql2Maria/Docs/CONVERSION_REFERENCE.md#️-not-converted--manual-review-required)

---

## Documentation

| Document | Audience | Description |
|---|---|---|
| [`docs/USER_MANUAL.md`](Dj-MsSql2Maria/Docs/USER_MANUAL.md) | End users | Step-by-step guide to using the application |
| [`docs/CONVERSION_REFERENCE.md`](Dj-MsSql2Maria/Docs/CONVERSION_REFERENCE.md) | Developers | Complete reference of every REMOVE / ADDED / CONVERT transformation |

---

## Changelog

### 2.1.1 — CSV quoted-field parsing fixes

- **Fix: commas inside quoted CSV fields no longer split into extra columns.**  
  Values such as `"13,10"` or `"12,12,12,12"` were previously broken across multiple SQL columns because `double.TryParse` (with system culture) treated the comma as a decimal separator. Parsing now uses `CultureInfo.InvariantCulture` so these values are correctly emitted as single quoted strings.
- **Fix: embedded double-quotes inside CSV fields now handled correctly.**  
  RFC 4180 `""` escape sequences (e.g. `""bob", "jim", and "anne""`) are decoded to their literal `"` characters and the entire field is preserved as one SQL string value.
- **Fix: spurious empty field at end of every parsed CSV row removed.**  
  The previous `while (i <= line.Length)` loop always appended an extra empty field to every row; the rewritten parser uses `while (i < line.Length)` and adds the final field once after the loop.
- **Fix: leading whitespace before an opening quote is now ignored.**  
  CSVs with `, "field"` (space before the opening quote) are parsed as a quoted field rather than falling into the unquoted branch and splitting on internal commas.

### 2.0.1 — Initial public release

- SQL file, BAK file, and CSV import modes
- Full MS SQL → MariaDB type, function, identifier, and DDL conversion
- Portable single `.exe`, no SQL Server required

---

## Contributing

Pull requests are welcome. Please open an issue first to discuss significant changes.

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-conversion`
3. Commit your changes
4. Push and open a Pull Request

---

## License

[MIT](LICENSE)

---

*MS SQL to MariaDB 2.1.1 (Dj-MsSql2Maria) — https://www.donaldjamescompany.com/windows-app-dj-mssql2maria.html*
