using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Dj_MsSql2Maria;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private string? _currentLogFile;

    public MainWindow()
    {
        InitializeComponent();
    }

    // -- UI helpers ----------------------------------------------------------

    private bool IsBakMode =>
        CmbInputType.SelectedItem is ComboBoxItem item &&
        item.Content?.ToString()?.Contains(".BAK") == true;

    private bool IsCsvMode =>
        CmbInputType.SelectedItem is ComboBoxItem csvItem &&
        csvItem.Content?.ToString()?.Contains(".CSV") == true;

    private void SetBusy(bool busy)
    {
        BtnGo.IsEnabled    = !busy;
        BtnClear.IsEnabled = !busy;
        BtnExit.IsEnabled  = !busy;
        BtnStop.IsEnabled  =  busy;
        CmbInputType.IsEnabled = !busy;
        // VIEW LOG is enabled once a log file exists (set before SetBusy(true) is called)
        BtnViewLog.IsEnabled = !busy && _currentLogFile is not null && File.Exists(_currentLogFile);
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLog.AppendText(message + Environment.NewLine);
            TxtLog.ScrollToEnd();
        });
    }

    private void SetProgress(double value) =>
        Dispatcher.Invoke(() => PrgBar.Value = value);

    /// <summary>Appends a line to the green-on-black status box and auto-scrolls to the bottom.</summary>
    private void Status(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.AppendText(message + Environment.NewLine);
            // Scroll the containing ScrollViewer to the bottom so the newest line is always visible.
            StatusScroller.ScrollToBottom();
        });
    }

    /// <summary>Writes a timestamped line to both the log panel and the current log file.</summary>
    private void AppLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Log(line);
        if (_currentLogFile is not null)
        {
            try { File.AppendAllText(_currentLogFile, line + Environment.NewLine); }
            catch { /* best-effort file write */ }
        }
    }

    /// <summary>Opens the current log file in Notepad.</summary>
    private void BtnViewLog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLogFile is null || !File.Exists(_currentLogFile)) return;

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_currentLogFile}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open log file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -- Event handlers -------------------------------------------------------

    private void CmbInputType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        bool bak = IsBakMode;
        bool csv = IsCsvMode;
        // Both BAK and CSV share the tables/data options panel
        PnlBakOptions.Visibility = (bak || csv) ? Visibility.Visible  : Visibility.Collapsed;
        PnlCsvOptions.Visibility = csv          ? Visibility.Visible  : Visibility.Collapsed;

        // Reset path when mode changes
        TxtInputPath.Text = string.Empty;
    }

    private void ChkAppendSuffix_Changed(object sender, RoutedEventArgs e)
    {
        if (TxtSuffix is not null)
            TxtSuffix.IsEnabled = ChkAppendSuffix.IsChecked == true;
    }

    private void ChkIfExistsTables_Changed(object sender, RoutedEventArgs e)
    {
        if (CmbIfExistsTablesAction is not null)
            CmbIfExistsTablesAction.IsEnabled = ChkIfExistsTables.IsChecked == true;
    }

    private void ChkIfExistsRecords_Changed(object sender, RoutedEventArgs e)
    {
        if (CmbIfExistsRecordsAction is not null)
            CmbIfExistsRecordsAction.IsEnabled = ChkIfExistsRecords.IsChecked == true;
    }

    private void BtnBrowseInput_Click(object sender, RoutedEventArgs e)
    {
        if (IsBakMode)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select SQL Server BAK file",
                Filter = "Backup files (*.bak)|*.bak|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtInputPath.Text = dlg.FileName;
        }
        else if (IsCsvMode)
        {
            var dlg = new OpenFileDialog
            {
                Title       = "Select CSV file(s)",
                Filter      = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
                TxtInputPath.Text = string.Join(";", dlg.FileNames);
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title       = "Select SQL file(s)",
                Filter      = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
                TxtInputPath.Text = string.Join(";", dlg.FileNames);
        }
    }

    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select output folder",
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(TxtOutputFolder.Text))
            dlg.InitialDirectory = TxtOutputFolder.Text;

        if (dlg.ShowDialog() == true)
            TxtOutputFolder.Text = dlg.FolderName;
    }

    private async void BtnGo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtInputPath.Text))
        {
            MessageBox.Show("Please select an input file or files.", "Input required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtOutputFolder.Text))
        {
            MessageBox.Show("Please select an output folder.", "Output required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string dbName = TxtDatabaseName.Text.Trim();
        if (string.IsNullOrWhiteSpace(dbName))
        {
            MessageBox.Show("Please enter a Target Database Name.\n\nThis is used to generate CREATE DATABASE and USE statements at the top of the output SQL.",
                "Database name required", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtDatabaseName.Focus();
            return;
        }

        string suffix = ChkAppendSuffix.IsChecked == true
            ? TxtSuffix.Text.Trim()
            : string.Empty;

        string[] inputFiles = TxtInputPath.Text.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string outputFolder = TxtOutputFolder.Text;

        // Read Options controls
        string fileExistsAction    = (CmbFileExistsAction.SelectedItem   as ComboBoxItem)?.Content?.ToString() ?? "Ask";
        bool   ifExistsTables      = ChkIfExistsTables.IsChecked == true;
        string tablesAction        = ifExistsTables
            ? (CmbIfExistsTablesAction.SelectedItem  as ComboBoxItem)?.Content?.ToString() ?? "Ask"
            : "None";
        bool   ifExistsRecords     = ChkIfExistsRecords.IsChecked == true;
        string recordsAction       = ifExistsRecords
            ? (CmbIfExistsRecordsAction.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Ask"
            : "None";

        // Build ConversionOptions from selections:
        //   Tables "Overwrite" ? DROP TABLE IF EXISTS prepended before CREATE TABLE
        //   Records "Overwrite" or "Skip" ? INSERT IGNORE (skip dups without error)
        var conversionOptions = new ConversionOptions(
            DropTableIfExists: ifExistsTables  && tablesAction  == "Overwrite",
            InsertIgnore:      ifExistsRecords && (recordsAction == "Overwrite" || recordsAction == "Skip")
        );

        // Create a new timestamped log file for this run
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string exeFolder = AppContext.BaseDirectory;
        _currentLogFile = Path.Combine(exeFolder, $"Dj-MsSql2Maria_{timestamp}.log");
        BtnViewLog.IsEnabled = false;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        TxtLog.Clear();
        TxtStatus.Clear();
        PrgBar.Value = 0;

        try
        {
            AppLog($"Dj-MsSql2Maria started — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            AppLog($"Mode: {(CmbInputType.SelectedItem as ComboBoxItem)?.Content}");
            AppLog($"Output folder: {outputFolder}");
            AppLog($"File exists action: {fileExistsAction}");
            AppLog($"IF EXISTS tables: {(ifExistsTables ? tablesAction : "disabled")}  |  IF EXISTS records: {(ifExistsRecords ? recordsAction : "disabled")}");
            Status("Initialising conversion…");

            if (IsBakMode)
            {
                bool tables            = ChkBakTables.IsChecked          == true;
                bool data              = ChkBakData.IsChecked             == true;
                bool consolidateTables = ChkBakTablesIndividual.IsChecked == true;
                bool consolidateData   = ChkBakDataIndividual.IsChecked   == true;
                AppLog($"BAK file: {inputFiles[0]}");
                AppLog($"Include tables: {tables}  |  Include data: {data}");
                await RunBakConversionAsync(inputFiles[0], outputFolder, suffix, dbName,
                    tables, data, consolidateTables, consolidateData,
                    fileExistsAction, conversionOptions, _cts.Token);
            }
            else if (IsCsvMode)
            {
                bool tables            = ChkBakTables.IsChecked          == true;
                bool data              = ChkBakData.IsChecked             == true;
                bool consolidateTables = ChkBakTablesIndividual.IsChecked == true;
                bool consolidateData   = ChkBakDataIndividual.IsChecked   == true;
                AppLog($"CSV file(s): {string.Join(", ", inputFiles)}");
                AppLog($"Include tables: {tables}  |  Include data: {data}");
                AppLog($"Consolidate tables: {consolidateTables}  |  Consolidate data: {consolidateData}");
                await RunCsvConversionAsync(inputFiles, outputFolder, suffix, dbName,
                    tables, data, consolidateTables, consolidateData,
                    fileExistsAction, conversionOptions, _cts.Token);
            }
            else
            {
                AppLog($"Input file(s): {string.Join(", ", inputFiles)}");
                await RunSqlConversionAsync(inputFiles, outputFolder, suffix, dbName,
                    fileExistsAction, conversionOptions, _cts.Token);
            }

            AppLog("Run completed successfully.");
        }
        catch (OperationCanceledException)
        {
            AppLog("Operation cancelled by user.");
            Status("?  Cancelled.");
        }
        catch (Exception ex)
        {
            AppLog($"FATAL ERROR: {ex}");
            Status($"ERROR: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            // Enable VIEW LOG now that the run is finished and the log file exists
            BtnViewLog.IsEnabled = _currentLogFile is not null && File.Exists(_currentLogFile);
            _cts.Dispose();
            _cts = null;
            SetProgress(100);
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        BtnStop.IsEnabled = false;
        AppLog("Stop requested by user.");
        Status("Stop requested — finishing current file…");
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        TxtInputPath.Text    = string.Empty;
        TxtOutputFolder.Text = string.Empty;
        TxtLog.Clear();
        TxtStatus.Clear();
        PrgBar.Value         = 0;
        ChkAppendSuffix.IsChecked = false;
        TxtSuffix.Text       = "_MariaDb";
        ChkBakTables.IsChecked = true;
        ChkBakData.IsChecked   = true;
        CmbInputType.SelectedIndex = 0;
        _currentLogFile = null;
        BtnViewLog.IsEnabled = false;
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e) => Close();

    // -- Conversion runners ---------------------------------------------------

    private async Task RunSqlConversionAsync(
        string[] inputFiles, string outputFolder, string suffix, string dbName,
        string fileExistsAction, ConversionOptions options,
        CancellationToken ct)
    {
        AppLog($"SQL conversion starting — {inputFiles.Length} file(s).");
        Status($"Converting {inputFiles.Length} file(s)…");

        string baseName = inputFiles.Length == 1
            ? Path.GetFileNameWithoutExtension(inputFiles[0])
            : "output";

        string outFile = Path.Combine(outputFolder, baseName + suffix + ".sql");
        AppLog($"Output file: {outFile}");

        if (File.Exists(outFile))
        {
            bool proceed = await HandleFileExistsAsync(outFile, fileExistsAction, ct);
            if (!proceed) return;
        }

        await using var writer = new StreamWriter(outFile, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // SQL input files contain CREATE TABLE statements, so include CREATE DATABASE.
        await writer.WriteLineAsync($"CREATE DATABASE IF NOT EXISTS `{dbName}`;");
        await writer.WriteLineAsync($"USE `{dbName}`;");
        await writer.WriteLineAsync();

        for (int i = 0; i < inputFiles.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string path = inputFiles[i];
            string fileName = Path.GetFileName(path);

            Status($"[{i + 1}/{inputFiles.Length}] Reading: {fileName}");
            AppLog($"  Reading file: {path}");

            string sql = await File.ReadAllTextAsync(path, ct);
            AppLog($"  File read — {sql.Length:N0} chars. Applying conversions…");
            Status($"[{i + 1}/{inputFiles.Length}] Converting: {fileName}");

            string converted = SqlConverter.ConvertToMariaDb(sql, options);
            AppLog($"  Conversion complete — {converted.Length:N0} chars. Writing output…");

            await writer.WriteLineAsync(converted);
            await writer.WriteLineAsync();

            double pct = (i + 1) * 100.0 / inputFiles.Length;
            SetProgress(pct);
            Status($"[{i + 1}/{inputFiles.Length}] Done: {fileName}  ({pct:F0}%)");
            AppLog($"  Wrote converted output for: {fileName}");
        }

        AppLog($"All files written to: {outFile}");
        Status($"?  Complete — {outFile}");
        Log($"?  Done. Output: {outFile}");
    }

    private async Task RunBakConversionAsync(
        string bakFile, string outputFolder, string suffix, string dbName,
        bool includeTables, bool includeData,
        bool consolidateTables, bool consolidateData,
        string fileExistsAction, ConversionOptions options,
        CancellationToken ct)
    {
        string bakName = Path.GetFileName(bakFile);
        AppLog($"BAK conversion starting: {bakFile}");
        AppLog($"Include tables: {includeTables}  |  Consolidate tables: {consolidateTables}");
        AppLog($"Include data:   {includeData}    |  Consolidate data:   {consolidateData}");
        Status($"Scanning BAK file: {bakName}");
        Log($"Extracting from BAK: {bakName}");

        string baseName = Path.GetFileNameWithoutExtension(bakFile);

        SetProgress(10);
        AppLog("Scanning BAK byte stream for embedded SQL text segments…");
        Status("Scanning BAK file for embedded SQL text…");
        Log("  Scanning BAK file for embedded SQL text…");

        var segments = await Task.Run(
            () => SqlConverter.ExtractSqlFromBak(bakFile, includeTables, includeData, ct), ct);

        SetProgress(60);
        AppLog($"Scan complete — {segments.Count} SQL segment(s) found.");
        Status($"Found {segments.Count} segment(s). Converting…");
        Log($"  Found {segments.Count} segment(s). Converting…");

        var tableSegments = segments.Where(s => s.Type == BakSegmentType.Table).ToList();
        var dataSegments  = segments.Where(s => s.Type == BakSegmentType.Data).ToList();

        int total = segments.Count;
        int done  = 0;

        async Task WriteSegmentsAsync(
            List<BakSegment> segs, string typeSuffix, bool consolidate, bool isTableScript)
        {
            if (segs.Count == 0) return;

            if (consolidate)
            {
                string outFile = Path.Combine(outputFolder,
                    $"{baseName}{suffix}_{typeSuffix}.sql");
                AppLog($"Consolidated output: {outFile}");

                if (File.Exists(outFile))
                {
                    bool proceed = await HandleFileExistsAsync(outFile, fileExistsAction, ct);
                    if (!proceed) return;
                }

                await using var writer = new StreamWriter(outFile, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (isTableScript)
                    await writer.WriteLineAsync($"CREATE DATABASE IF NOT EXISTS `{dbName}`;");
                await writer.WriteLineAsync($"USE `{dbName}`;");
                await writer.WriteLineAsync();

                foreach (var seg in segs)
                {
                    ct.ThrowIfCancellationRequested();
                    done++;
                    double pct = 60 + done * 40.0 / Math.Max(total, 1);
                    AppLog($"  Converting {typeSuffix} segment {done}/{total}: {seg.TableName}…");
                    Status($"Converting {typeSuffix} {done}/{total}: {seg.TableName}  ({pct:F0}%)");
                    string converted = SqlConverter.ConvertToMariaDb(seg.Sql, options);
                    await writer.WriteLineAsync(converted);
                    await writer.WriteLineAsync();
                    SetProgress(pct);
                }

                Status($"?  {typeSuffix} ? {outFile}");
                Log($"?  {typeSuffix} done. Output: {outFile}");
            }
            else
            {
                foreach (var seg in segs)
                {
                    ct.ThrowIfCancellationRequested();
                    done++;
                    double pct = 60 + done * 40.0 / Math.Max(total, 1);
                    string safeTable = string.Concat(
                        seg.TableName.Split(Path.GetInvalidFileNameChars()));
                    string outFile = Path.Combine(outputFolder,
                        $"{baseName}_{safeTable}_{typeSuffix}{suffix}.sql");

                    AppLog($"  Writing individual file: {outFile}");
                    Status($"Writing {typeSuffix} file {done}/{total}: {seg.TableName}  ({pct:F0}%)");

                    if (File.Exists(outFile))
                    {
                        bool proceed = await HandleFileExistsAsync(outFile, fileExistsAction, ct);
                        if (!proceed) continue;
                    }

                    await using var writer = new StreamWriter(outFile, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    if (isTableScript)
                        await writer.WriteLineAsync($"CREATE DATABASE IF NOT EXISTS `{dbName}`;");
                    await writer.WriteLineAsync($"USE `{dbName}`;");
                    await writer.WriteLineAsync();
                    string converted = SqlConverter.ConvertToMariaDb(seg.Sql, options);
                    await writer.WriteLineAsync(converted);
                    SetProgress(pct);
                }

                Status($"?  {segs.Count} individual {typeSuffix} file(s) written.");
                Log($"?  {typeSuffix} done. {segs.Count} individual file(s) in: {outputFolder}");
            }
        }

        await WriteSegmentsAsync(tableSegments, "tables", consolidateTables, isTableScript: true);
        await WriteSegmentsAsync(dataSegments,  "data",   consolidateData,   isTableScript: false);

        SetProgress(100);
        AppLog("BAK conversion complete.");
        Status($"?  Complete — output in: {outputFolder}");
    }

    // -- File-exists handler --------------------------------------------------

    /// <summary>
    /// Converts a set of CSV files to MariaDB SQL output files, respecting the
    /// user's table/data and consolidation choices.
    /// </summary>
    private async Task RunCsvConversionAsync(
        string[] inputFiles, string outputFolder, string suffix, string dbName,
        bool includeTables, bool includeData,
        bool consolidateTables, bool consolidateData,
        string fileExistsAction, ConversionOptions options,
        CancellationToken ct)
    {
        if (!includeTables && !includeData)
        {
            AppLog("CSV conversion skipped — no output type selected (Tables and Data both unchecked).");
            Status("Nothing to do — enable Tables and/or Data.");
            return;
        }

        AppLog($"CSV conversion starting — {inputFiles.Length} file(s).");
        Status($"Converting {inputFiles.Length} CSV file(s)…");

        var enc = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        // Headers: CREATE DATABASE only belongs in scripts that create tables.
        string tableHeader = $"CREATE DATABASE IF NOT EXISTS `{dbName}`;\r\nUSE `{dbName}`;\r\n\r\n";
        string dataHeader  = $"USE `{dbName}`;\r\n\r\n";

        // Collect per-file results first (one Task.Run per file)
        var results = new List<(string TableName, string CreateSql, string DataSql)>();
        for (int i = 0; i < inputFiles.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string path     = inputFiles[i];
            string fileName = Path.GetFileName(path);
            double pct      = (i + 1) * 100.0 / inputFiles.Length;

            Status($"[{i + 1}/{inputFiles.Length}] Parsing: {fileName}");
            AppLog($"  Parsing CSV: {path}");

            var (createSql, dataSql) = await Task.Run(
                () => SqlConverter.CsvToMariaDb(path, options, ct), ct);

            string tableName = Path.GetFileNameWithoutExtension(path);
            results.Add((tableName, createSql, dataSql));
            SetProgress(pct * 0.6); // first 60% is parsing
            AppLog($"  Parsed: {fileName}");
        }

        // ?? Single CSV: write one combined output file named after the source ??
        if (inputFiles.Length == 1)
        {
            string tableName  = results[0].TableName;
            string outFile    = Path.Combine(outputFolder, $"{tableName}{suffix}.sql");
            bool   writeOut   = true;

            if (File.Exists(outFile))
            {
                bool proceed = await HandleFileExistsAsync(outFile, fileExistsAction, ct);
                if (!proceed) { AppLog($"Skipped (file exists): {outFile}"); writeOut = false; }
            }

            if (writeOut)
            {
                await using var w = new StreamWriter(outFile, append: false, enc);
                // Use CREATE DATABASE header when tables are included; USE-only when data-only.
                await w.WriteAsync(includeTables ? tableHeader : dataHeader);
                if (includeTables)
                {
                    await w.WriteLineAsync(results[0].CreateSql);
                    await w.WriteLineAsync();
                }
                if (includeData)
                {
                    await w.WriteLineAsync(results[0].DataSql);
                }
                AppLog($"Output file: {outFile}");
            }

            SetProgress(100);
            AppLog("CSV conversion complete.");
            Status($"?  Complete — {Path.GetFileName(outFile)}");
            Log($"?  Done. Output in: {outputFolder}");
            return;
        }

        SetProgress(65);

        // ?? Multiple CSVs — Tables output ?????????????????????????????????????
        if (includeTables)
        {
            if (consolidateTables)
            {
                // Single consolidated tables file
                string tablesFile = Path.Combine(outputFolder, $"_CreateTables{suffix}.sql");
                bool writeTables = true;
                if (File.Exists(tablesFile))
                {
                    bool proceed = await HandleFileExistsAsync(tablesFile, fileExistsAction, ct);
                    if (!proceed) { AppLog("Skipped tables file (already exists)."); writeTables = false; }
                }
                if (writeTables)
                {
                    await using var tw = new StreamWriter(tablesFile, append: false, enc);
                    await tw.WriteAsync(tableHeader);
                    foreach (var (_, createSql, _) in results)
                    {
                        await tw.WriteLineAsync(createSql);
                        await tw.WriteLineAsync();
                    }
                    AppLog($"Tables file: {tablesFile}");
                }
            }
            else
            {
                // One tables file per CSV
                for (int i = 0; i < results.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (tableName, createSql, _) = results[i];
                    string outFile = Path.Combine(outputFolder, $"{tableName}_CreateTable{suffix}.sql");
                    if (File.Exists(outFile))
                    {
                        bool proceed = await HandleFileExistsAsync(outFile, fileExistsAction, ct);
                        if (!proceed) { AppLog($"Skipped: {outFile}"); continue; }
                    }
                    await using var tw = new StreamWriter(outFile, append: false, enc);
                    await tw.WriteAsync(tableHeader);
                    await tw.WriteLineAsync(createSql);
                    AppLog($"  Table file: {outFile}");
                }
            }
        }

        SetProgress(80);

        // ?? Multiple CSVs — Data output ????????????????????????????????????????
        if (includeData)
        {
            if (consolidateData)
            {
                // Single consolidated data file
                string dataFile = Path.Combine(outputFolder, $"_Data{suffix}.sql");
                bool writeData = true;
                if (File.Exists(dataFile))
                {
                    bool proceed = await HandleFileExistsAsync(dataFile, fileExistsAction, ct);
                    if (!proceed) { AppLog("Skipped data file (already exists)."); writeData = false; }
                }
                if (writeData)
                {
                    await using var dw = new StreamWriter(dataFile, append: false, enc);
                    await dw.WriteAsync(dataHeader);
                    foreach (var (_, _, dataSql) in results)
                    {
                        await dw.WriteLineAsync(dataSql);
                        await dw.WriteLineAsync();
                    }
                    AppLog($"Data file: {dataFile}");
                }
            }
            else
            {
                // One data file per CSV
                for (int i = 0; i < results.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (tableName, _, dataSql) = results[i];
                    string outFile = Path.Combine(outputFolder, $"{tableName}_Data{suffix}.sql");
                    if (File.Exists(outFile))
                    {
                        bool proceed = await HandleFileExistsAsync(outFile, fileExistsAction, ct);
                        if (!proceed) { AppLog($"Skipped: {outFile}"); continue; }
                    }
                    await using var dw = new StreamWriter(outFile, append: false, enc);
                    await dw.WriteAsync(dataHeader);
                    await dw.WriteLineAsync(dataSql);
                    AppLog($"  Data file: {outFile}");
                }
            }
        }

        SetProgress(100);
        AppLog("CSV conversion complete.");
        Status($"?  Complete — output in: {outputFolder}");
        Log($"?  Done. Output in: {outputFolder}");
    }

    // -- File-exists handler --------------------------------------------------

    /// <summary>
    /// Applies the user-selected "Action if output file exists" policy.
    /// Returns true if the caller should proceed (overwrite), false to skip.
    /// Throws <see cref="OperationCanceledException"/> on "Error &amp; Exit".
    /// </summary>
    private Task<bool> HandleFileExistsAsync(string filePath, string action, CancellationToken ct)
    {
        switch (action)
        {
            case "Overwrite":
                AppLog($"  Output file exists — overwriting: {filePath}");
                return Task.FromResult(true);

            case "Skip":
                AppLog($"  Output file exists — skipping: {filePath}");
                Status($"Skipped (file exists): {Path.GetFileName(filePath)}");
                return Task.FromResult(false);

            case "Error & Exit":
                throw new InvalidOperationException(
                    $"Output file already exists and action is set to 'Error & Exit':\n{filePath}");

            default: // "Ask"
            {
                var result = MessageBox.Show(
                    $"Output file already exists:\n{filePath}\n\nOverwrite it?",
                    "File exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                bool proceed = result == MessageBoxResult.Yes;
                AppLog($"  Output file exists — user chose {(proceed ? "overwrite" : "skip")}: {filePath}");
                return Task.FromResult(proceed);
            }
        }
    }
}
