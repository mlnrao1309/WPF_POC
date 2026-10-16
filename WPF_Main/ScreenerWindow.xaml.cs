using AlgoTrading.Screener.Dsl;
using AlgoTrading.Screener.Repositories;
using AlgoTrading.Screener.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlgoTrading.Screener.Wpf;

public sealed partial class ScreenerWindow : Window
{
    private readonly IServiceScopeFactory _scopeFactory;

    // Fires when the window is closing; every in-flight run is linked to it.
    private readonly CancellationTokenSource _windowLifetime = new();

    private CancellationTokenSource? _cts;
    private volatile bool _closing;
    private bool _running;
    private const string AllOption = "(All)";
    private readonly IQueryStore _queryStore;
    private SavedQuery? _currentQuery;   // the selected saved query, if any
                                         //public ScreenerWindow(IServiceScopeFactory scopeFactory)
                                         //{
                                         //    _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    //    InitializeComponent();

    //    // Sensible defaults so the very first Run works.
    //    FromBox.SelectedDate = DateTime.UtcNow.AddDays(-400);
    //    ToBox.SelectedDate = DateTime.UtcNow;
    //    Loaded += async (_, _) => await LoadFilterOptionsAsync();
    //    QueryBox.RunRequested += async (_, _) => await RunAsync();
    //}

    private readonly ISettingsStore _settingsStore;
    private readonly ScreenerExecutionPlanBuilder _planBuilder;
    private readonly ScreenerPlanStore _planStore;
    public ScreenerWindow(
    IServiceScopeFactory scopeFactory,
    IQueryStore queryStore,
    ISettingsStore settingsStore,
    ScreenerExecutionPlanBuilder planBuilder,
    ScreenerPlanStore planStore)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _queryStore = queryStore ?? throw new ArgumentNullException(nameof(queryStore));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _planBuilder = planBuilder ?? throw new ArgumentNullException(nameof(planBuilder));
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));

        InitializeComponent();

        QueryBox.RunRequested += async (_, _) => await RunAsync();
        Closing += OnClosing;
        Closed += OnClosed;

        Loaded += async (_, _) =>
        {
            await LoadFilterOptionsAsync();      // 1. populate combos
            await ApplySavedSettingsAsync();     // 2. restore selections
            await ReloadSavedQueriesAsync();     // 3. populate query combo
        };
    }
    // ---------- Lifetime ----------

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs? e)
    {
        //base.OnClosing(e);
        //if (e?.Cancel == true) return;

        //_closing = true;

        //// Signal every linked token. Both Cancel calls are safe even if the
        //// token source was already disposed by a run that raced with us.
        //try { _windowLifetime.Cancel(); } catch (ObjectDisposedException) { }
        //try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _closing = true;

        try { _windowLifetime.Cancel(); } catch (ObjectDisposedException) { }
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }

        // Persist window state. Fire-and-forget: atomic write means a mid-write
        // crash leaves the previous file intact. Not awaited because the window
        // is closing and the UI thread shouldn't block on file I/O.
        _ = SaveSettingsAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _windowLifetime.Dispose();        
    }

    // ---------- Entry points ----------

    private async void RunButton_Click(object sender, RoutedEventArgs e)
        => await RunAsync();

    // ---------- Main loop ----------

    private async Task RunAsync()
    {
        if (_closing || _running) return;
        _running = true;

        CancellationTokenSource? cts = null;
        try
        {
            cts = new CancellationTokenSource();
            _cts = cts;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cts.Token, _windowLifetime.Token);
            var token = linked.Token;

            RunButton.IsEnabled = false;
            QueryBox.IsRunEnabled = false;
            ResultsGrid.ItemsSource = null;
            StatusText.Text = "Running…";
            StatusText.Foreground = Brushes.Gray;

            try
            {
                // Fresh scope per run → fresh DbContext, no shared-state races.
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ScreenerRunner>();

                var rows = await runner.RunAsync(QueryBox.Query, BuildRequest(), token);

                if (token.IsCancellationRequested || _closing) return;

                ResultsGrid.ItemsSource = ToTable(rows).DefaultView;
                StatusText.Text = $"{rows.Count} match{(rows.Count == 1 ? "" : "es")}";
                StatusText.Foreground = Brushes.Green;
            }
            catch (OperationCanceledException)
            {
                if (!_closing)
                {
                    StatusText.Text = "Cancelled";
                    StatusText.Foreground = Brushes.DarkOrange;
                }
            }
            catch (Exception ex)
            {
                if (!_closing)
                {
                    StatusText.Text = "Error";
                    StatusText.Foreground = Brushes.Red;
                    MessageBox.Show(this, ex.Message, "Screener error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                if (!_closing)
                {
                    RunButton.IsEnabled = true;
                    QueryBox.IsRunEnabled = true;
                }
            }
        }
        finally
        {
            // Clear _cts *before* disposing so any racing Cancel sees null.
            _cts = null;
            cts?.Dispose();
            _running = false;
        }
    }

    // ---------- Helpers ----------

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new WindowSettings(
                LastQuery: QueryBox.Query,
                Timeframe: ReadCombo(TimeframeBox),
                FromDate: null,
                ToDate: null,
                Segment: null,
                Exchange: null);

            await _settingsStore.SaveAsync(settings);
        }
        catch
        {
            // Non-fatal — window state is a convenience, never a correctness concern.
        }
    }
    private ScreenerRequest BuildRequest()
    {
        string? tf = ReadCombo(TimeframeBox);
        if (string.IsNullOrWhiteSpace(tf))
            throw new InvalidOperationException(
                "Timeframe not selected. Wait for filter options to load, then pick one.");

        return new ScreenerRequest
        {
            TimeFrame = tf.Trim(),
            FromInclusive = new DateTime(1753, 1, 1),
            ToExclusive = DateTime.Today.AddDays(1),
            Segment = null,
            Exchange = null
        };
    }

    private static string? ReadCombo(ComboBox box, string? fallback = null)
    {
        if (box.SelectedItem is ComboBoxItem item) return item.Content?.ToString();
        return box.SelectedItem?.ToString();
    }

    /// <summary>Flattens ScreenerRow.Values into real DataGrid columns.</summary>
    private static DataTable ToTable(IReadOnlyList<ScreenerRow> rows)
    {
        const int decimals = 4;

        static object RoundValue(object value) => value switch
        {
            decimal dec => Math.Round(dec, decimals, MidpointRounding.AwayFromZero),
            double dbl => Math.Round(dbl, decimals, MidpointRounding.AwayFromZero),
            float flt => (float)Math.Round(flt, decimals, MidpointRounding.AwayFromZero),
            _ => value
        };

        var table = new DataTable();
        if (rows.Count == 0) return table;

        // If ANY row carries an explicit projection, treat the whole result as
        // explicit. All rows come from the same query, so this is consistent.
        var projected = rows[0].ProjectedColumns;

        if (projected is { Count: > 0 })
        {
            // --- Explicit select: emit only those columns, in order. ---
            foreach (var c in projected)
                table.Columns.Add(c, typeof(object));
            foreach (var r in rows)
            {
                var row = table.NewRow();
                foreach (var c in projected)
                {
                    row[c] = r.Values.TryGetValue(c, out var v) && v is not null
                        ? RoundValue(v)
                        : (object)DBNull.Value;
                }
                table.Rows.Add(row);
            }

            return table;
        }

        // --- Default projection: fixed columns + whatever Values has. ---
        var fixedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Symbol", "Segment", "Exchange", "TimeFrame", "LastBar"
    };

        table.Columns.Add("Symbol", typeof(string));
        table.Columns.Add("Segment", typeof(string));
        table.Columns.Add("Exchange", typeof(string));
        table.Columns.Add("TimeFrame", typeof(string));
        table.Columns.Add("LastBar", typeof(DateTime));

        var dynamicCols = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
            foreach (var key in r.Values.Keys)
                if (seen.Add(key) && !fixedNames.Contains(key))
                    dynamicCols.Add(key);

        foreach (var c in dynamicCols)
            table.Columns.Add(c, typeof(object));

        foreach (var r in rows)
        {
            var row = table.NewRow();
            row["Symbol"] = r.Symbol;
            row["Segment"] = r.Segment;
            row["Exchange"] = r.Exchange;
            row["TimeFrame"] = r.TimeFrame;
            row["LastBar"] = r.LastBarTime;

            foreach (var kv in r.Values)
            {
                if (fixedNames.Contains(kv.Key)) continue;
                if (!table.Columns.Contains(kv.Key)) continue;
                row[kv.Key] = kv.Value is null
                    ? DBNull.Value
                    : RoundValue(kv.Value);
            }

            table.Rows.Add(row);
        }

        return table;
    }
    /// <summary>
    /// Rounds all numeric columns (decimal, double, float) in a DataTable to N decimal places.
    /// </summary>
    private void RoundNumericColumns(DataTable dt, int decimals)
    {
        foreach (DataColumn col in dt.Columns)
        {
            Type colType = col.DataType;

            // Check if column type is numeric
            if (colType == typeof(decimal) || colType == typeof(double) || colType == typeof(float))
            {
                foreach (DataRow row in dt.Rows)
                {
                    if (row[col] != DBNull.Value)
                    {
                        // Convert to double for rounding, then cast back to original type
                        double value = Convert.ToDouble(row[col]);
                        double roundedValue = Math.Round(value, decimals, MidpointRounding.AwayFromZero);

                        if (colType == typeof(decimal))
                            row[col] = Convert.ToDecimal(roundedValue);
                        else if (colType == typeof(double))
                            row[col] = roundedValue;
                        else if (colType == typeof(float))
                            row[col] = Convert.ToSingle(roundedValue);
                    }
                }
            }
        }
    }
    private async Task LoadFilterOptionsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScreenerRepository>();
        var options = await repo.LoadFilterOptionsAsync();

        SetOptions(TimeframeBox, options.Timeframes, preferred: null);

    }

    private static void SetOptions(ComboBox box, IReadOnlyList<string>? values, string? preferred)
    {
        box.ItemsSource = null;
        box.Items.Clear();

        IReadOnlyList<string> options = values ?? Array.Empty<string>();
        box.ItemsSource = options;
        string? selected = options.FirstOrDefault(value =>
            string.Equals(value, preferred, StringComparison.OrdinalIgnoreCase));
        box.SelectedItem = selected ?? options.FirstOrDefault();
    }
    // =====================================================================
    // Saved-query management
    // =====================================================================

    private async Task ReloadSavedQueriesAsync()
    {
        var templates = await _queryStore.GetTemplatesAsync();
        var saved = await _queryStore.GetSavedAsync();

        var items = new List<QueryComboItem>();

        if (templates.Count > 0)
        {
            items.Add(QueryComboItem.Separator("── Templates ──"));
            items.AddRange(templates.Select(t => QueryComboItem.From(t, isTemplate: true)));
        }

        if (saved.Count > 0)
        {
            items.Add(QueryComboItem.Separator("── Saved ──"));
            items.AddRange(saved.Select(s => QueryComboItem.From(s, isTemplate: false)));
        }

        SavedQueriesBox.ItemsSource = items;

        // Force nothing selected. WPF sometimes leaves the previously-selected
        // item in place when the collection is swapped; a separator would then
        // show as the "selected query" with no actual query behind it.
        SavedQueriesBox.SelectedItem = null;

        // Restore prior selection if it survives the reload.
        if (_currentQuery is not null)
        {
            var match = items.FirstOrDefault(i => i.Query?.Id == _currentQuery.Id);
            if (match is not null)
                SavedQueriesBox.SelectedItem = match;
        }
        System.Diagnostics.Debug.WriteLine($"[Screener] templates={templates.Count} saved={saved.Count}");
        System.Diagnostics.Debug.WriteLine($"[Screener] templates path: {ScreenerPaths.TemplatesFile}");
    }

    private void SavedQueriesBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedQueriesBox.SelectedItem is not QueryComboItem item || item.Query is null)
            return;

        _currentQuery = item.Query;
        QueryBox.Query = item.Query.Query;
        ScreenerNameBox.Text = item.Query.Name;
    }

    private void BrowseTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        SavedQueriesBox.Focus();
        SavedQueriesBox.IsDropDownOpen = true;
    }

    private async void BuildSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? timeframe = ReadCombo(TimeframeBox);
            if (string.IsNullOrWhiteSpace(timeframe))
                throw new InvalidOperationException("Select a timeframe before building the Screener.");
            ScreenerExecutionPlan plan = _planBuilder.Build(ScreenerNameBox.Text, QueryBox.Query, timeframe);
            string path;
            try
            {
                path = await _planStore.SaveAsync(plan, false, _windowLifetime.Token);
            }
            catch (ScreenerPlanCollisionException collision)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    "A different valid plan already exists. Replace it?\n\n" + collision.PlanPath,
                    "Replace execution plan", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                path = await _planStore.SaveAsync(plan, true, _windowLifetime.Token);
            }
            StatusText.Text = "Saved execution plan: " + Path.GetFileName(path);
            StatusText.Foreground = Brushes.Green;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusText.Text = "Build failed";
            StatusText.Foreground = Brushes.Red;
            MessageBox.Show(this, exception.Message, "Screener build error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SaveQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuery is null)
        {
            // No current selection → behave like Save As.
            await SaveAsInternalAsync();
            return;
        }

        var updated = _currentQuery with
        {
            Query = QueryBox.Query,
            // Name and Id unchanged
        };

        await _queryStore.UpsertAsync(updated);
        _currentQuery = updated;

        StatusText.Text = $"Saved '{updated.Name}'.";
        StatusText.Foreground = Brushes.Green;

        await ReloadSavedQueriesAsync();
    }

    private async void SaveAsQueryButton_Click(object sender, RoutedEventArgs e)
        => await SaveAsInternalAsync();

    private async Task SaveAsInternalAsync()
    {
        string suggested = _currentQuery?.Name ?? "New query";

        var dialog = new SaveQueryDialog(suggested) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (string.IsNullOrWhiteSpace(dialog.QueryName))
            return;

        var newQuery = SavedQuery.NewSaved(
            name: dialog.QueryName.Trim(),
            query: QueryBox.Query,
            description: null);

        await _queryStore.UpsertAsync(newQuery);
        _currentQuery = newQuery;

        StatusText.Text = $"Saved as '{newQuery.Name}'.";
        StatusText.Foreground = Brushes.Green;

        await ReloadSavedQueriesAsync();
    }

    private async void DeleteQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuery is null) return;

        // A template can never be deleted.
        if (string.Equals(_currentQuery.Category, "Template", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Templates cannot be deleted.",
                "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Delete saved query '{_currentQuery.Name}'?",
            "Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        await _queryStore.DeleteAsync(_currentQuery.Id);
        _currentQuery = null;
        QueryBox.Query = string.Empty;

        StatusText.Text = "Deleted.";
        StatusText.Foreground = Brushes.Gray;

        await ReloadSavedQueriesAsync();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(ScreenerPaths.HelpFile))
        {
            MessageBox.Show(this,
                $"Help file not found at:\n{ScreenerPaths.HelpFile}",
                "Help", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Opens in the user's default .md handler (VS Code, Typora, etc.)
        // or in the browser if nothing is registered.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ScreenerPaths.HelpFile,
            UseShellExecute = true
        });
    }

    // After a successful run, record it against the currently-selected query
    // so LastRunUtc / LastRunRowCount stay current. Add this right after the
    // successful RunAsync in your existing RunAsync method:
    //
    //     if (_currentQuery is not null)
    //         await _queryStore.RecordRunAsync(_currentQuery.Id, rows.Count);
    //
    // (Before calling this, guard against the query being a Template so we
    // don't try to write to the read-only templates file. The IQueryStore
    // implementation handles that gracefully — UpsertAsync/RecordRunAsync
    // only touches saved-queries.json, so a template id will simply not be
    // found and the update is skipped.)

    private async Task ApplySavedSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        if (ReferenceEquals(settings, WindowSettings.Empty)) return;

        if (!string.IsNullOrWhiteSpace(settings.LastQuery))
            QueryBox.Query = settings.LastQuery;

        SelectIfPresent(TimeframeBox, settings.Timeframe);
    }

    private static void SelectIfPresent(ComboBox box, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        // Combo items may be ComboBoxItem (hardcoded) or plain strings
        // (ItemsSource assigned from FilterOptions). Handle both.
        foreach (var item in box.Items)
        {
            var text = item switch
            {
                ComboBoxItem ci => ci.Content?.ToString(),
                _ => item?.ToString()
            };

            if (string.Equals(text, value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
    }


}
/// <summary>
/// Wrapper for the query combo. A null Query means the row is a
/// non-selectable separator used to group Templates and Saved entries.
/// </summary>
internal sealed class QueryComboItem
{
    public SavedQuery? Query { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsSeparator { get; init; }

    public static QueryComboItem Separator(string text) => new()
    {
        Query = null,
        Display = text,
        IsSeparator = true
    };

    public static QueryComboItem From(SavedQuery q, bool isTemplate) => new()
    {
        Query = q,
        Display = isTemplate ? $"  {q.Name}" : $"  {q.Name}"
    };
}
