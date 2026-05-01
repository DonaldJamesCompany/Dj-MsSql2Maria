using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dj_MsSql2Maria;

/// <summary>A single SQL segment extracted from a BAK file, tagged with its origin.</summary>
internal sealed record BakSegment(string TableName, BakSegmentType Type, string Sql);

internal enum BakSegmentType { Table, Data }

/// <summary>Controls how the converter emits DDL/DML guards in the output SQL.</summary>
internal sealed record ConversionOptions(
    /// <summary>Wrap every CREATE TABLE with DROP TABLE IF EXISTS … before it.</summary>
    bool DropTableIfExists,
    /// <summary>Use INSERT IGNORE instead of INSERT for data rows.</summary>
    bool InsertIgnore
)
{
    public static readonly ConversionOptions Default = new(
        DropTableIfExists: false,
        InsertIgnore: false);
}

/// <summary>
/// Converts MS SQL Server DDL/DML syntax to MariaDB-compatible SQL,
/// and extracts embedded SQL text from .BAK backup files.
/// </summary>
internal static class SqlConverter
{
    // ── Public entry points ──────────────────────────────────────────────────

    /// <summary>Converts a complete MS SQL script string to MariaDB syntax.</summary>
    public static string ConvertToMariaDb(string sql, ConversionOptions? options = null)
    {
        options ??= ConversionOptions.Default;
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        sql = StripComments(sql);
        sql = RemoveUnsupportedStatements(sql);
        sql = ConvertSchemaPrefixes(sql);      // must run before bracket conversion
        sql = ConvertBracketIdentifiers(sql);  // converts [name] → `name`
        sql = UnquoteDataTypes(sql);           // unquote bracket-quoted type names (e.g. `nvarchar` → nvarchar in column-type position)
        sql = ConvertDatatypes(sql);           // map bare MSSQL type names → MariaDB equivalents
        sql = ConvertIdentityToAutoIncrement(sql);
        sql = ConvertDefaultConstraints(sql);
        sql = ConvertStringFunctions(sql);
        sql = ConvertDateFunctions(sql);
        sql = ConvertGoStatements(sql);
        sql = ConvertSetStatements(sql);
        sql = ConvertNVarcharLiterals(sql);
        sql = ConvertTopToLimit(sql);
        sql = ConvertIfExists(sql);
        sql = ConvertWithNolock(sql);
        sql = FixTrailingCommasBeforeCloseParen(sql);
        sql = EnsureStatementSemicolons(sql);

        // Options-driven post-passes
        if (options.DropTableIfExists)
            sql = AddDropTableIfExists(sql);
        if (options.InsertIgnore)
            sql = ConvertInsertToInsertIgnore(sql);

        return sql.Trim();
    }

    // ── CSV import ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a single CSV file and returns two SQL strings:
    /// <list type="bullet">
    ///   <item><description><c>createSql</c> — a CREATE TABLE statement inferred from the headers.</description></item>
    ///   <item><description><c>dataSql</c>   — INSERT INTO … statements for every data row.</description></item>
    /// </list>
    /// Column types default to LONGTEXT; numeric-looking columns use DOUBLE.
    /// </summary>
    public static (string createSql, string dataSql) CsvToMariaDb(
        string csvPath, ConversionOptions options, CancellationToken ct)
    {
        string tableName = Path.GetFileNameWithoutExtension(csvPath);
        string backtickTable = $"`{tableName}`";

        var lines = File.ReadAllLines(csvPath);
        if (lines.Length == 0)
            return (string.Empty, string.Empty);

        string[] headers = ParseCsvLine(lines[0]);

        // Drop any columns whose header is blank (e.g. trailing comma in the header row).
        var validIndices = Enumerable.Range(0, headers.Length)
            .Where(i => !string.IsNullOrWhiteSpace(headers[i]))
            .ToArray();
        headers = validIndices.Select(i => headers[i]).ToArray();

        // Infer column types by scanning all data rows
        var isNumeric = new bool[headers.Length];
        Array.Fill(isNumeric, true);
        for (int r = 1; r < lines.Length; r++)
        {
            if (string.IsNullOrWhiteSpace(lines[r])) continue;
            var row = ParseCsvLine(lines[r]);
            for (int c = 0; c < headers.Length; c++)
            {
                string cell = validIndices[c] < row.Length ? row[validIndices[c]] : string.Empty;
                if (!string.IsNullOrEmpty(cell) && !double.TryParse(cell, out _))
                    isNumeric[c] = false;
            }
        }

        // BUILD CREATE TABLE
        var ct_sb = new StringBuilder();
        if (options.DropTableIfExists)
            ct_sb.AppendLine($"DROP TABLE IF EXISTS {backtickTable};");
        ct_sb.AppendLine($"CREATE TABLE {backtickTable}(");
        ct_sb.AppendLine($"\t`NewID` bigint AUTO_INCREMENT PRIMARY KEY NOT NULL,");
        for (int c = 0; c < headers.Length; c++)
        {
            string colType = isNumeric[c] ? "DOUBLE" : "LONGTEXT";
            string comma = c < headers.Length - 1 ? "," : string.Empty;
            ct_sb.AppendLine($"\t`{EscapeIdentifier(headers[c])}` {colType} NULL{comma}");
        }
        ct_sb.Append(");");

        // BUILD INSERT statements
        var d_sb = new StringBuilder();
        string colList = string.Join(", ", headers.Select(h => $"`{EscapeIdentifier(h)}`"));
        string insertVerb = options.InsertIgnore ? "INSERT IGNORE INTO" : "INSERT INTO";

        for (int r = 1; r < lines.Length; r++)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[r])) continue;
            var rawRow = ParseCsvLine(lines[r]);
            // Map only the valid (non-blank-header) columns
            var row = validIndices.Select(i => i < rawRow.Length ? rawRow[i] : string.Empty).ToArray();
            var values = new string[headers.Length];
            for (int c = 0; c < headers.Length; c++)
            {
                string raw = c < row.Length ? row[c] : string.Empty;
                if (string.IsNullOrEmpty(raw))
                    values[c] = "NULL";
                else if (isNumeric[c] && double.TryParse(raw, out _))
                    values[c] = raw;
                else
                    values[c] = $"'{EscapeSqlString(raw)}'";
            }
            d_sb.AppendLine($"{insertVerb} {backtickTable} ({colList}) VALUES ({string.Join(", ", values)});");
        }

        return (ct_sb.ToString(), d_sb.ToString());
    }

    /// <summary>Parses one CSV line respecting double-quoted fields (RFC 4180).</summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i <= line.Length)
        {
            if (i == line.Length) { fields.Add(string.Empty); break; }
            if (line[i] == '"')
            {
                // Quoted field
                i++;
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                        else { i++; break; }
                    }
                    else { sb.Append(line[i++]); }
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                int start = i;
                while (i < line.Length && line[i] != ',') i++;
                fields.Add(line[start..i]);
                if (i < line.Length) i++; // skip comma
            }
        }
        return [.. fields];
    }

    private static string EscapeIdentifier(string name) =>
        name.Replace("`", "``");

    private static string EscapeSqlString(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
    /// This is a best-effort byte-scan approach that does not require a live SQL Server instance.
    /// </summary>
    public static List<BakSegment> ExtractSqlFromBak(
        string bakPath, bool includeTables, bool includeData, CancellationToken ct)
    {
        var results = new List<BakSegment>();

        byte[] bytes = File.ReadAllBytes(bakPath);

        // Try UTF-16 LE decode first (most common for SQL Server internal strings)
        string full16 = Encoding.Unicode.GetString(bytes);
        ExtractSegments(full16, includeTables, includeData, results, ct);

        // Also try ASCII / UTF-8 in case of older backups or mixed content
        if (results.Count == 0)
        {
            string full8 = Encoding.UTF8.GetString(bytes);
            ExtractSegments(full8, includeTables, includeData, results, ct);
        }

        if (results.Count == 0)
            results.Add(new BakSegment("_no_sql_found", BakSegmentType.Table,
                "SELECT 'Dj-MsSql2Maria: No extractable SQL text was found in this BAK file. Attach to SQL Server, script with SSMS, then use SQL file mode.' AS Note;"));

        return results;
    }

    // ── BAK extraction helpers ───────────────────────────────────────────────

    private static readonly Regex CreateTableRx = new(
        @"CREATE\s+TABLE\s+([\[\`]?[\w\s]+[\]\`]?)\s*[\(\s\S]{10,2000}?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InsertRx = new(
        @"INSERT\s+(?:INTO\s+)?([\[\`]?[\w\s]+[\]\`]?)[\s\S]{5,1000}?;",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TableNameCleanRx = new(@"[\[\]\`\s]", RegexOptions.Compiled);

    private static void ExtractSegments(
        string text, bool tables, bool data,
        List<BakSegment> results, CancellationToken ct)
    {
        if (tables)
        {
            foreach (Match m in CreateTableRx.Matches(text))
            {
                ct.ThrowIfCancellationRequested();
                string s = m.Value.Trim();
                if (s.Length > 30 && IsPrintable(s))
                {
                    string tableName = TableNameCleanRx.Replace(m.Groups[1].Value, "_").Trim('_');
                    results.Add(new BakSegment(tableName, BakSegmentType.Table, s));
                }
            }
        }

        if (data)
        {
            foreach (Match m in InsertRx.Matches(text))
            {
                ct.ThrowIfCancellationRequested();
                string s = m.Value.Trim();
                if (s.Length > 20 && IsPrintable(s))
                {
                    string tableName = TableNameCleanRx.Replace(m.Groups[1].Value, "_").Trim('_');
                    results.Add(new BakSegment(tableName, BakSegmentType.Data, s));
                }
            }
        }
    }

    private static bool IsPrintable(string s)
    {
        int printable = s.Count(c => c >= 0x20 && c < 0x7F);
        return printable * 100 / s.Length > 60;
    }

    // ── Conversion passes ────────────────────────────────────────────────────

    private static string StripComments(string sql)
    {
        // Remove block comments /* ... */ (including multi-line)
        sql = Regex.Replace(sql, @"/\*.*?\*/", string.Empty,
            RegexOptions.Singleline);
        // Remove line comments -- ...
        sql = Regex.Replace(sql, @"--[^\r\n]*", string.Empty,
            RegexOptions.Multiline);
        // Collapse blank lines left behind
        sql = Regex.Replace(sql, @"(\r?\n){3,}", "\r\n\r\n");
        return sql;
    }

    private static string RemoveUnsupportedStatements(string sql)
    {
        // Remove USE [db]
        sql = Regex.Replace(sql, @"^\s*USE\s+\[?\w+\]?\s*;?\s*$", string.Empty,
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Remove SET statements that are MSSQL-only
        sql = Regex.Replace(sql,
            @"^\s*SET\s+(ANSI_NULLS|QUOTED_IDENTIFIER|ANSI_PADDING|NOCOUNT)\s+(ON|OFF)\s*;?\s*$",
            string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Remove WITH (STATISTICS_NORECOMPUTE …) index options
        sql = Regex.Replace(sql,
            @"\bWITH\s*\(\s*STATISTICS_NORECOMPUTE\s*=\s*(ON|OFF)[^)]*\)",
            string.Empty, RegexOptions.IgnoreCase);

        // Remove CLUSTERED / NONCLUSTERED keywords (MariaDB ignores)
        sql = Regex.Replace(sql, @"\b(CLUSTERED|NONCLUSTERED)\b", string.Empty,
            RegexOptions.IgnoreCase);

        // Remove TEXTIMAGE_ON, ON [PRIMARY]
        sql = Regex.Replace(sql,
            @"\b(TEXTIMAGE_ON|ON)\s+\[?(PRIMARY|\w+)\]?",
            string.Empty, RegexOptions.IgnoreCase);

        // Remove PAD_INDEX / FILLFACTOR options
        sql = Regex.Replace(sql,
            @",?\s*PAD_INDEX\s*=\s*(ON|OFF)",
            string.Empty, RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql,
            @",?\s*FILLFACTOR\s*=\s*\d+",
            string.Empty, RegexOptions.IgnoreCase);

        // Remove ALLOW_ROW_LOCKS / ALLOW_PAGE_LOCKS
        sql = Regex.Replace(sql,
            @",?\s*ALLOW_(ROW|PAGE)_LOCKS\s*=\s*(ON|OFF)",
            string.Empty, RegexOptions.IgnoreCase);

        return sql;
    }

    private static string ConvertDatatypes(string sql)
    {
        // Map MSSQL types → MariaDB equivalents
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NVARCHAR"]        = "VARCHAR",
            ["NCHAR"]           = "CHAR",
            ["NTEXT"]           = "LONGTEXT",
            ["TEXT"]            = "LONGTEXT",
            ["IMAGE"]           = "LONGBLOB",
            ["VARBINARY"]       = "VARBINARY",
            ["UNIQUEIDENTIFIER"]= "CHAR(36)",
            ["BIT"]             = "TINYINT(1)",
            ["SMALLMONEY"]      = "DECIMAL(10,4)",
            ["MONEY"]           = "DECIMAL(19,4)",
            ["REAL"]            = "FLOAT",
            ["SMALLDATETIME"]   = "DATETIME",
            ["DATETIME2"]       = "DATETIME(6)",
            ["DATETIMEOFFSET"]  = "DATETIME(6)",
            ["XML"]             = "LONGTEXT",
            ["HIERARCHYID"]     = "VARBINARY(892)",
            ["GEOGRAPHY"]       = "GEOMETRY",
            ["GEOMETRY"]        = "GEOMETRY",
            ["ROWVERSION"]      = "TIMESTAMP",
            ["TIMESTAMP"]       = "TIMESTAMP",
            ["SQL_VARIANT"]     = "TEXT",
        };

        foreach (var (mssql, maria) in map)
        {
            // Replace as whole word. Backtick-quoted identifiers are safe here because
            // UnquoteDataTypes already ran and only unquoted tokens in column-type position;
            // table/column names that share a type keyword remain backtick-quoted.
            if (maria.Contains('('))
            {
                // Fixed replacement: also remove any existing (n) trailing size
                sql = Regex.Replace(sql,
                    $@"(?<!`)\b{Regex.Escape(mssql)}\b(\s*\(\s*[\w,\s]+\s*\))?(?!`)",
                    maria, RegexOptions.IgnoreCase);
            }
            else
            {
                sql = Regex.Replace(sql,
                    $@"(?<!`)\b{Regex.Escape(mssql)}\b(?!`)",
                    maria, RegexOptions.IgnoreCase);
            }
        }

        // NVARCHAR(MAX) / VARCHAR(MAX) → LONGTEXT  (leading and/or trailing backtick tolerated)
        sql = Regex.Replace(sql, @"`?VARCHAR`?\s*\(\s*MAX\s*\)", "LONGTEXT", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"`?VARBINARY`?\s*\(\s*MAX\s*\)", "LONGBLOB", RegexOptions.IgnoreCase);

        return sql;
    }

    private static string ConvertIdentityToAutoIncrement(string sql)
    {
        // IDENTITY(seed, incr) → AUTO_INCREMENT PRIMARY KEY
        sql = Regex.Replace(sql,
            @"\bIDENTITY\s*\(\s*\d+\s*,\s*\d+\s*\)",
            "AUTO_INCREMENT PRIMARY KEY", RegexOptions.IgnoreCase);

        sql = Regex.Replace(sql, @"\bIDENTITY\b", "AUTO_INCREMENT PRIMARY KEY", RegexOptions.IgnoreCase);

        // Safety pass: if AUTO_INCREMENT exists without PRIMARY KEY (e.g. already in source), add it
        sql = Regex.Replace(sql,
            @"\bAUTO_INCREMENT\b(?!\s+PRIMARY\s+KEY)",
            "AUTO_INCREMENT PRIMARY KEY", RegexOptions.IgnoreCase);

        return sql;
    }

    private static string ConvertDefaultConstraints(string sql)
    {
        // CONSTRAINT [df_xxx] DEFAULT (value) → DEFAULT value
        sql = Regex.Replace(sql,
            @"\bCONSTRAINT\s+\[?\w+\]?\s+DEFAULT\s*\(([^)]*)\)",
            "DEFAULT $1", RegexOptions.IgnoreCase);

        // DEFAULT (getdate()) → DEFAULT CURRENT_TIMESTAMP
        sql = Regex.Replace(sql,
            @"\bDEFAULT\s*\(\s*getdate\s*\(\s*\)\s*\)",
            "DEFAULT CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);

        // DEFAULT (newid()) → DEFAULT (UUID())
        sql = Regex.Replace(sql,
            @"\bDEFAULT\s*\(\s*newid\s*\(\s*\)\s*\)",
            "DEFAULT (UUID())", RegexOptions.IgnoreCase);

        // DEFAULT (0) / DEFAULT (1) → strip parens
        sql = Regex.Replace(sql,
            @"\bDEFAULT\s*\(([^)(]+)\)",
            "DEFAULT $1", RegexOptions.IgnoreCase);

        return sql;
    }

    private static string ConvertBracketIdentifiers(string sql)
    {
        // [identifier] → `identifier`  (any chars except [ and ] are valid, e.g. [LOCKS - Copy])
        return Regex.Replace(sql, @"\[([^\[\]]+)\]", "`$1`");
    }

    private static string ConvertStringFunctions(string sql)
    {
        sql = Regex.Replace(sql, @"\bLEN\s*\(", "CHAR_LENGTH(", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bCHARINDEX\s*\(([^,]+),([^)]+)\)",
            "LOCATE($1,$2)", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bSUBSTRING\s*\(", "SUBSTRING(", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bISNULL\s*\(", "IFNULL(", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bIIF\s*\(([^,]+),([^,]+),([^)]+)\)",
            "IF($1,$2,$3)", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bNEWID\s*\(\s*\)", "UUID()", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bCONVERT\s*\(\s*VARCHAR[^,]*,\s*([^)]+)\)",
            "CAST($1 AS CHAR)", RegexOptions.IgnoreCase);
        return sql;
    }

    private static string ConvertDateFunctions(string sql)
    {
        sql = Regex.Replace(sql, @"\bGETDATE\s*\(\s*\)", "NOW()", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bGETUTCDATE\s*\(\s*\)", "UTC_TIMESTAMP()", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bSYSDATETIME\s*\(\s*\)", "NOW(6)", RegexOptions.IgnoreCase);
        // DATEADD(part, n, date) → DATE_ADD(date, INTERVAL n part)
        sql = Regex.Replace(sql,
            @"\bDATEADD\s*\(\s*(\w+)\s*,\s*([^,]+),\s*([^)]+)\)",
            "DATE_ADD($3, INTERVAL $2 $1)", RegexOptions.IgnoreCase);
        // DATEDIFF(part, start, end) → DATEDIFF(end, start)  [MariaDB DATEDIFF is days-only]
        sql = Regex.Replace(sql,
            @"\bDATEDIFF\s*\(\s*\w+\s*,\s*([^,]+),\s*([^)]+)\)",
            "DATEDIFF($2, $1)", RegexOptions.IgnoreCase);
        // DATEPART(part, date) → EXTRACT(part FROM date)
        sql = Regex.Replace(sql,
            @"\bDATEPART\s*\(\s*(\w+)\s*,\s*([^)]+)\)",
            "EXTRACT($1 FROM $2)", RegexOptions.IgnoreCase);
        return sql;
    }

    private static string ConvertSchemaPrefixes(string sql)
    {
        // [dbo].TableName → TableName  (bracketed schema prefix — must run before bracket conversion)
        sql = Regex.Replace(sql, @"\[dbo\]\s*\.\s*", string.Empty, RegexOptions.IgnoreCase);
        // dbo.TableName → TableName  (bare schema prefix)
        sql = Regex.Replace(sql, @"\bdbo\s*\.\s*", string.Empty, RegexOptions.IgnoreCase);
        return sql;
    }

    private static string ConvertGoStatements(string sql)
    {
        // GO batch separator — remove entirely (MariaDB doesn't use GO)
        sql = Regex.Replace(sql, @"^\s*GO\s*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return sql;
    }

    private static string ConvertSetStatements(string sql)
    {
        sql = Regex.Replace(sql,
            @"^\s*SET\s+NOCOUNT\s+(ON|OFF)\s*;?\s*$",
            string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql,
            @"^\s*SET\s+XACT_ABORT\s+(ON|OFF)\s*;?\s*$",
            string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return sql;
    }

    private static string ConvertNVarcharLiterals(string sql)
    {
        // N'string' → 'string'
        return Regex.Replace(sql, @"\bN'", "'");
    }

    private static string ConvertTopToLimit(string sql)
    {
        // SELECT TOP n … → SELECT … LIMIT n  (simple single-table cases)
        // This is a best-effort transform; complex queries may need manual review.
        // Best-effort: strip TOP n (LIMIT must be added manually at end of query)
        return Regex.Replace(sql,
            @"\bSELECT\s+TOP\s+(\d+)\b",
            "SELECT",
            RegexOptions.IgnoreCase);
    }

    private static string ConvertIfExists(string sql)
    {
        // IF EXISTS (SELECT …) DROP TABLE → DROP TABLE IF EXISTS
        sql = Regex.Replace(sql,
            @"IF\s+EXISTS\s*\([^)]*\)\s*(DROP\s+TABLE\s+(?:`[^`]+`|\[?\w+\]?))",
            "$1 IF EXISTS", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // IF OBJECT_ID('x') IS NOT NULL DROP TABLE x → DROP TABLE IF EXISTS x
        sql = Regex.Replace(sql,
            @"IF\s+OBJECT_ID\s*\(\s*N?'([^']+)'\s*(?:,\s*N?'[^']*')?\s*\)\s+IS\s+NOT\s+NULL\s+(DROP\s+(?:TABLE|PROCEDURE|VIEW|FUNCTION)\s+\S+)",
            "$2", RegexOptions.IgnoreCase);

        return sql;
    }

    private static string ConvertWithNolock(string sql)
    {
        // WITH (NOLOCK) is a MSSQL hint — remove entirely (not supported by MariaDB)
        return Regex.Replace(sql, @"\bWITH\s*\(\s*NOLOCK\s*\)", string.Empty,
            RegexOptions.IgnoreCase);
    }

    private static string FixTrailingCommasBeforeCloseParen(string sql)
    {
        // Remove trailing commas before ) that sometimes arise from stripping columns
        return Regex.Replace(sql, @",\s*\)", ")");
    }

    // Known SQL data-type keywords that must NOT be backtick-quoted
    private static readonly HashSet<string> SqlTypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIGINT","INT","INTEGER","SMALLINT","TINYINT","BIT","BOOL","BOOLEAN",
        "DECIMAL","NUMERIC","FLOAT","DOUBLE","REAL",
        "CHAR","VARCHAR","NCHAR","NVARCHAR","TEXT","LONGTEXT","MEDIUMTEXT","TINYTEXT",
        "BLOB","LONGBLOB","MEDIUMBLOB","TINYBLOB","VARBINARY","BINARY",
        "DATE","DATETIME","TIMESTAMP","TIME","YEAR",
        "GEOMETRY","JSON","UUID",
        "AUTO_INCREMENT",
        // MSSQL-only types that appear bracket-quoted in source and need unquoting before ConvertDatatypes
        "MONEY","SMALLMONEY","NTEXT","IMAGE","UNIQUEIDENTIFIER",
        "SMALLDATETIME","DATETIME2","DATETIMEOFFSET","XML",
        "HIERARCHYID","GEOGRAPHY","ROWVERSION","SQL_VARIANT",
    };

    /// <summary>
    /// After bracket-to-backtick conversion, data-type names that were originally
    /// bracket-quoted end up as `VARCHAR`, `DECIMAL`, etc.  Strip those backticks
    /// so the final SQL contains bare type keywords.
    /// Also handles residual [TYPE(size)] patterns that the bracket regex missed
    /// because the closing ] came after the parenthetical.
    /// </summary>
    private static string UnquoteDataTypes(string sql)
    {
        // Pass 1: `TYPE(size)` → TYPE(size)   e.g. `DECIMAL(19,4)` → DECIMAL(19,4)
        // Safe: a backtick-quoted word immediately followed by (...) can only be a type-with-size,
        // never a table or column name in CREATE TABLE column-definition syntax.
        sql = Regex.Replace(sql, @"`([A-Za-z_]\w*)\(([^)]*)\)`",
            m => SqlTypeKeywords.Contains(m.Groups[1].Value)
                ? $"{m.Groups[1].Value}({m.Groups[2].Value})"
                : m.Value);

        // Pass 2: unquote `TYPE` only when it appears in a column-definition type position,
        // i.e. immediately after a backtick-quoted identifier + whitespace (the column name).
        // Pattern: `colname` `TYPE`  →  `colname` TYPE
        // This avoids unquoting table names like `TEXT` in CREATE TABLE `TEXT`(
        sql = Regex.Replace(sql,
            @"(`[^`]+`)\s+`([A-Za-z_]\w*)`",
            m => SqlTypeKeywords.Contains(m.Groups[2].Value)
                ? $"{m.Groups[1].Value} {m.Groups[2].Value}"
                : m.Value);

        return sql;
    }

    /// <summary>
    /// Ensures every top-level DDL/DML statement ends with a semicolon.
    /// Adds ';' after the closing ')' of CREATE TABLE blocks and after
    /// standalone INSERT/UPDATE/DELETE/DROP/ALTER statements.
    /// </summary>
    private static string EnsureStatementSemicolons(string sql)
    {
        // For CREATE TABLE ... ) blocks: add ; after the final closing paren if absent
        sql = Regex.Replace(sql,
            @"(\)\s*)(\r?\n\s*\r?\n|\r?\n\s*(CREATE|INSERT|UPDATE|DELETE|DROP|ALTER|SELECT)\b)",
            m => m.Groups[1].Value.TrimEnd().EndsWith(";")
                ? m.Value
                : m.Groups[1].Value.TrimEnd() + ";\r\n\r\n" + m.Groups[2].Value.TrimStart('\r', '\n', ' '),
            RegexOptions.IgnoreCase);

        // Ensure the very last statement ends with ;
        sql = Regex.Replace(sql, @"(\))(\s*)$",
            m => m.Groups[1].Value + ";" + m.Groups[2].Value);

        return sql;
    }

    /// <summary>
    /// Prepends DROP TABLE IF EXISTS `tbl`; before each CREATE TABLE `tbl`( block.
    /// Used when the user selects "Overwrite" for the IF EXISTS TABLES option.
    /// </summary>
    private static string AddDropTableIfExists(string sql)
    {
        return Regex.Replace(sql,
            @"(CREATE\s+TABLE\s+(`[^`]+`)\s*\()",
            "DROP TABLE IF EXISTS $2;\r\n$1",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Converts plain INSERT INTO … to INSERT IGNORE INTO … so duplicate-key rows
    /// are skipped rather than causing an error.
    /// Used when the user selects "Skip" or "Overwrite" for the IF EXISTS RECORDS option.
    /// </summary>
    private static string ConvertInsertToInsertIgnore(string sql)
    {
        return Regex.Replace(sql,
            @"\bINSERT\s+INTO\b",
            "INSERT IGNORE INTO",
            RegexOptions.IgnoreCase);
    }
}
