using System.Windows;
using System.Windows.Controls;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Models;
using AlgoTrading.RuleEditor.Services;

namespace AlgoTrading.RuleEditor.Tabs;

public partial class OverviewTab : UserControl
{
    public event EventHandler? RuleChanged;
    public event EventHandler? ParametersChanged;

    private static readonly string[] TimeframeOptions =
    {
        "1m", "3m", "5m", "15m", "18m", "25m", "30m", "1H", "D", "W", "M"
    };

    private EditorStrategy? _strategy;
    private bool _suppress;

    public OverviewTab()
    {
        InitializeComponent();
        ParameterPanelControl.Changed += (s, e) =>
        {
            if (_suppress) return;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public IReadOnlyList<EditorParameter> GetParameters() => ParameterPanelControl.GetParameters();

    public void Bind(EditorStrategy strategy)
    {
        _strategy = strategy;
        _suppress = true;
        try
        {
            EnsureCombos();
            StrategyNameBox.Text = strategy.Rule.StrategyName ?? "";
            DescriptionBox.Text = strategy.Rule.Description ?? "";

            var tuned = strategy.Rule.InstrumentToken.HasValue && strategy.Rule.InstrumentToken.Value != 0;
            if (tuned)
            {
                var display = strategy.Rule.InstrumentDisplay ?? "";
                InstrumentBox.Text = strategy.Rule.InstrumentToken!.Value +
                    (string.IsNullOrEmpty(display) ? "" : " - " + display);
            }
            else
            {
                InstrumentBox.Text = "";
            }

            SelectTimeframe(EvalTfBox, SafeTf(strategy.Rule.EvaluationTimeframe));
            SelectTimeframe(BaseTfBox, SafeTf(strategy.Rule.BaseTimeframe));
            SelectTimeframe(TargetTfBox, strategy.Rule.TargetTimeframe.HasValue
                ? SafeTf(strategy.Rule.TargetTimeframe.Value) : "(none)");

            DirectionBox.SelectedItem = strategy.Rule.Direction.ToString();
            ExecModeBox.SelectedItem = strategy.Rule.ExecutionMode.ToString();

            ParameterPanelControl.Bind(ParameterService.Load(strategy.Rule));

            ApplyTunedVisibility();
        }
        finally { _suppress = false; }
    }

    private void EnsureCombos()
    {
        if (EvalTfBox.Items.Count == 0)
            foreach (var s in TimeframeOptions) EvalTfBox.Items.Add(s);
        if (BaseTfBox.Items.Count == 0)
            foreach (var s in TimeframeOptions) BaseTfBox.Items.Add(s);
        if (TargetTfBox.Items.Count == 0)
        {
            TargetTfBox.Items.Add("(none)");
            foreach (var s in TimeframeOptions) TargetTfBox.Items.Add(s);
        }
        if (DirectionBox.Items.Count == 0)
        {
            DirectionBox.Items.Add("Long");
            DirectionBox.Items.Add("Short");
        }
        if (ExecModeBox.Items.Count == 0)
        {
            ExecModeBox.Items.Add("Delivery");
            ExecModeBox.Items.Add("Intraday");
        }
    }

    private static void SelectTimeframe(ComboBox box, string value)
    {
        if (string.IsNullOrEmpty(value)) { box.SelectedIndex = -1; return; }
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is string s && s == value) { box.SelectedIndex = i; return; }
        }
        box.SelectedIndex = -1;
    }

    private static string SafeTf(Timeframe tf)
    {
        try { return tf.ToString() ?? ""; }
        catch { return ""; }
    }

    private void ApplyTunedVisibility()
    {
        var tuned = _strategy?.Rule.InstrumentToken.HasValue == true
                    && _strategy.Rule.InstrumentToken!.Value != 0;
        var v = tuned ? Visibility.Visible : Visibility.Collapsed;
        InstrumentLabel.Visibility = v;
        InstrumentRow.Visibility = v;
        TargetTfLabel.Visibility = v;
        TargetTfBox.Visibility = v;
    }

    private void RaiseChanged()
    {
        if (_suppress) return;
        if (_strategy is not null) _strategy.IsDirty = true;
        RuleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStrategyNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppress || _strategy is null) return;
        _strategy.Rule.StrategyName = StrategyNameBox.Text;
        RaiseChanged();
    }

    private void OnDescriptionLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppress || _strategy is null) return;
        _strategy.Rule.Description = DescriptionBox.Text;
        RaiseChanged();
    }

    private void OnDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _strategy is null) return;
        var s = DirectionBox.SelectedItem as string;
        if (string.IsNullOrEmpty(s)) return;
        if (Enum.TryParse<Direction>(s, out var d)) { _strategy.Rule.Direction = d; RaiseChanged(); }
    }

    private void OnExecModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _strategy is null) return;
        var s = ExecModeBox.SelectedItem as string;
        if (string.IsNullOrEmpty(s)) return;
        if (Enum.TryParse<ExecutionMode>(s, out var m)) { _strategy.Rule.ExecutionMode = m; RaiseChanged(); }
    }

    private void OnEvalTfChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _strategy is null) return;
        var s = EvalTfBox.SelectedItem as string;
        if (string.IsNullOrEmpty(s)) return;
        if (TryParseTf(s, out var tf)) { _strategy.Rule.EvaluationTimeframe = tf; RaiseChanged(); }
    }

    private void OnBaseTfChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _strategy is null) return;
        var s = BaseTfBox.SelectedItem as string;
        if (string.IsNullOrEmpty(s)) return;
        if (TryParseTf(s, out var tf)) { _strategy.Rule.BaseTimeframe = tf; RaiseChanged(); }
    }

    private void OnTargetTfChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _strategy is null) return;
        var s = TargetTfBox.SelectedItem as string;
        if (string.IsNullOrEmpty(s)) return;
        if (s == "(none)") { _strategy.Rule.TargetTimeframe = null; RaiseChanged(); return; }
        if (TryParseTf(s, out var tf)) { _strategy.Rule.TargetTimeframe = tf; RaiseChanged(); }
    }

    private void OnPickInstrumentClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Instrument picker is delivered in T8c." + Environment.NewLine +
            "For now, set instrumentToken and instrumentDisplay directly in the JSON " +
            "and open the file.",
            "Instrument picker",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static bool TryParseTf(string s, out Timeframe tf)
    {
        tf = default;
        try { tf = Timeframe.Parse(s); return true; }
        catch { return false; }
    }
}