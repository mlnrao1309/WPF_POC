using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Services;
using ValidationResult = AlgoTrading.Models.ValidationResult;
namespace AlgoTrading.RuleEditor.Controls;

public partial class StrategyChecksPanel : UserControl
{
    private RuleValidationService? _validator;

    public event EventHandler? ValidateRequested;

    public StrategyChecksPanel()
    {
        InitializeComponent();
    }

    public void AttachValidator(RuleValidationService validator)
    {
        _validator = validator;
        RefreshLookupStatus();
    }

    public void Clear()
    {
        ErrorList.Items.Clear();
    }

    public void Show(RuleDefinition? rule)
    {
        Clear();
        if (rule is null || _validator is null) return;

        ValidationResult result;
        try { result = _validator.Validate(rule); }
        catch (Exception ex)
        {
            AddRow("ERR", "Validator threw: " + ex.Message);
            return;
        }

        var errors = result.Errors;
        var warnings = result.Warnings;

        if (errors is not null)
        {
            foreach (var e in errors)
                AddRow("ERR", e.Code + ": " + e.Message);
        }

        if (warnings is not null)
        {
            foreach (var w in warnings)
                AddRow("WRN", w.Code + ": " + w.Message);
        }

        var extra = _validator.CheckInstrumentExists(rule);
        if (extra is not null) AddRow("ERR", extra);

        var skipped = result.SkippedChecks;
        if (skipped is not null && skipped.Count > 0)
            AddRow("INF", "Skipped: " + string.Join(", ", skipped));

        if (ErrorList.Items.Count == 0)
            AddRow("OK", "No issues found.");
    }

    private void AddRow(string severity, string text)
    {
        var color = severity switch
        {
            "ERR" => Brushes.Firebrick,
            "WRN" => Brushes.DarkOrange,
            "OK" => Brushes.DarkGreen,
            _ => Brushes.Gray
        };
        ErrorList.Items.Add(new Row { Display = "[" + severity + "] " + text, Color = color });
    }

    private void RefreshLookupStatus()
    {
        if (_validator is null) { LookupStatusText.Text = ""; return; }
        LookupStatusText.Text = _validator.LookupAvailable
            ? "Instrument lookup available."
            : "Instrument lookup unavailable - some checks skipped.";
    }

    private void OnValidateNowClick(object sender, RoutedEventArgs e)
        => ValidateRequested?.Invoke(this, EventArgs.Empty);

    public sealed class Row
    {
        public string Display { get; init; } = "";
        public Brush Color { get; init; } = Brushes.Black;
    }
}