using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AlgoTrading.Models;
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
    private bool _refreshingRuns;
    private bool _queuing;
    private readonly DispatcherTimer _historyRefresh = new() { Interval = TimeSpan.FromSeconds(3) };

    public BacktestTab()
    {
        InitializeComponent();
        LossBasisBox.ItemsSource = new Dictionary<RiskLossBasis, string>
        {
            [RiskLossBasis.RealizedOnly] = "Only realized losses (default)",
            [RiskLossBasis.IncludeUnrealized] = "Include unrealized losses on open positions"
        };
        LossBasisBox.SelectedValue = RiskLossBasis.RealizedOnly;
        TokenListBox.ItemsSource = _tokens;
        FromPicker.SelectedDate = DateTime.Today.AddYears(-2);
        ToPicker.SelectedDate = DateTime.Today;
        _historyRefresh.Tick += OnHistoryRefreshTick;
        Loaded += OnHistoryLoaded;
        Unloaded += OnHistoryUnloaded;
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

    public async void RefreshRunList()
    {
        if (_runs is null || RunsGrid is null || _refreshingRuns) return;
        _refreshingRuns = true;
        try
        {
            var rows = await _runs.LoadRecentRunsAsync(50);
            RunsGrid.ItemsSource = rows;
            RunHistoryStatus.Text = rows.Count == 0 ? "No recorded runs." : "";
        }
        catch (Exception ex)
        {
            RunHistoryStatus.Text = "Run history could not be loaded: " + ex.Message;
        }
        finally { _refreshingRuns = false; }
    }

    private void OnHistoryRefreshTick(object? sender, EventArgs e) => RefreshRunList();
    private void OnHistoryLoaded(object sender, RoutedEventArgs e)
    {
        RefreshRunList();
        _historyRefresh.Start();
    }
    private void OnHistoryUnloaded(object sender, RoutedEventArgs e) => _historyRefresh.Stop();

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

    private async void OnQueueClick(object sender, RoutedEventArgs e) => await QueueInternalAsync(ensureDaemon: false);

    private async void OnRunNowClick(object sender, RoutedEventArgs e) => await QueueInternalAsync(ensureDaemon: true);

    private async Task QueueInternalAsync(bool ensureDaemon)
    {
        if (_owner is null || _queue is null || _runs is null) return;
        if (_queuing)
        {
            QueueStatusText.Text = "A backtest submission is already being prepared.";
            return;
        }

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

        var mode = SweepBox.IsChecked == true ? "Sweep"
                 : WalkForwardBox.IsChecked == true ? "WalkForward"
                 : "Single";

        var tokens = _tokens.Select(t => t.Token).ToList();

        try
        {
            _queuing = true;
            RunNowButton.IsEnabled = false;
            QueueStatusText.Text = "Checking all required candle data...";
            var ruleJson = RuleDefinitionJson.Serialize(rule);
            foreach (var token in tokens)
                await _runs.ValidateBacktestDataAsync(token, ruleJson, from, to);
            if (ensureDaemon && _daemon is not null && !_daemon.EnsureRunning(waitMs: 15000, out var error))
                throw new InvalidOperationException(error);
            var result = _queue.Queue(new QueueBatchRequest
            {
                Rule = rule,
                InstrumentTokens = tokens,
                From = from,
                To = to,
                Mode = mode,
                LossBasis = (RiskLossBasis)LossBasisBox.SelectedValue,
                TradedCapital = 50_000m
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
            QueueStatusText.Text = "Backtest stopped before queuing.";
            MessageBox.Show("Backtest stopped: " + ex.Message, "Data preparation",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _queuing = false;
            RunNowButton.IsEnabled = true;
        }
    }

}
