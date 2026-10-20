using System.Windows.Controls;
using System.Windows.Media;
using AlgoTrading.Models;
using AlgoTrading.RuleEditor.Services;

namespace AlgoTrading.RuleEditor.Controls;

public partial class RuntimeHandoffPanel : UserControl
{
    public RuntimeHandoffPanel()
    {
        InitializeComponent();
    }

    public void Clear()
    {
        TimeframesText.Text = "No rule loaded.";
        BaseRequiredText.Text = "";
        FeasibilityText.Text = "";
    }

    public void Update(RuleDefinition? rule)
    {
        if (rule is null) { Clear(); return; }

        IReadOnlyCollection<string> tfs;
        try { tfs = RuleAnalysisService.CollectReferencedTimeframes(rule); }
        catch { tfs = Array.Empty<string>(); }

        var sorted = tfs.OrderBy(s => Rank(s)).ThenBy(s => s, StringComparer.Ordinal).ToList();
        TimeframesText.Text = "Timeframes referenced: " +
            (sorted.Count == 0 ? "(none)" : string.Join(", ", sorted));

        var baseTf = SafeString(rule.BaseTimeframe);
        var evalTf = SafeString(rule.EvaluationTimeframe);

        var needsDaily = sorted.Any(IsPositional);
        var needsIntraday = sorted.Any(IsIntraday) || IsIntraday(evalTf);

        string requirement;
        if (needsIntraday && needsDaily) requirement = "Both intraday and daily base required.";
        else if (needsIntraday) requirement = "Intraday base required.";
        else if (needsDaily) requirement = "Daily base required.";
        else requirement = "No timeframe requirements.";

        BaseRequiredText.Text = "Declared base: " +
            (string.IsNullOrEmpty(baseTf) ? "(unset)" : baseTf) +
            ". " + requirement;

        var (ok, message) = CheckFeasibility(baseTf, sorted);
        FeasibilityText.Text = (ok ? "[OK] " : "[X] ") + message;
        FeasibilityText.Foreground = ok ? Brushes.DarkGreen : Brushes.Firebrick;
    }

    private static (bool ok, string message) CheckFeasibility(string baseTf, IEnumerable<string> tfs)
    {
        if (string.IsNullOrEmpty(baseTf)) return (false, "Base timeframe is unset.");

        if (!TryParseMinutes(baseTf, out var baseMin))
        {
            return (true, "Positional base - no aggregation needed.");
        }

        var offenders = new List<string>();
        foreach (var tf in tfs)
        {
            if (tf == baseTf) continue;
            if (TryParseMinutes(tf, out var m))
            {
                if (m <= 0 || m % baseMin != 0) offenders.Add(tf);
            }
        }

        if (offenders.Count == 0) return (true, "Base timeframe satisfies all referenced timeframes.");
        return (false, "Base " + baseTf + " cannot produce: " + string.Join(", ", offenders) + ".");
    }

    private static bool TryParseMinutes(string tf, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrEmpty(tf)) return false;
        if (tf.EndsWith("m", StringComparison.Ordinal))
            return int.TryParse(tf[..^1], out minutes);
        if (tf.EndsWith("H", StringComparison.Ordinal) &&
            int.TryParse(tf[..^1], out var h)) { minutes = h * 60; return true; }
        return false;
    }

    private static bool IsIntraday(string tf)
        => !string.IsNullOrEmpty(tf) &&
           (tf.EndsWith("m", StringComparison.Ordinal) || tf.EndsWith("H", StringComparison.Ordinal));

    private static bool IsPositional(string tf)
        => tf is "D" or "W" or "M";

    private static int Rank(string tf)
    {
        if (TryParseMinutes(tf, out var m)) return m;
        return tf switch { "D" => 100000, "W" => 200000, "M" => 300000, _ => 999999 };
    }

    private static string SafeString(Timeframe tf)
    {
        try { return tf.ToString() ?? ""; }
        catch { return ""; }
    }
}