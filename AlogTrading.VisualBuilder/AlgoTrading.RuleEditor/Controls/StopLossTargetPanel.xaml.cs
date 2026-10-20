using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AlgoTrading.RuleEditor.Services;

namespace AlgoTrading.RuleEditor.Controls;

public partial class StopLossTargetPanel : UserControl
{
    public event EventHandler? Changed;

    private StopLossTargetDto? _sl;
    private StopLossTargetDto? _tp;
    private bool _suppress;

    public StopLossTargetPanel()
    {
        InitializeComponent();
        ApplySlVisibility();
        ApplyTpVisibility();
    }

    public void Bind(StopLossTargetDto? sl, StopLossTargetDto? tp, IReadOnlyList<string> namedGroups)
    {
        _suppress = true;
        try
        {
            _sl = sl;
            _tp = tp;

            PopulateGroupCombo(SlGroupBox, namedGroups);
            PopulateGroupCombo(TpGroupBox, namedGroups);

            SlEnabledBox.IsChecked = sl is not null;
            SlModeBox.SelectedIndex = ModeIndex(sl?.Mode);
            SlValueBox.Text = (sl?.Value ?? 0m).ToString(CultureInfo.InvariantCulture);
            SlGroupBox.SelectedItem = sl?.GroupName;

            TpEnabledBox.IsChecked = tp is not null;
            TpModeBox.SelectedIndex = ModeIndex(tp?.Mode);
            TpValueBox.Text = (tp?.Value ?? 0m).ToString(CultureInfo.InvariantCulture);
            TpGroupBox.SelectedItem = tp?.GroupName;
        }
        finally { _suppress = false; }
        ApplySlVisibility();
        ApplyTpVisibility();
    }

    public StopLossTargetDto? GetStopLoss() => _sl;
    public StopLossTargetDto? GetTarget() => _tp;

    private static int ModeIndex(string? mode) => mode switch
    {
        "Points" => 1,
        "Rule" => 2,
        _ => 0
    };

    private static void PopulateGroupCombo(ComboBox box, IReadOnlyList<string> groups)
    {
        box.Items.Clear();
        foreach (var g in groups) box.Items.Add(g);
    }

    private static string? ModeFromCombo(ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem item && item.Content is string s) return s;
        return null;
    }

    private void ApplySlVisibility()
    {
        var mode = ModeFromCombo(SlModeBox) ?? "Percentage";
        SlValueRow.Visibility = mode == "Rule" ? Visibility.Collapsed : Visibility.Visible;
        SlGroupRow.Visibility = mode == "Rule" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyTpVisibility()
    {
        var mode = ModeFromCombo(TpModeBox) ?? "Percentage";
        TpValueRow.Visibility = mode == "Rule" ? Visibility.Collapsed : Visibility.Visible;
        TpGroupRow.Visibility = mode == "Rule" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSlEnabledChanged(object s, RoutedEventArgs e)
    {
        if (_suppress) return;
        if (SlEnabledBox.IsChecked == true)
        {
            _sl ??= new StopLossTargetDto { Mode = ModeFromCombo(SlModeBox) ?? "Percentage", Value = 2m };
        }
        else
        {
            _sl = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTpEnabledChanged(object s, RoutedEventArgs e)
    {
        if (_suppress) return;
        if (TpEnabledBox.IsChecked == true)
        {
            _tp ??= new StopLossTargetDto { Mode = ModeFromCombo(TpModeBox) ?? "Percentage", Value = 4m };
        }
        else
        {
            _tp = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSlModeChanged(object s, SelectionChangedEventArgs e)
    {
        ApplySlVisibility();
        if (_suppress || _sl is null) return;
        _sl.Mode = ModeFromCombo(SlModeBox) ?? "Percentage";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTpModeChanged(object s, SelectionChangedEventArgs e)
    {
        ApplyTpVisibility();
        if (_suppress || _tp is null) return;
        _tp.Mode = ModeFromCombo(TpModeBox) ?? "Percentage";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSlValueChanged(object s, RoutedEventArgs e)
    {
        if (_suppress || _sl is null) return;
        if (decimal.TryParse(SlValueBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        {
            _sl.Value = v;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTpValueChanged(object s, RoutedEventArgs e)
    {
        if (_suppress || _tp is null) return;
        if (decimal.TryParse(TpValueBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        {
            _tp.Value = v;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSlGroupChanged(object s, SelectionChangedEventArgs e)
    {
        if (_suppress || _sl is null) return;
        _sl.GroupName = SlGroupBox.SelectedItem as string;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTpGroupChanged(object s, SelectionChangedEventArgs e)
    {
        if (_suppress || _tp is null) return;
        _tp.GroupName = TpGroupBox.SelectedItem as string;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}