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

    private bool IsMultiMode =>
        CmbInputType.SelectedItem is ComboBoxItem item &&
        item.Content?.ToString()?.Contains("Multiple") == true;

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
        PnlBakOptions.Visibility = bak ? Visibility.Visible : Visibility.Collapsed;

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
        else if (IsMultiMode)
        {
            var dlg = new OpenFileDialog
            {
                Title     = "Select SQL files",
                Filter    = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
                TxtInputPath.Text = string.Join(";", dlg.FileNames);
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select SQL file",
                Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtInputPath.Text = dlg.FileName;
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

        string suffix = ChkAppendSuffix.IsChecked == true
            ? TxtSuffix.Text.Trim()
            : string.Empty;

        string[] inputFiles = TxtInputPath.Text.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string outputFolder = TxtOutputFolder.Text;

        // Create a new timestamped log file for this run
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _currentLogFile = Path.Combine(outputFolder, $"Dj-MsSql2Maria_{timestamp}.log");
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
            Status("Initialising conversion…");

            if (IsBakMode)
            {
                bool tables            = ChkBakTables.IsChecked          == true;
                bool data              = ChkBakData.IsChecked             == true;
                bool consolidateTables = ChkBakTablesIndividual.IsChecked == true;
                bool consolidateData   = ChkBakDataIndividual.IsChecked   == true;
                AppLog($"BAK file: {inputFiles[0]}");
                AppLog($"Include tables: {tables}  |  Include data: {data}");
                await RunBakConversionAsync(inputFiles[0], outputFolder, suffix,
                    tables, data, consolidateTables, consolidateData, _cts.Token);
            }
            else
            {
                AppLog($"Input file(s): {string.Join(", ", inputFiles)}");
                await RunSqlConversionAsync(inputFiles, outputFolder, suffix, _cts.Token);
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
        string[] inputFiles, string outputFolder, string suffix,
        CancellationToken ct)
    {
        AppLog($"SQL conversion starting — {inputFiles.Length} file(s).");
        Status($"Converting {inputFiles.Length} file(s)…");

        string baseName = inputFiles.Length == 1
            ? Path.GetFileNameWithoutExtension(inputFiles[0])
            : "output";

        string outFile = Path.Combine(outputFolder, baseName + suffix + ".sql");
        AppLog($"Output file: {outFile}");

        await using var writer = new StreamWriter(outFile, append: false, System.Text.Encoding.UTF8);
        await writer.WriteLineAsync($"-- Generated by Dj-MsSql2Maria on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync($"-- Source: {string.Join(", ", inputFiles)}");
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

            string converted = SqlConverter.ConvertToMariaDb(sql);
            AppLog($"  Conversion complete — {converted.Length:N0} chars. Writing output…");

            await writer.WriteLineAsync($"-- ===== Source: {fileName} =====");
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
        string bakFile, string outputFolder, string suffix,
        bool includeTables, bool includeData,
        bool consolidateTables, bool consolidateData,
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
            List<BakSegment> segs, string typeSuffix, bool consolidate)
        {
            if (segs.Count == 0) return;

            if (consolidate)
            {
                string outFile = Path.Combine(outputFolder,
                    $"{baseName}{suffix}_{typeSuffix}.sql");
                AppLog($"Consolidated output: {outFile}");

                await using var writer = new StreamWriter(outFile, append: false, System.Text.Encoding.UTF8);
                await writer.WriteLineAsync(
                    $"-- Generated by Dj-MsSql2Maria on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                await writer.WriteLineAsync($"-- Source BAK: {bakFile}  [{typeSuffix}]");
                await writer.WriteLineAsync();

                foreach (var seg in segs)
                {
                    ct.ThrowIfCancellationRequested();
                    done++;
                    double pct = 60 + done * 40.0 / Math.Max(total, 1);
                    AppLog($"  Converting {typeSuffix} segment {done}/{total}: {seg.TableName}…");
                    Status($"Converting {typeSuffix} {done}/{total}: {seg.TableName}  ({pct:F0}%)");
                    string converted = SqlConverter.ConvertToMariaDb(seg.Sql);
                    await writer.WriteLineAsync($"-- ===== Table: {seg.TableName} =====");
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

                    await using var writer = new StreamWriter(outFile, append: false, System.Text.Encoding.UTF8);
                    await writer.WriteLineAsync(
                        $"-- Generated by Dj-MsSql2Maria on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    await writer.WriteLineAsync($"-- Source BAK: {bakFile}");
                    await writer.WriteLineAsync($"-- Table: {seg.TableName}  [{typeSuffix}]");
                    await writer.WriteLineAsync();
                    string converted = SqlConverter.ConvertToMariaDb(seg.Sql);
                    await writer.WriteLineAsync(converted);
                    SetProgress(pct);
                }

                Status($"?  {segs.Count} individual {typeSuffix} file(s) written.");
                Log($"?  {typeSuffix} done. {segs.Count} individual file(s) in: {outputFolder}");
            }
        }

        await WriteSegmentsAsync(tableSegments, "tables", consolidateTables);
        await WriteSegmentsAsync(dataSegments,  "data",   consolidateData);

        SetProgress(100);
        AppLog("BAK conversion complete.");
        Status($"?  Complete — output in: {outputFolder}");
    }
}
