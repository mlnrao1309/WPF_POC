using System.Windows.Controls;
using AlgoTrading.RuleEditor.Controls;
using AlgoTrading.RuleEditor.Models;
using AlgoTrading.RuleEditor.Services;

namespace AlgoTrading.RuleEditor.Tabs;

public partial class ConditionTab : UserControl
{
    private MainWindow? _owner;
    private string _slot = "";
    private bool _showSlTp;
    private EditorNode? _root;

    public event EventHandler? SlotChanged;

    public ConditionTab()
    {
        InitializeComponent();
        SlTpPanel.Changed += (s, e) => RaiseChanged();
    }

    public void Configure(MainWindow owner, string slot, bool showStopLossTargetPanel)
    {
        _owner = owner;
        _slot = slot;
        _showSlTp = showStopLossTargetPanel;
        SlTpPanel.Visibility = _showSlTp ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public void Bind(EditorStrategy? strategy)
    {
        if (strategy is null)
        {
            _root = null;
            GroupHost.Content = null;
            return;
        }

        _root = ConditionTreeService.LoadSlot(strategy.Rule, _slot)
                ?? new EditorNode { Kind = "group", Enabled = true, Logic = "All" };

        RebuildGroupUI();

        if (_showSlTp)
        {
            var sl = StopLossTargetService.Load(strategy.Rule, "stopLoss");
            var tp = StopLossTargetService.Load(strategy.Rule, "target");
            SlTpPanel.Bind(sl, tp, CollectNamedGroups(strategy.Rule));
        }
    }

    private void RebuildGroupUI()
    {
        if (_root is null) { GroupHost.Content = null; return; }
        var ctrl = new ConditionGroupControl(
            _root, 0, isRoot: true,
            ParameterNamesProvider,
            RaiseChanged,
            onDelete: null);
        GroupHost.Content = ctrl;
    }

    private IReadOnlyList<string> ParameterNamesProvider()
    {
        if (_owner is null) return Array.Empty<string>();
        return _owner.CurrentParameterNames();
    }

    private void RaiseChanged()
    {
        if (_owner is null || _root is null) return;
        _owner.ApplyConditionSlot(_slot, _root);

        if (_showSlTp)
        {
            _owner.ApplyStopLossTarget(
                SlTpPanel.GetStopLoss(),
                SlTpPanel.GetTarget());
        }

        SlotChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<string> CollectNamedGroups(AlgoTrading.Models.RuleDefinition rule)
    {
        var list = new List<string>();
        try
        {
            var json = AlgoTrading.Models.RuleDefinitionJson.Serialize(rule);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Walk(doc.RootElement, list);
        }
        catch { }
        return list;
    }

    private static void Walk(System.Text.Json.JsonElement el, List<string> list)
    {
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (p.NameEquals("name") && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var v = p.Value.GetString();
                    if (!string.IsNullOrEmpty(v)) list.Add(v!);
                }
                Walk(p.Value, list);
            }
        }
        else if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray()) Walk(item, list);
        }
    }
}