# MS SQL to MariaDB 1.0.0 (Dj-MsSql2Maria) — End-User Manual

> **Version 1.0** | .NET 9 · Windows x64  
> Converts Microsoft SQL Server `.SQL` scripts and `.BAK` backup files into MariaDB-compatible SQL.

---

## Table of Contents

1. [Overview](#1-overview)
2. [System Requirements](#2-system-requirements)
3. [Installation](#3-installation)
4. [Application Layout](#4-application-layout)
5. [Step-by-Step Usage](#5-step-by-step-usage)
   - 5.1 [Convert a Single SQL File](#51-convert-a-single-sql-file)
   - 5.2 [Convert Multiple SQL Files](#52-convert-multiple-sql-files)
   - 5.3 [Convert from a BAK Backup File](#53-convert-from-a-bak-backup-file)
6. [Output File Naming](#6-output-file-naming)
7. [Button Reference](#7-button-reference)
8. [The Log Panel](#8-the-log-panel)
9. [Progress Bar](#9-progress-bar)
10. [Known Limitations](#10-known-limitations)
11. [Troubleshooting](#11-troubleshooting)
12. [Frequently Asked Questions](#12-frequently-asked-questions)

---

## 1. Overview

**Dj-MsSql2Maria** is a portable, standalone Windows desktop application. It reads one or more Microsoft SQL Server `.SQL` script files, or a SQL Server `.BAK` backup file, and produces a single consolidated MariaDB-compatible `.SQL` output file.

No installation is required — the application ships as a single self-contained `.exe`.

---

## 2. System Requirements

| Requirement | Minimum |
|---|---|
| Operating System | Windows 10 (x64) or later |
| Disk Space | ~60 MB (self-contained runtime included) |
| .NET Runtime | None required — bundled inside the `.exe` |
| SQL Server | **Not required** for `.SQL` conversion; see BAK notes below |

---

## 3. Installation

1. Copy `Dj-MsSql2Maria.exe` to any folder you have read/write access to.
2. Double-click the file to launch Dj-MsSql2Maria — no installer, no registry changes.

To publish the portable executable yourself from source:

```powershell
dotnet publish -c Release
```

The output will be located in `bin\Release\net9.0-windows\win-x64\publish\`.

---

## 4. Application Layout

```
┌─────────────────────────────────────────────────────┐
│  Input                                              │
│    Type:  [ Single .SQL File ▼ ]                    │
│    File:  [________________________] [Browse…]      │
│    ☐ Create MariaDB script for Tables?  (BAK only)  │
│    ☐ Create MariaDB script for Data?    (BAK only)  │
├─────────────────────────────────────────────────────┤
│  Output                                             │
│    Folder: [_____________________] [Browse…]        │
│    ☐ Append to filename?  [_MariaDb_____________]   │
├─────────────────────────────────────────────────────┤
│  Log                                                │
│  ┌───────────────────────────────────────────────┐  │
│  │  (scrollable log output)                      │  │
│  └───────────────────────────────────────────────┘  │
│  [████████████░░░░░░░░] Progress Bar                │
│  [ STOP ]  [ CLEAR ]  [ GO ]  [ EXIT ]              │
└─────────────────────────────────────────────────────┘
```

---

## 5. Step-by-Step Usage

### 5.1 Convert a Single SQL File

1. In Dj-MsSql2Maria's **Type** drop-down, select **Single .SQL File** (the default).
2. Click **Browse…** next to the **File** field.
3. Navigate to and select your `.sql` file.
4. Click **Browse…** next to the **Folder** field and choose where to save the output.
5. *(Optional)* Check **Append to filename?** and edit the suffix (default `_MariaDb`).
6. Click **GO**.

The output file will be named after the input file, e.g. `Customers.sql` → `Customers_MariaDb.sql`.

---

### 5.2 Convert Multiple SQL Files

1. In Dj-MsSql2Maria's **Type** drop-down, select **Multiple .SQL Files**.
2. Click **Browse…** — a standard multi-select file dialog opens.
3. Hold **Ctrl** or **Shift** to select several `.sql` files, then click **Open**.
4. Choose an output folder.
5. *(Optional)* Set the filename suffix.
6. Click **GO**.

All selected files are concatenated and converted into a single output file named `output_MariaDb.sql` (or `output.sql` if the suffix option is unchecked).

---

### 5.3 Convert from a BAK Backup File

> **Important:** BAK extraction is a *best-effort* text scan.  
> For a complete, guaranteed extraction, attach the BAK to a SQL Server instance,
> script the objects using SSMS, then convert the resulting `.sql` files using
> Single or Multiple mode.

1. In Dj-MsSql2Maria's **Type** drop-down, select **MS SQL Server .BAK File**.
2. Click **Browse…** — the file dialog is filtered to `.bak` files only.
3. Two extra checkboxes become visible in the Input section:
   - **Create MariaDB script for Tables?** — extracts `CREATE TABLE` statements.
   - **Create MariaDB script for Data?** — extracts `INSERT` statements.
   - At least one must be logically useful; both are checked by default.
4. Choose an output folder.
5. *(Optional)* Set the filename suffix.
6. Click **GO**.

The output file is named after the BAK file, e.g. `MyDatabase_MariaDb.sql`.

---

## 6. Output File Naming

| Input Mode | Base name | With suffix `_MariaDb` |
|---|---|---|
| Single .SQL File | Same as input file | `OriginalName_MariaDb.sql` |
| Multiple .SQL Files | `output` | `output_MariaDb.sql` |
| BAK File | Same as BAK file | `BackupName_MariaDb.sql` |

The suffix text field is freely editable. Clear it to use an empty suffix, or type any valid filename characters.

---

## 7. Button Reference

| Button | Keyboard | Enabled when | Action |
|---|---|---|---|
| **GO** | — | Not processing | Starts the conversion process |
| **STOP** | — | Processing is active | Immediately cancels the current operation |
| **CLEAR** | — | Not processing | Resets all controls to their default values |
| **EXIT** | — | Not processing | Closes the application |

> After clicking **STOP**, the output file may be partially written. Delete it before retrying.

---

## 8. The Log Panel

The **Log** panel shows real-time progress messages as each file is processed:

```
Converting 3 file(s)…
  Processing: Customers.sql
  Processing: Orders.sql
  Processing: Products.sql
✔  Done. Output: C:\Output\output_MariaDb.sql
```

If an error occurs, a red `ERROR:` message is shown and a dialog box is displayed.

The log is cleared each time **GO** is clicked, and can also be cleared with the **CLEAR** button.

---

## 9. Progress Bar

The progress bar fills from 0 % to 100 % as files are processed.

- For **SQL file** mode, progress advances evenly per file.
- For **BAK** mode, progress advances in phases: scan (0–60 %), write (60–100 %).

---

## 10. Known Limitations

| Limitation | Detail |
|---|---|
| BAK extraction is best-effort | Only UTF-16 and UTF-8 text segments are scanned; binary data pages, compressed backups, and encrypted backups cannot be decoded without a live SQL Server |
| `SELECT TOP n` | Converted to a comment hint; you must manually move `LIMIT n` to the end of the query |
| `DATEDIFF` with non-day units | MariaDB's `DATEDIFF` is days-only; sub-day precision results need manual review |
| Stored Procedures & Functions | Syntax inside procedure bodies is converted on a best-effort basis; complex T-SQL logic (cursors, `TRY/CATCH`, `MERGE`) requires manual review |
| Schema prefixes other than `dbo` | Only the `dbo.` prefix is automatically removed; other schemas (e.g. `hr.`, `sales.`) must be handled manually |
| Collations | `COLLATE` clauses are not altered; MariaDB uses its own collation names |
| Computed columns | `AS (expression) PERSISTED` computed column syntax is not converted |

---

## 11. Troubleshooting

**The Browse… (folder) dialog does not open on Windows 10**  
Ensure you are running the latest Windows 10 update. The folder picker uses the Windows Shell dialog which requires an up-to-date shell component.

**Output file is empty or very small after BAK conversion**  
The BAK file may be compressed or encrypted. Attach it to SQL Server, run SSMS "Script Database As", and convert the generated `.sql` files instead.

**"Input required" warning appears even though a file is selected**  
This can happen if the file path contains a semicolon (`;`). Rename the file or folder to remove the semicolon and try again.

**The app closes immediately on launch**  
Ensure your antivirus software is not blocking the self-contained executable. Right-click → Properties → Unblock if necessary.

**Converted SQL still has errors in MariaDB**  
Review the [Known Limitations](#10-known-limitations) above. Complex T-SQL constructs such as `MERGE`, `PIVOT`, `TRY/CATCH`, and multi-schema objects will need manual editing after conversion.

---

## 12. Frequently Asked Questions

**Q: Does the app connect to SQL Server or MariaDB?**  
A: No. It is entirely offline. It reads files from disk and writes converted files to disk only.

**Q: Will the output file overwrite an existing file with the same name?**  
A: Yes, without prompting. Choose a different output folder or suffix if you want to preserve previous output.

**Q: Can I run multiple instances simultaneously?**  
A: Yes. Each instance operates independently on its own files.

**Q: Is Unicode content preserved?**  
A: Yes. All output files are written as UTF-8. `N'...'` Unicode string literals are converted to plain `'...'` literals (MariaDB strings are Unicode by default when using a `utf8mb4` character set).

**Q: What MariaDB version is the output compatible with?**  
A: The output targets MariaDB 10.4 and later. Most features are compatible with MariaDB 10.2+.

**Q: Can I automate this from the command line?**  
A: The current version is GUI-only. Command-line support may be added in a future release.

---

*MS SQL to MariaDB 1.0.0 (Dj-MsSql2Maria) is open source. Source code: https://github.com/DonaldJamesCompany/Dj-MsSql2Maria*
