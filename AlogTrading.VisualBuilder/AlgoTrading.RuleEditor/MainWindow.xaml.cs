using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Models;
using AlgoTrading.RuleEditor.Services;
using AlgoTrading.RuleEditor.Tabs;
using AlgoTrading.RuleEditor.Windows;

namespace AlgoTrading.RuleEditor;

public partial class MainWindow : Window
{
    private const double SidePanelDefaultWidth = 340;
    private const double SidePanelMinWidth = 200;

    private readonly RuleFileService _fileService = new();
    private readonly EditorSettings _settings;
    private readonly InstrumentLookupService _lookup;
    private readonly RuleValidationService _validator;
    private readonly ResultWatcherService _resultWatcher;

    private EditorStrategy? _strategy;
    private bool _suppressSlotApply;

    private double _lastSidePanelWidth = SidePanelDefaultWidth;
    private bool _sidePanelVisible = true;

    public MainWindow()
    {
        InitializeComponent();

        _settings = EditorSettings.Load();
        _lookup = new InstrumentLookupService(_settings.SqlConnectionString);
        _validator = new RuleValidationService(_lookup);

        ChecksPanel.AttachValidator(_validator);
        ChecksPanel.ValidateRequested += (s, e) => RunValidation();

        OverviewTabControl.RuleChanged += (s, e) => OnRuleChanged();
        OverviewTabControl.ParametersChanged += (s, e) => OnParametersChanged();

        EligibilityTabControl.Configure(this, "eligibility", showStopLossTargetPanel: false);
        EntryTabControl.Configure(this, "entry", showStopLossTargetPanel: false);
        ProtectionTabControl.Configure(this, "protection", showStopLossTargetPanel: true);
        FullExitTabControl.Configure(this, "fullExit", showStopLossTargetPanel: false);

        EligibilityTabControl.SlotChanged += (s, e) => OnRuleChanged();
        EntryTabControl.SlotChanged += (s, e) => OnRuleChanged();
        ProtectionTabControl.SlotChanged += (s, e) => OnRuleChanged();
        FullExitTabControl.SlotChanged += (s, e) => OnRuleChanged();

        BacktestTabControl.Configure(this, _settings, _lookup);

        _resultWatcher = new ResultWatcherService(_settings.ResultsPath);
        _resultWatcher.ResultArrived += OnResultArrived;

        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ToggleSidePanel()),
            Key.C, ModifierKeys.Control | ModifierKeys.Shift));

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    // ---------- side panel docking ----------

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPersistedSidePanelState();
        try { _resultWatcher.Start(); } catch { }
        NewStrategy();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSidePanelState();
        try { _resultWatcher.Dispose(); } catch { }
    }

    private void OnSidePanelToggleChanged(object sender, RoutedEventArgs e)
    {
        var wantVisible = SidePanelToggle.IsChecked == true;
        if (wantVisible == _sidePanelVisible) return;
        SetSidePanelVisible(wantVisible);
    }

    private void ToggleSidePanel()
    {
        SetSidePanelVisible(!_sidePanelVisible);
        SidePanelToggle.IsChecked = _sidePanelVisible;
    }

    private void SetSidePanelVisible(bool visible)
    {
        if (visible)
        {
            SidePanelHost.Visibility = Visibility.Visible;
            SideSplitter.Visibility = Visibility.Visible;
            SidePanelColumn.MinWidth = SidePanelMinWidth;
            var w = _lastSidePanelWidth < SidePanelMinWidth
                ? SidePanelDefaultWidth
                : _lastSidePanelWidth;
            SidePanelColumn.Width = new GridLength(w, GridUnitType.Pixel);
        }
        else
        {
            if (SidePanelColumn.ActualWidth >= SidePanelMinWidth)
                _lastSidePanelWidth = SidePanelColumn.ActualWidth;

            SidePanelHost.Visibility = Visibility.Collapsed;
            SideSplitter.Visibility = Visibility.Collapsed;
            SidePanelColumn.MinWidth = 0;
            SidePanelColumn.Width = new GridLength(0, GridUnitType.Pixel);
        }
        _sidePanelVisible = visible;
    }

    private void OnSplitterDragCompleted(object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (SidePanelColumn.ActualWidth < SidePanelMinWidth)
        {
            SetSidePanelVisible(false);
            SidePanelToggle.IsChecked = false;
            return;
        }
        _lastSidePanelWidth = SidePanelColumn.ActualWidth;
    }

    // ---------- dock persistence ----------

    private static string DockStatePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlgoTrading", "RuleEditor");
            return Path.Combine(dir, "dock-state.json");
        }
    }

    private void ApplyPersistedSidePanelState()
    {
        try
        {
            var path = DockStatePath;
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                var dto = JsonSerializer.Deserialize<DockStateDto>(text);
                if (dto is not null)
                {
                    if (dto.SidePanelWidth >= SidePanelMinWidth)
                        _lastSidePanelWidth = dto.SidePanelWidth;
                    _sidePanelVisible = dto.SidePanelVisible;
                }
            }
        }
        catch { }

        SidePanelToggle.IsChecked = _sidePanelVisible;
        SetSidePanelVisible(_sidePanelVisible);
    }

    private void SaveSidePanelState()
    {
        try
        {
            if (SidePanelColumn.ActualWidth >= SidePanelMinWidth)
                _lastSidePanelWidth = SidePanelColumn.ActualWidth;

            var dir = Path.GetDirectoryName(DockStatePath)!;
            Directory.CreateDirectory(dir);
            var dto = new DockStateDto
            {
                SidePanelVisible = _sidePanelVisible,
                SidePanelWidth = _lastSidePanelWidth
            };
            File.WriteAllText(DockStatePath, JsonSerializer.Serialize(dto));
        }
        catch { }
    }

    private sealed class DockStateDto
    {
        public bool SidePanelVisible { get; set; } = true;
        public double SidePanelWidth { get; set; } = SidePanelDefaultWidth;
    }

    // ---------- result notification ----------

    private void OnResultArrived(object? sender, ResultNotification n)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowResultToast(n);
            BacktestTabControl.RefreshRunList();
        }));
    }

    private void ShowResultToast(ResultNotification n)
    {
        var title = "Backtest complete";
        if (!string.IsNullOrEmpty(n.StrategyName))
            title = "Backtest complete: " + n.StrategyName;

        var body = BuildToastBody(n);
        ToastHost.Show(title, body);
    }

    private static string BuildToastBody(ResultNotification n)
    {
        var parts = new List<string>(4);
        if (n.InstrumentToken.HasValue)
            parts.Add("token " + n.InstrumentToken.Value);
        if (n.TotalTrades.HasValue)
            parts.Add("trades " + n.TotalTrades.Value);
        if (n.NetPnL.HasValue)
            parts.Add("Net P&L " + n.NetPnL.Value.ToString("0.00"));
        if (n.WinRate.HasValue)
            parts.Add("Win " + n.WinRate.Value.ToString("0.0") + "%");
        return string.Join(" | ", parts);
    }

    // ---------- exposed to tabs ----------

    public RuleDefinition? CurrentRule() => _strategy?.Rule;

    public IReadOnlyList<string> CurrentParameterNames()
    {
        var list = new List<string>();
        foreach (var p in OverviewTabControl.GetParameters())
            if (!string.IsNullOrEmpty(p.Name)) list.Add(p.Name);
        return list;
    }

    public void ApplyConditionSlot(string slot, EditorNode root)
    {
        if (_strategy is null || _suppressSlotApply) return;
        _strategy.Rule = ConditionTreeService.ApplySlot(_strategy.Rule, slot, root);
        _strategy.IsDirty = true;
    }

    public void ApplyStopLossTarget(StopLossTargetDto? sl, StopLossTargetDto? tp)
    {
        if (_strategy is null) return;
        _strategy.Rule = StopLossTargetService.Apply(_strategy.Rule, "stopLoss", sl);
        _strategy.Rule = StopLossTargetService.Apply(_strategy.Rule, "target", tp);
        _strategy.IsDirty = true;
    }

    // ---------- file menu ----------

    private void OnNewClick(object sender, RoutedEventArgs e) => NewStrategy();

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Strategy JSON (*.json)|*.json",
            Title = "Open strategy"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var rule = _fileService.Load(dlg.FileName);
            _strategy = new EditorStrategy
            {
                Rule = rule,
                CurrentFilePath = dlg.FileName,
                IsDirty = false
            };
            BindAll();
            StatusText.Text = "Opened " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to open: " + ex.Message, "Open",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => SaveCurrent(false);
    private void OnSaveAsClick(object sender, RoutedEventArgs e) => SaveCurrent(true);

    private void OnCostsClick(object sender, RoutedEventArgs e)
    {
        var win = new CostConfigWindow(_settings.CostsPath) { Owner = this };
        win.ShowDialog();
    }

    private void SaveCurrent(bool forceDialog)
    {
        if (_strategy is null) { StatusText.Text = "Nothing to save."; return; }

        RunValidation();
        if (!IsValidForSave()) { StatusText.Text = "Save blocked: validation errors."; return; }

        string targetPath;
        if (forceDialog || string.IsNullOrEmpty(_strategy.CurrentFilePath))
        {
            if (forceDialog)
            {
                _strategy.Rule.InstrumentToken = null;
                _strategy.Rule.InstrumentDisplay = null;
                _strategy.Rule.TargetTimeframe = null;
                ApplyTunedBadge();
            }

            var suggestedName = RuleFileService.SanitizeName(_strategy.Rule.StrategyName ?? "strategy");
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Strategy JSON (*.json)|*.json",
                FileName = suggestedName + ".json",
                Title = forceDialog ? "Save strategy as" : "Save strategy"
            };
            if (dlg.ShowDialog(this) != true) return;
            targetPath = dlg.FileName;
        }
        else
        {
            targetPath = _strategy.CurrentFilePath!;
        }

        try
        {
            _fileService.Save(_strategy.Rule, targetPath);
            _strategy.CurrentFilePath = targetPath;
            _strategy.IsDirty = false;
            FileLabel.Text = Path.GetFileName(targetPath);
            StatusText.Text = "Saved " + Path.GetFileName(targetPath);
            UpdateWindowTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to save: " + ex.Message, "Save",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- strategy lifecycle ----------

    private void NewStrategy()
    {
        try
        {
            var rule = RuleDefinitionJson.Deserialize(NewStrategyTemplate.Json);
            _strategy = new EditorStrategy { Rule = rule, IsDirty = false };
            BindAll();
            StatusText.Text = "New strategy.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to seed new strategy: " + ex.Message,
                "New", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BindAll()
    {
        if (_strategy is null) return;
        _suppressSlotApply = true;
        try
        {
            OverviewTabControl.Bind(_strategy);
            EligibilityTabControl.Bind(_strategy);
            EntryTabControl.Bind(_strategy);
            ProtectionTabControl.Bind(_strategy);
            FullExitTabControl.Bind(_strategy);
        }
        finally { _suppressSlotApply = false; }

        ApplyTunedBadge();
        FileLabel.Text = _strategy.CurrentFilePath is null
            ? "(unsaved)" : Path.GetFileName(_strategy.CurrentFilePath);
        UpdateWindowTitle();
        RunValidation();
    }

    private void ApplyTunedBadge()
    {
        var tuned = _strategy?.IsTuned == true;
        ModeBadge.Text = tuned ? "Tuned" : "Generic";
    }

    private void UpdateWindowTitle()
    {
        var name = _strategy?.Rule.StrategyName ?? "Untitled strategy";
        Title = name + " - AlgoTrading Rule Editor";
    }

    // ---------- validation ----------

    private bool _lastValidationOk;

    private void OnParametersChanged()
    {
        if (_strategy is null) return;
        _strategy.Rule = ParameterService.Apply(_strategy.Rule, OverviewTabControl.GetParameters());
        _strategy.IsDirty = true;
        OnRuleChanged();
    }

    private void OnRuleChanged() => RunValidation();

    private void RunValidation()
    {
        if (_strategy is null)
        {
            ChecksPanel.Clear();
            HandoffPanel.Clear();
            _lastValidationOk = false;
            return;
        }

        ChecksPanel.Show(_strategy.Rule);
        HandoffPanel.Update(_strategy.Rule);

        try
        {
            var r = _validator.Validate(_strategy.Rule);
            _lastValidationOk = r.IsValid &&
                                _validator.CheckInstrumentExists(_strategy.Rule) is null;
        }
        catch
        {
            _lastValidationOk = false;
        }
    }

    private bool IsValidForSave() => _lastValidationOk;

    // ---------- minimal RelayCommand for key bindings ----------

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) { _execute = execute; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }
}
