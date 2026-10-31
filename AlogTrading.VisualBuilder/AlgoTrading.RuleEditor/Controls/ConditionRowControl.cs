using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AlgoTrading.RuleEditor.Models;

namespace AlgoTrading.RuleEditor.Controls;

public sealed class ConditionRowControl : Border
{
    private static readonly (string Label, string Value)[] Operators =
    {
        (">",           "GreaterThan"), ("<",           "LessThan"),
        (">=",          "GreaterOrEqual"), ("<=",          "LessOrEqual"),
        ("=",           "Equal"), ("!=",          "NotEqual"),
        ("Crossed above","CrossedAbove"), ("Crossed below","CrossedBelow"),
        ("Opened above","OpenedAbove"), ("Opened below","OpenedBelow"),
        ("Closed above","ClosedAbove"), ("Closed below","ClosedBelow")
    };

    private readonly EditorNode _model;
    private readonly Func<IReadOnlyList<string>> _parameterNamesProvider;
    private readonly Func<IReadOnlyList<string>> _referenceNamesProvider;
    private readonly Action _onChanged;
    private readonly Action<ConditionRowControl> _onDelete;

    private ComboBox _opCombo = null!;
    private ContentControl _leftHost = null!;
    private ContentControl _rightHost = null!;

    public ConditionRowControl(
        EditorNode model,
        Func<IReadOnlyList<string>> parameterNamesProvider,
        Func<IReadOnlyList<string>> referenceNamesProvider,
        Action onChanged,
        Action<ConditionRowControl> onDelete)
    {
        _model = model;
        _parameterNamesProvider = parameterNamesProvider;
        _referenceNamesProvider = referenceNamesProvider;
        _onChanged = onChanged;
        _onDelete = onDelete;
        ConditionGroupControl.RowModelMap[this] = model;

        Padding = new Thickness(6, 4, 6, 4);
        Margin = new Thickness(0, 2, 0, 2);
        Background = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFC));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4));
        BorderThickness = new Thickness(1);

        BuildUI();
    }

    private void BuildUI()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        var enabled = new CheckBox { IsChecked = _model.Enabled, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Enabled" };
        enabled.Checked += (s, e) => { _model.Enabled = true; _onChanged(); };
        enabled.Unchecked += (s, e) => { _model.Enabled = false; _onChanged(); };
        Grid.SetColumn(enabled, 0);
        g.Children.Add(enabled);

        _leftHost = new ContentControl { Margin = new Thickness(0, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Top };
        Grid.SetColumn(_leftHost, 1);
        g.Children.Add(_leftHost);

        _opCombo = new ComboBox { Width = Double.NaN, Margin = new Thickness(4, 0, 4, 0), HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Top, VerticalContentAlignment = VerticalAlignment.Top };
        foreach (var (label, _) in Operators) _opCombo.Items.Add(label);
        _opCombo.SelectedItem = LabelFor(_model.Operator);
        _opCombo.SelectionChanged += OnOperatorChanged;
        Grid.SetColumn(_opCombo, 2);
        g.Children.Add(_opCombo);

        _rightHost = new ContentControl { Margin = new Thickness(4, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Top};
        Grid.SetColumn(_rightHost, 3);
        g.Children.Add(_rightHost);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var commentBtn = new Button { Content = "...", Width = 26, Height = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0), ToolTip = "Comment" };
        commentBtn.Click += OnCommentClick;
        buttons.Children.Add(commentBtn);

        var delBtn = new Button { Content = "X", Width = 26, Height = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), ToolTip = "Delete" };
        delBtn.Click += (s, e) => _onDelete(this);
        buttons.Children.Add(delBtn);

        Grid.SetColumn(buttons, 4);
        g.Children.Add(buttons);

        Child = g;
        RebuildEditors();
    }

    private void RebuildEditors()
    {
        _leftHost.Content = new ExpressionEditorControl(
            _model.Left, 0, _parameterNamesProvider, _referenceNamesProvider, () => { EnforceOpenedClosedConstraint(); _onChanged(); });

        _rightHost.Content = new ExpressionEditorControl(
            _model.Right, 0, _parameterNamesProvider, _referenceNamesProvider, _onChanged);
    }

    private void OnOperatorChanged(object s, SelectionChangedEventArgs e)
    {
        var label = _opCombo.SelectedItem as string;
        var value = ValueFor(label);
        if (string.IsNullOrEmpty(value)) return;
        _model.Operator = value;
        EnforceOpenedClosedConstraint();
        RebuildEditors();
        _onChanged();
    }

    private void EnforceOpenedClosedConstraint()
    {
        var op = _model.Operator;
        if (op is "OpenedAbove" or "OpenedBelow") { _model.Left.Kind = "field"; _model.Left.Field = "Open"; }
        else if (op is "ClosedAbove" or "ClosedBelow") { _model.Left.Kind = "field"; _model.Left.Field = "Close"; }
    }

    private void OnCommentClick(object s, RoutedEventArgs e)
    {
        var dlg = new CommentDialog(_model.Comment ?? "") { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) { _model.Comment = string.IsNullOrWhiteSpace(dlg.CommentText) ? null : dlg.CommentText; _onChanged(); }
    }

    private static string LabelFor(string value)
    {
        foreach (var (label, val) in Operators) if (val == value) return label;
        return ">";
    }

    private static string ValueFor(string? label)
    {
        if (label is null) return "";
        foreach (var (lab, val) in Operators) if (lab == label) return val;
        return "";
    }
}