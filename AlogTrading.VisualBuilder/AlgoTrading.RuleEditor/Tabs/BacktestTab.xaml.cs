using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Models;
using AlgoTrading.RuleEditor.Services;
using AlgoTrading.RuleEditor.Windows;

namespace AlgoTrading.RuleEditor.Tabs;

public partial class BacktestTab : UserControl
{
    public sealed class TokenRow
    {
        public uint Token { get; init; }
        public string Symbol { get; init; } = "";
        public string DisplayName { get; init; } = "";
    }

    private readonly ObservableCollection<TokenRow> _tokens = new();

    private MainWindow? _owner;
    private EditorSettings? _settings;
    private InstrumentLookupService? _lookup;
    private StrategyRepositoryService? _runs;
    private BacktestQueueService? _queue;
    private DaemonLauncherService? _daemon;
    private bool _suppressChecks;

    public BacktestTab()
    {
        InitializeComponent();
        TokenListBox.ItemsSource = _tokens;
        FromPicker.SelectedDate = DateTime.Today.AddYears(-2);
        ToPicker.SelectedDate = DateTime.Today;
    }

    public void Configure(MainWindow owner, EditorSettings settings,
                          InstrumentLookupService lookup)
    {
        _owner = owner;
        _settings = settings;
        _lookup = lookup;
        _runs = new StrategyRepositoryService(settings.SqlConnectionString);
        _queue = new BacktestQueueService(settings.RulesPendingPath, settings.RulesRunningPath);
        _daemon = new DaemonLauncherService(
            settings.CliExecutablePath, settings.ConfigPath, settings.LockPath);
        RefreshRunList();
    }

    public void Bind(EditorStrategy? strategy)
    {
        if (strategy is null) return;
        _suppressChecks = true;
        try
        {
            BaseTfBox.Text = SafeTf(strategy.Rule.BaseTimeframe);
        }
        finally { _suppressChecks = false; }
    }

    public void RefreshRunList()
    {
        if (_runs is null || RunsGrid is null) return;
        try { RunsGrid.ItemsSource = _runs.LoadRecentRuns(50); }
        catch { RunsGrid.ItemsSource = Array.Empty<RunListRow>(); }
    }

    private void OnModeCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChecks) return;
        if (SweepBox.IsChecked == true && WalkForwardBox.IsChecked == true)
        {
            _suppressChecks = true;
            if (ReferenceEquals(sender, SweepBox)) WalkForwardBox.IsChecked = false;
            else SweepBox.IsChecked = false;
            _suppressChecks = false;
        }
    }

    private void OnPickTokensClick(object sender, RoutedEventArgs e)
    {
        if (_lookup is null || !_lookup.IsAvailable)
        {
            MessageBox.Show(
                "SQL connection is not configured. Set it in appsettings.json " +
                "or the ALGOTRADING_SQL environment variable.",
                "Instrument picker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new InstrumentPickerWindow(_lookup, allowMultiSelect: true)
        {
            Owner = Window.GetWindow(this)
        };
        if (win.ShowDialog() != true) return;

        foreach (var dto in win.SelectedInstruments)
        {
            if (_tokens.Any(t => t.Token == dto.Token)) continue;
            _tokens.Add(new TokenRow
            {
                Token = dto.Token,
                Symbol = dto.Symbol,
                DisplayName = dto.DisplayName
            });
        }
    }

    private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = TokenListBox.SelectedItems.Cast<TokenRow>().ToList();
        foreach (var row in selected) _tokens.Remove(row);
    }

    private void OnClearTokensClick(object sender, RoutedEventArgs e) => _tokens.Clear();

    private void OnOpenResultsClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        try
        {
            Directory.CreateDirectory(_settings.ResultsPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _settings.ResultsPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to open results folder: " + ex.Message,
                "Open folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnQueueClick(object sender, RoutedEventArgs e) => QueueInternal(ensureDaemon: false);

    private void OnRunNowClick(object sender, RoutedEventArgs e) => QueueInternal(ensureDaemon: true);

    private void QueueInternal(bool ensureDaemon)
    {
        if (_owner is null || _queue is null) return;

        var rule = _owner.CurrentRule();
        if (rule is null)
        {
            MessageBox.Show("No strategy is loaded.", "Queue",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_tokens.Count == 0)
        {
            MessageBox.Show("Pick at least one instrument.", "Queue",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (FromPicker.SelectedDate is null || ToPicker.SelectedDate is null)
        {
            MessageBox.Show("From and To dates are required.", "Queue",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var from = DateOnly.FromDateTime(FromPicker.SelectedDate.Value);
        var to = DateOnly.FromDateTime(ToPicker.SelectedDate.Value);
        if (from.CompareTo(to) > 0)
        {
            MessageBox.Show("From must be <= To.", "Queue",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // "Run now" ensures the daemon is up before writing the batch.
        if (ensureDaemon && _daemon is not null)
        {
            if (!_daemon.EnsureRunning(waitMs: 15000, out var error))
            {
                MessageBox.Show(error, "Run now", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        var mode = SweepBox.IsChecked == true ? "Sweep"
                 : WalkForwardBox.IsChecked == true ? "WalkForward"
                 : "Single";

        var tokens = _tokens.Select(t => t.Token).ToList();

        try
        {
            var result = _queue.Queue(new QueueBatchRequest
            {
                Rule = rule,
                InstrumentTokens = tokens,
                From = from,
                To = to,
                Mode = mode
            });

            var message = "Queued " + result.QueuedJobPaths.Count +
                          (result.QueuedJobPaths.Count == 1 ? " job" : " jobs");
            if (result.SkippedDuplicates.Count > 0)
            {
                message += "  |  Skipped " + result.SkippedDuplicates.Count +
                           " duplicate(s): " +
                           string.Join(", ", result.SkippedDuplicates);
            }
            QueueStatusText.Text = message;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to queue: " + ex.Message, "Queue",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SafeTf(Timeframe tf)
    {
        try { return tf.ToString() ?? ""; }
        catch { return ""; }
    }
}