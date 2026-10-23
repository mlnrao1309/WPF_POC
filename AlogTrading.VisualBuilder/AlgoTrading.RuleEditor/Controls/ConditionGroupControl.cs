using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AlgoTrading.RuleEditor.Models;

namespace AlgoTrading.RuleEditor.Controls;

/// <summary>
/// Recursive group editor. Root groups have no delete button; nested groups do.
/// </summary>
public sealed class ConditionGroupControl : Border
{
    private const int MaxDepth = 10;

    private readonly EditorNode _model;
    private readonly int _depth;
    private readonly bool _isRoot;
    private readonly Func<IReadOnlyList<string>> _parameterNamesProvider;
    private readonly Action _onChanged;
    private readonly Action<ConditionGroupControl>? _onDelete;

    private StackPanel _childrenPanel = null!;

    public ConditionGroupControl(
        EditorNode model,
        int depth,
        bool isRoot,
        Func<IReadOnlyList<string>> parameterNamesProvider,
        Action onChanged,
        Action<ConditionGroupControl>? onDelete)
    {
        _model = model;
        _depth = depth;
        _isRoot = isRoot;
        _parameterNamesProvider = parameterNamesProvider;
        _onChanged = onChanged;
        _onDelete = onDelete;

        Padding = new Thickness(8);
        Margin = new Thickness(0, 4, 0, 4);
        Background = _isRoot ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF9));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xD0, 0xDA));
        BorderThickness = new Thickness(1);

        BuildUI();
    }

    private void BuildUI()
    {
        var outer = new StackPanel { Orientation = Orientation.Vertical };

        // header
        var header = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

        var logicLabel = new TextBlock
        { Text = "Logic:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        header.Children.Add(logicLabel);

        var logicCombo = new ComboBox { Width = 90 };
        logicCombo.Items.Add("All");
        logicCombo.Items.Add("Any");
        logicCombo.SelectedItem = _model.Logic;
        logicCombo.SelectionChanged += (s, e) =>
        {
            if (logicCombo.SelectedItem is string v) { _model.Logic = v; _onChanged(); }
        };
        header.Children.Add(logicCombo);

        var enabledCb = new CheckBox
        {
            Content = "enabled",
            IsChecked = _model.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        enabledCb.Checked += (s, e) => { _model.Enabled = true; _onChanged(); };
        enabledCb.Unchecked += (s, e) => { _model.Enabled = false; _onChanged(); };
        header.Children.Add(enabledCb);

        var addCondBtn = new Button
        {
            Content = "+ Condition",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 0)
        };
        addCondBtn.Click += (s, e) =>
        {
            _model.Children.Add(new EditorNode
            {
                Kind = "condition",
                Enabled = true,
                Left = new EditorExpression { Kind = "field", Field = "Close" },
                Operator = "GreaterThan",
                Right = new EditorExpression { Kind = "number", Number = 0m }
            });
            RebuildChildren();
            _onChanged();
        };
        header.Children.Add(addCondBtn);

        var addGroupBtn = new Button
        {
            Content = "+ Group",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 0)
        };
        if (_depth + 1 >= MaxDepth) addGroupBtn.IsEnabled = false;
        addGroupBtn.Click += (s, e) =>
        {
            _model.Children.Add(new EditorNode
            {
                Kind = "group",
                Enabled = true,
                Logic = "All"
            });
            RebuildChildren();
            _onChanged();
        };
        header.Children.Add(addGroupBtn);

        if (!_isRoot)
        {
            var delBtn = new Button
            {
                Content = "Delete group",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 0, 0)
            };
            delBtn.Click += (s, e) => _onDelete?.Invoke(this);
            header.Children.Add(delBtn);
        }

        outer.Children.Add(header);

        _childrenPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 0, 0, 0) };
        outer.Children.Add(_childrenPanel);

        Child = outer;
        RebuildChildren();
    }

    private void RebuildChildren()
    {
        _childrenPanel.Children.Clear();
        foreach (var child in _model.Children.ToArray())
        {
            if (child.Kind == "group")
            {
                var ctrl = new ConditionGroupControl(
                    child, _depth + 1, isRoot: false,
                    _parameterNamesProvider, _onChanged,
                    onDelete: c => RemoveChild(c));
                _childrenPanel.Children.Add(ctrl);
            }
            else
            {
                var ctrl = new ConditionRowControl(
                    child, _parameterNamesProvider, _onChanged,
                    row => RemoveRow(row));
                _childrenPanel.Children.Add(ctrl);
            }
        }
    }

    private void RemoveChild(ConditionGroupControl ctrl)
    {
        if (ctrl == this) return;
        for (int i = 0; i < _model.Children.Count; i++)
        {
            // Match by reference.
            var candidate = _model.Children[i];
            if (candidate.Kind == "group" && ReferenceEquals(candidate, ctrl._model))
            {
                _model.Children.RemoveAt(i);
                break;
            }
        }
        RebuildChildren();
        _onChanged();
    }

    private void RemoveRow(ConditionRowControl row)
    {
        var model = FindConditionFor(row);
        if (model is null) return;
        _model.Children.Remove(model);
        RebuildChildren();
        _onChanged();
    }

    private EditorNode? FindConditionFor(ConditionRowControl row)
    {
        foreach (var child in _model.Children)
        {
            if (child.Kind != "condition") continue;
            // Build a probe row and compare references by identity of the model.
            // The row control does not expose its model; use the closure trick:
            // we stored the model in the constructor. Track it via a static map.
            if (RowModelMap.TryGetValue(row, out var m) && ReferenceEquals(m, child)) return child;
        }
        return null;
    }

    // Static identity maps so we can locate the model for a control instance.
    internal static readonly Dictionary<ConditionRowControl, EditorNode> RowModelMap = new();
    internal static readonly Dictionary<ConditionGroupControl, EditorNode> GroupModelMap = new();
}