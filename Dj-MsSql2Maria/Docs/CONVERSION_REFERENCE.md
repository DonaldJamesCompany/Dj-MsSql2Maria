# MS SQL to MariaDB 1.1.8 (Dj-MsSql2Maria) — SQL Conversion Reference

> **Audience:** Developers reviewing, testing, or extending the conversion output.  
> This reference lists **every** transformation applied by Dj-MsSql2Maria's `SqlConverter.cs`, grouped by action type.

---

## How to Read This Document

Transformations fall into three categories:

| Category | Meaning |
|---|---|
| ??? **REMOVE** | The MS SQL token/statement is found and **deleted entirely** from the output � nothing replaces it |
| ? **ADDED** | Something **new is inserted** into the output that was not present in the original MS SQL (e.g. a structural fix, semicolon insertion) |
| ?? **CONVERT** | An MS SQL token, keyword, data type, or function is **replaced** with its MariaDB equivalent |

---

## Table of Contents

- [??? REMOVE](#?-remove)
  - [R1 � Unsupported DDL Clauses](#r1--unsupported-ddl-clauses)
  - [R2 � Unsupported Index Options](#r2--unsupported-index-options)
  - [R3 � Session SET Statements](#r3--session-set-statements)
  - [R4 � Database Context Statement](#r4--database-context-statement)
- [? ADDED](#-added)
  - [A1 - Structural Fix: Trailing Comma](#a1--structural-fix-trailing-comma)
  - [A2 - Statement Semicolons](#a2--statement-semicolons)

- [?? CONVERT](#-convert)
  - [C1 � Data Types](#c1--data-types)
  - [C2 � Column Identity](#c2--column-identity)
  - [C3 � Default Constraints](#c3--default-constraints)
  - [C4 � Identifiers](#c4--identifiers-bracket--backtick)
  - [C5 � Schema Prefix](#c5--schema-prefix)
  - [C6 � String Functions](#c6--string-functions)
  - [C7 � Date & Time Functions](#c7--date--time-functions)
  - [C8 � String Literals](#c8--string-literals)
  - [C9 � Row Limiting](#c9--row-limiting)
  - [C10 � Conditional DDL](#c10--conditional-ddl)
- [?? NOT CONVERTED](#?-not-converted--manual-review-required)

---

## ??? REMOVE

> These items are **found and deleted**. No replacement text is written in their place.

### R1 � Unsupported DDL Clauses

| MS SQL � what is removed | Why |
|---|---|
| `CLUSTERED` | Index clustering is implicit in InnoDB; keyword is meaningless in MariaDB |
| `NONCLUSTERED` | Same as above |
| `ON [PRIMARY]` | SQL Server filegroup clause; has no MariaDB equivalent |
| `TEXTIMAGE_ON [PRIMARY]` | SQL Server filegroup clause for LOB storage; not supported |
| `WITH (STATISTICS_NORECOMPUTE = ON/OFF, �)` | SQL Server index statistics hint; not supported |

### R2 � Unsupported Index Options

These options appear inside `WITH (�)` on `CREATE INDEX` or `CREATE TABLE` index definitions.

| MS SQL � what is removed | Why |
|---|---|
| `PAD_INDEX = ON/OFF` | Index page padding; not supported in MariaDB |
| `FILLFACTOR = n` | Index fill factor; not supported in MariaDB |
| `ALLOW_ROW_LOCKS = ON/OFF` | Lock granularity hint; InnoDB manages this automatically |
| `ALLOW_PAGE_LOCKS = ON/OFF` | Lock granularity hint; InnoDB manages this automatically |

### R3 � Session SET Statements

| MS SQL � what is removed | Why |
|---|---|
| `SET ANSI_NULLS ON` | ANSI NULL semantics are always enforced in MariaDB |
| `SET ANSI_NULLS OFF` | Same |
| `SET QUOTED_IDENTIFIER ON` | MariaDB uses backtick quoting; this setting is irrelevant |
| `SET QUOTED_IDENTIFIER OFF` | Same |
| `SET ANSI_PADDING ON` | Not applicable to MariaDB |
| `SET ANSI_PADDING OFF` | Same |
| `SET NOCOUNT ON` | No equivalent � row-count suppression is not available in the same way |
| `SET NOCOUNT OFF` | Same |
| `SET XACT_ABORT ON` | No direct equivalent in MariaDB |
| `SET XACT_ABORT OFF` | Same |

### R4 � Database Context Statement

| MS SQL � what is removed | Why |
|---|---|
| `USE [DatabaseName]` | Removed from script body; add `USE database_name;` manually if needed |

---

## ? ADDED

> These items are **inserted into the output** where they did not previously exist.

### A1 - Structural Fix: Trailing Comma

After stripping unsupported options a trailing comma before ) may remain.
The converter removes it, producing valid syntax.

| Before fix | After fix |
|---|---|
| `LastCol INT,)` | `LastCol INT)` |

### A2 - Statement Semicolons

MariaDB requires each SQL statement to end with ;. The converter appends a semicolon
after every CREATE TABLE closing ) and ensures the final statement in the script is
also terminated.

| Before | After |
|---|---|
| `CREATE TABLE t ( id INT )` | `CREATE TABLE t ( id INT );` |
---

## ?? CONVERT

> MS SQL on the left is **replaced** with the MariaDB equivalent on the right.

### C1 � Data Types

| MS SQL Type | ? | MariaDB Type | Notes |
|---|:---:|---|---|
| `NVARCHAR(n)` | ? | `VARCHAR(n)` | MariaDB VARCHAR is Unicode with utf8mb4 charset |
| `NVARCHAR(MAX)` | ? | `LONGTEXT` | |
| `VARCHAR(MAX)` | ? | `LONGTEXT` | |
| `NCHAR(n)` | ? | `CHAR(n)` | |
| `NTEXT` | ? | `LONGTEXT` | Deprecated in SQL Server also |
| `TEXT` | ? | `LONGTEXT` | |
| `IMAGE` | ? | `LONGBLOB` | |
| `VARBINARY(MAX)` | ? | `LONGBLOB` | |
| `VARBINARY(n)` | ? | `VARBINARY(n)` | Unchanged � listed for completeness |
| `UNIQUEIDENTIFIER` | ? | `CHAR(36)` | Stores formatted UUID string; size specifier stripped |
| `BIT` | ? | `TINYINT(1)` | MariaDB has no single-bit boolean column type |
| `SMALLMONEY` | ? | `DECIMAL(10,4)` | |
| `MONEY` | ? | `DECIMAL(19,4)` | |
| `REAL` | ? | `FLOAT` | |
| `SMALLDATETIME` | ? | `DATETIME` | Minute-level precision only |
| `DATETIME2` | ? | `DATETIME(6)` | Microsecond precision preserved |
| `DATETIMEOFFSET` | ? | `DATETIME(6)` | ? Timezone offset is lost � store separately if needed |
| `XML` | ? | `LONGTEXT` | No native XML type in MariaDB |
| `HIERARCHYID` | ? | `VARBINARY(892)` | App-level hierarchy handling required |
| `GEOGRAPHY` | ? | `GEOMETRY` | WGS-84 function names differ � test carefully |
| `GEOMETRY` | ? | `GEOMETRY` | Unchanged � listed for completeness |
| `ROWVERSION` | ? | `TIMESTAMP` | ? Semantics differ significantly � review manually |
| `TIMESTAMP` | ? | `TIMESTAMP` | Unchanged � listed for completeness |
| `SQL_VARIANT` | ? | `TEXT` | Typed storage is lost |

### C2 � Column Identity

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `IDENTITY(1,1)` | ? | `AUTO_INCREMENT` | Seed and increment values are ignored |
| `IDENTITY(seed, incr)` | ? | `AUTO_INCREMENT` | ? Non-default seed/increment requires manual ALTER TABLE |
| `IDENTITY` (bare) | ? | `AUTO_INCREMENT` | |

### C3 � Default Constraints

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `CONSTRAINT [df_Name] DEFAULT (value)` | ? | `DEFAULT value` | Named wrapper removed; value preserved |
| `DEFAULT (getdate())` | ? | `DEFAULT CURRENT_TIMESTAMP` | |
| `DEFAULT (newid())` | ? | `DEFAULT (UUID())` | |
| `DEFAULT (0)` | ? | `DEFAULT 0` | Parentheses stripped |
| `DEFAULT (1)` | ? | `DEFAULT 1` | Parentheses stripped |
| `DEFAULT (any_literal)` | ? | `DEFAULT any_literal` | Generic paren strip for scalar literals |

### C4 � Identifiers (Bracket ? Backtick)

| MS SQL | ? | MariaDB |
|---|:---:|---|
| `[ColumnName]` | ? | `` `ColumnName` `` |
| `[Table Name With Spaces]` | ? | `` `Table Name With Spaces` `` |
| `[Any Identifier]` | ? | `` `Any Identifier` `` |

### C5 � Schema Prefix

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `dbo.TableName` | ? | `TableName` | dbo. prefix stripped |
| `dbo.[TableName]` | ? | `` `TableName` `` | Bracket conversion also applied |
| Other schemas (hr., sales., �) | ? | *(not converted)* | Must be removed or mapped manually |

### C6 � String Functions

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `LEN(expr)` | ? | `CHAR_LENGTH(expr)` | CHAR_LENGTH counts characters not bytes (Unicode-safe) |
| `CHARINDEX(search, str)` | ? | `LOCATE(search, str)` | Same argument order |
| `ISNULL(expr, replacement)` | ? | `IFNULL(expr, replacement)` | |
| `IIF(cond, true_val, false_val)` | ? | `IF(cond, true_val, false_val)` | |
| `NEWID()` | ? | `UUID()` | |
| `CONVERT(VARCHAR�, expr)` | ? | `CAST(expr AS CHAR)` | ? Style/format codes lost � review date format conversions |
| `SUBSTRING(str, start, len)` | ? | `SUBSTRING(str, start, len)` | Identical � listed for completeness |

### C7 � Date & Time Functions

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `GETDATE()` | ? | `NOW()` | |
| `GETUTCDATE()` | ? | `UTC_TIMESTAMP()` | |
| `SYSDATETIME()` | ? | `NOW(6)` | Microsecond precision |
| `DATEADD(part, n, date)` | ? | `DATE_ADD(date, INTERVAL n part)` | ? Argument order changes |
| `DATEDIFF(part, start, end)` | ? | `DATEDIFF(end, start)` | ? Returns days only � use TIMESTAMPDIFF for non-day units |
| `DATEPART(part, date)` | ? | `EXTRACT(part FROM date)` | |

> **Note on abbreviated interval parts:** SQL Server accepts `yy`, `mm`, `dd`, `hh`, `mi`, `ss`
> in DATEADD/DATEPART. These are **not** automatically expanded � replace with
> `YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND` manually.

### C8 � String Literals

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `N'unicode string'` | ? | `'unicode string'` | N prefix stripped; strings are Unicode with utf8mb4 |

### C9 � Row Limiting

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `SELECT TOP n ...` | -> | `SELECT ...` | TOP n clause removed; add `LIMIT n` at end of query manually |
| `SELECT TOP (n) ...` | -> | `SELECT ...` | Same as above |

### C10 � Conditional DDL

| MS SQL | ? | MariaDB | Notes |
|---|:---:|---|---|
| `IF EXISTS (SELECT �) DROP TABLE tbl` | ? | `DROP TABLE IF EXISTS tbl` | |
| `IF OBJECT_ID('tbl') IS NOT NULL DROP TABLE tbl` | -> | `DROP TABLE tbl` | Add IF EXISTS manually for safety |
| `IF OBJECT_ID('proc','P') IS NOT NULL DROP PROCEDURE proc` | -> | `DROP PROCEDURE proc` | Add IF EXISTS manually for safety |

---

## ?? NOT CONVERTED � Manual Review Required

The constructs below are **not changed by the converter**. They appear in the output unchanged
and must be manually rewritten before the script will run on MariaDB.

| MS SQL Feature | Recommended MariaDB approach |
|---|---|
| `MERGE` statement | `INSERT � ON DUPLICATE KEY UPDATE` or separate INSERT/UPDATE/DELETE |
| `PIVOT` / `UNPIVOT` | Rewrite using CASE/IF inside GROUP BY aggregation |
| `TRY � CATCH` block | `DECLARE � HANDLER FOR SQLEXCEPTION` in stored procedures |
| `OUTPUT` clause | Triggers or application-side logic |
| `CROSS APPLY` / `OUTER APPLY` | `JOIN � LATERAL (�)` |
| `ROW_NUMBER() OVER (�)` | Window functions supported in MariaDB 10.2+ � verify syntax |
| `SEQUENCE` objects | AUTO_INCREMENT column or a dedicated sequence table |
| `COMPUTE BY` | GROUP BY with aggregate functions |
| Computed columns `col AS (expr) PERSISTED` | `col � GENERATED ALWAYS AS (expr) STORED` |
| `COLLATE` clauses | Replace SQL Server collation names with MariaDB equivalents (e.g. utf8mb4_unicode_ci) |
| Linked Server / four-part names | FEDERATED storage engine or application-side federation |
| `BULK INSERT` | `LOAD DATA INFILE` |
| `OPENROWSET` / `OPENQUERY` | Application-side logic |
| `EXECUTE AS` / `WITH EXECUTE AS` | `DEFINER = user` / `SQL SECURITY INVOKER` on routines |
| Non-dbo schema prefixes | Remove or map to a MariaDB database name manually |
| DATETIMEOFFSET timezone values | Store timezone offset in a separate column |
| Non-default IDENTITY seed or increment | `ALTER TABLE t AUTO_INCREMENT = seed;` |
| Abbreviated DATEADD/DATEPART parts (yy, mm, dd �) | Expand to YEAR, MONTH, DAY, HOUR, MINUTE, SECOND |
| DATEDIFF with non-day units | `TIMESTAMPDIFF(part, start, end)` |

---

*MS SQL to MariaDB 1.1.8 (Dj-MsSql2Maria) — https://github.com/DonaldJamesCompany/Dj-MsSql2Maria*
