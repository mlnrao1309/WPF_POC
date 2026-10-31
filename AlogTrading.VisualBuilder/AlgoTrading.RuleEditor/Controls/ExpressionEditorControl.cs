using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AlgoTrading.RuleEditor.Models;
using AlgoTrading.RuleEditor.Services;

namespace AlgoTrading.RuleEditor.Controls;

public sealed class ExpressionEditorControl : UserControl
{
    private static readonly string[] KindKeys =
        { "field", "number", "parameter", "indicator", "calculation", "reference" };

    private static readonly (string Key, string Label)[] TimeframeMap =
    {
        ("5m", "5m"), ("15m", "15m"), ("1H", "1H"), ("D", "Daily"), ("W", "Weekly"), ("M", "Monthly")
    };

    private readonly EditorExpression _model;
    private readonly int _depth;
    private readonly Action _onChanged;
    private readonly Func<IReadOnlyList<string>> _parameterNamesProvider;
    private readonly Func<IReadOnlyList<string>> _referenceNamesProvider;

    private Button _kindBtn = null!;
    private ContentControl _subHost = null!;

    public ExpressionEditorControl(
        EditorExpression model, int depth,
        Func<IReadOnlyList<string>> parameterNamesProvider,
        Func<IReadOnlyList<string>> referenceNamesProvider,
        Action onChanged)
    {
        _model = model ?? new EditorExpression();
        _depth = depth;
        _parameterNamesProvider = parameterNamesProvider;
        _referenceNamesProvider = referenceNamesProvider;
        _onChanged = onChanged;
        BuildShell();
        RebuildSub();
    }

    private void BuildShell()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _kindBtn = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 3, 0),
            Content = KindGlyph(_model.Kind),
            ToolTip = "Change expression kind"
        };
        _kindBtn.Click += OnKindButtonClick;
        Grid.SetColumn(_kindBtn, 0);
        g.Children.Add(_kindBtn);

        _subHost = new ContentControl { VerticalContentAlignment = VerticalAlignment.Top };
        Grid.SetColumn(_subHost, 1);
        g.Children.Add(_subHost);

        Content = g;
    }

    private static string KindGlyph(string kind) => kind switch
    {
        "field" => "F",
        "number" => "#",
        "parameter" => "$",
        "indicator" => "I",
        "calculation" => "=",
        "reference" => "R",
        _ => "?"
    };

    private static string KindLabel(string kind) => kind switch
    {
        "field" => "Field",
        "number" => "Number",
        "parameter" => "Parameter",
        "indicator" => "Indicator",
        "calculation" => "Calculation",
        "reference" => "Reference",
        _ => kind
    };

    private void OnKindButtonClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var kind in KindKeys)
        {
            var captured = kind;
            var item = new MenuItem { Header = KindLabel(kind), IsCheckable = true, IsChecked = kind == _model.Kind };
            item.Click += (s, args) => SwitchKind(captured);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = _kindBtn;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void SwitchKind(string newKind)
    {
        if (newKind == _model.Kind) return;
        _model.Kind = newKind;

        switch (newKind)
        {
            case "field":
                _model.Offset = 0; _model.Timeframe = null; _model.Field ??= "Close";
                break;
            case "number":
                break;
            case "parameter":
                var names = _parameterNamesProvider();
                if (names.Count > 0 && string.IsNullOrEmpty(_model.ParameterName)) _model.ParameterName = names[0];
                break;
            case "reference":
                _model.Offset = 0; _model.Timeframe = null;
                var refNames = _referenceNamesProvider();
                if (refNames.Count > 0 && string.IsNullOrEmpty(_model.ReferenceId)) _model.ReferenceId = refNames[0];
                break;
            case "indicator":
                _model.Offset = 0; _model.Timeframe = null; _model.Indicator ??= "Sma";
                if (_model.Params.Count == 0)
                {
                    var d = IndicatorCatalogService.Find(_model.Indicator);
                    if (d is not null) foreach (var p in d.Parameters) _model.Params[p.Name] = p.Default;
                }
                _model.Output ??= null;
                break;
            case "calculation":
                _model.CalcOp ??= "Add"; _model.CalcLeft ??= new EditorExpression { Kind = "field", Field = "Close" }; _model.CalcRight ??= new EditorExpression { Kind = "number", Number = 0m };
                break;
        }

        _kindBtn.Content = KindGlyph(newKind);
        RebuildSub();
        _onChanged();
    }

    private void RebuildSub()
    {
        switch (_model.Kind)
        {
            case "number": _subHost.Content = BuildNumberUI(); break;
            case "parameter": _subHost.Content = BuildParameterUI(); break;
            case "reference": _subHost.Content = BuildReferenceUI(); break;
            case "indicator": _subHost.Content = BuildIndicatorUI(); break;
            case "calculation": _subHost.Content = BuildCalculationUI(); break;
            default: _subHost.Content = BuildFieldUI(); break;
        }
    }

    private UIElement BuildFieldUI()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(MakeTimeframeCombo(_model.Timeframe, v => { _model.Timeframe = v; _onChanged(); }));
        panel.Children.Add(MakeOffsetBox(_model.Offset, v => { _model.Offset = v; _onChanged(); }));
        panel.Children.Add(MakeFieldCombo(_model.Field, v => { _model.Field = v; _onChanged(); }));
        return panel;
    }

    private UIElement BuildNumberUI()
    {
        var box = new TextBox { Width = 90, Text = _model.Number.ToString(CultureInfo.InvariantCulture), VerticalContentAlignment = VerticalAlignment.Top };
        box.LostFocus += (s, e) =>
        {
            if (decimal.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) { _model.Number = v; _onChanged(); }
            else box.Text = _model.Number.ToString(CultureInfo.InvariantCulture);
        };
        return box;
    }

    private UIElement BuildParameterUI()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = "$", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 2, 0), Foreground = Brushes.DarkSlateBlue });
        var cb = new ComboBox { Width = 130, IsEditable = false };
        var names = _parameterNamesProvider();
        foreach (var n in names) cb.Items.Add(n);
        cb.SelectedItem = _model.ParameterName;
        cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string v) { _model.ParameterName = v; _onChanged(); } };
        panel.Children.Add(cb);
        if (names.Count == 0) panel.Children.Add(new TextBlock { Text = "(none declared)", Foreground = Brushes.Firebrick, Margin = new Thickness(4, 4, 0, 0) });
        return panel;
    }

    private UIElement BuildReferenceUI()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = "Ref:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 4, 0), Foreground = Brushes.DarkMagenta });

        var cbId = new ComboBox { Width = 120, IsEditable = true };
        var names = _referenceNamesProvider();
        foreach (var n in names) cbId.Items.Add(n);
        cbId.SelectedItem = _model.ReferenceId;
        cbId.LostFocus += (s, e) => { _model.ReferenceId = cbId.Text; _onChanged(); };
        // FIXED: Changed 'cb' to 'cbId'
        cbId.SelectionChanged += (s, e) => { if (cbId.SelectedItem is string v) { _model.ReferenceId = v; _onChanged(); } };
        panel.Children.Add(cbId);

        panel.Children.Add(new TextBlock { Text = ".", Margin = new Thickness(4, 4, 4, 0) });

        var cbField = new ComboBox { Width = 90 };
        foreach (var f in new[] { "High", "Low", "Open", "Close", "Value", "AgeInBars" }) cbField.Items.Add(f);
        cbField.SelectedItem = _model.ReferenceField ?? "High";
        cbField.SelectionChanged += (s, e) => { if (cbField.SelectedItem is string v) { _model.ReferenceField = v; _onChanged(); } };
        panel.Children.Add(cbField);

        if (names.Count == 0) panel.Children.Add(new TextBlock { Text = "(none declared)", Foreground = Brushes.Firebrick, Margin = new Thickness(4, 4, 0, 0) });
        return panel;
    }

    private UIElement BuildIndicatorUI()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var descriptor = IndicatorCatalogService.Find(_model.Indicator ?? "Sma");
        bool isPivot = descriptor?.IsPivot == true;

        if (isPivot && _model.Timeframe is not ("D" or "W" or "M")) _model.Timeframe = "D";

        row.Children.Add(isPivot
            ? MakePivotTimeframeCombo(_model.Timeframe, v => { _model.Timeframe = v; _onChanged(); })
            : MakeTimeframeCombo(_model.Timeframe, v => { _model.Timeframe = v; _onChanged(); }));

        row.Children.Add(MakeOffsetBox(_model.Offset, v => { _model.Offset = v; _onChanged(); }, isPivot ? "Offset (periods)" : "Offset (bars)"));

        var indCombo = new ComboBox { Width = 140 };
        foreach (var d in IndicatorCatalogService.All) indCombo.Items.Add(d.Kind);
        indCombo.SelectedItem = _model.Indicator ?? "Sma";
        indCombo.SelectionChanged += (s, e) =>
        {
            if (indCombo.SelectedItem is not string v) return;
            _model.Indicator = v;
            _model.Params.Clear();
            var d = IndicatorCatalogService.Find(v);
            if (d is not null) foreach (var p in d.Parameters) _model.Params[p.Name] = p.Default;
            _model.Output = null;
            if (d?.IsPivot == true && _model.Timeframe is not ("D" or "W" or "M")) _model.Timeframe = "D";
            RebuildSub();
            _onChanged();
        };
        row.Children.Add(indCombo);

        row.Children.Add(new TextBlock { Text = "(", FontSize = 14, Margin = new Thickness(2, 2, 2, 0), VerticalAlignment = VerticalAlignment.Top });

        if (_model.Indicator == "Count")
        {
            _model.Source = null;
            _model.Predicate ??= new EditorNode { Kind = "condition", Enabled = true, Left = new EditorExpression { Kind = "field", Field = "Close" }, Operator = "GreaterThan", Right = new EditorExpression { Kind = "field", Field = "Open" } };

            var predicate = _model.Predicate;
            row.Children.Add(new ExpressionEditorControl(predicate.Left, _depth + 1, _parameterNamesProvider, _referenceNamesProvider, _onChanged));

            var op = new ComboBox { Width = 130, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var name in Enum.GetNames<AlgoTrading.Models.Operator>()) op.Items.Add(name);
            op.SelectedItem = predicate.Operator;
            op.SelectionChanged += (_, _) => { if (op.SelectedItem is string v) { predicate.Operator = v; _onChanged(); } };
            row.Children.Add(op);

            row.Children.Add(new ExpressionEditorControl(predicate.Right, _depth + 1, _parameterNamesProvider, _referenceNamesProvider, _onChanged));
            row.Children.Add(new TextBlock { Text = ", lookback:", Margin = new Thickness(4, 3, 2, 0) });

            var lookback = new TextBox { Width = 55, Text = _model.Lookback.ToString(CultureInfo.InvariantCulture) };
            lookback.LostFocus += (_, _) => { if (int.TryParse(lookback.Text, out var n) && n > 0) { _model.Lookback = n; _onChanged(); } else lookback.Text = _model.Lookback.ToString(CultureInfo.InvariantCulture); };
            row.Children.Add(lookback);

            var streak = new CheckBox { Content = "Streak", IsChecked = _model.Streak, Margin = new Thickness(6, 3, 4, 0) };
            streak.Checked += (_, _) => { _model.Streak = true; _onChanged(); };
            streak.Unchecked += (_, _) => { _model.Streak = false; _onChanged(); };
            row.Children.Add(streak);

            row.Children.Add(new TextBlock { Text = ")" });
            return row;
        }

        bool hasSource = descriptor is not null && (descriptor.SourceKind == "Single" || descriptor.SourceKind == "Optional");
        if (hasSource)
        {
            _model.Source ??= new EditorExpression { Kind = "field", Field = "Close" };
            row.Children.Add(new ExpressionEditorControl(_model.Source, _depth + 1, _parameterNamesProvider, _referenceNamesProvider, _onChanged));
        }
        else _model.Source = null;

        if (descriptor is not null && descriptor.Parameters.Count > 0)
        {
            for (int i = 0; i < descriptor.Parameters.Count; i++)
            {
                var p = descriptor.Parameters[i];
                if (!_model.Params.TryGetValue(p.Name, out var cur)) cur = p.Default;
                row.Children.Add(new TextBlock { Text = ",", Margin = new Thickness(2, 4, 2, 0), Foreground = Brushes.Gray });
                var tb = new TextBox { Width = 55, Text = cur.ToString(CultureInfo.InvariantCulture), ToolTip = p.Name };
                var localName = p.Name;
                tb.LostFocus += (s, e) => {
                    if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) { _model.Params[localName] = v; _onChanged(); }
                    else tb.Text = (_model.Params.TryGetValue(localName, out var cur2) ? cur2 : p.Default).ToString(CultureInfo.InvariantCulture);
                };
                row.Children.Add(tb);
            }
        }

        row.Children.Add(new TextBlock { Text = ")", FontSize = 14, Margin = new Thickness(2, 2, 2, 0), VerticalAlignment = VerticalAlignment.Top });

        if (descriptor is not null && descriptor.Outputs.Count > 1)
        {
            var outCb = new ComboBox { Width = 90, Margin = new Thickness(4, 0, 0, 0) };
            foreach (var o in descriptor.Outputs) outCb.Items.Add(o);
            outCb.SelectedItem = _model.Output ?? descriptor.Outputs[0];
            outCb.SelectionChanged += (s, e) => { if (outCb.SelectedItem is string v) { _model.Output = v; _onChanged(); } };
            row.Children.Add(outCb);
        }

        return row;
    }

    private UIElement BuildCalculationUI()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "(", FontSize = 14, Margin = new Thickness(0, 2, 2, 0), VerticalAlignment = VerticalAlignment.Top });
        _model.CalcLeft ??= new EditorExpression { Kind = "field", Field = "Close" };
        row.Children.Add(new ExpressionEditorControl(_model.CalcLeft, _depth + 1, _parameterNamesProvider, _referenceNamesProvider, _onChanged));
        row.Children.Add(new TextBlock { Text = ")", FontSize = 14, Margin = new Thickness(2, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top });

        var opCb = new ComboBox { Width = 100 };
        foreach (var op in new[] { "Add", "Subtract", "Multiply", "Divide", "Modulo", "Min", "Max" }) opCb.Items.Add(op);
        opCb.SelectedItem = _model.CalcOp ?? "Add";
        opCb.SelectionChanged += (s, e) => { if (opCb.SelectedItem is string v) { _model.CalcOp = v; _onChanged(); } };
        row.Children.Add(opCb);

        row.Children.Add(new TextBlock { Text = "(", FontSize = 14, Margin = new Thickness(6, 2, 2, 0), VerticalAlignment = VerticalAlignment.Top });
        _model.CalcRight ??= new EditorExpression { Kind = "number", Number = 0m };
        row.Children.Add(new ExpressionEditorControl(_model.CalcRight, _depth + 1, _parameterNamesProvider, _referenceNamesProvider, _onChanged));
        row.Children.Add(new TextBlock { Text = ")", FontSize = 14, Margin = new Thickness(2, 2, 0, 0), VerticalAlignment = VerticalAlignment.Top });
        return row;
    }

    private static ComboBox MakeTimeframeCombo(string? current, Action<string?> onChanged)
    {
        var cb = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 3, 0), ToolTip = "Timeframe" };
        cb.Items.Add("(inherit)");
        foreach (var (_, label) in TimeframeMap) cb.Items.Add(label);
        cb.SelectedItem = LabelFor(current);
        cb.SelectionChanged += (s, e) => { var v = cb.SelectedItem as string; if (v == "(inherit)") onChanged(null); else onChanged(KeyForLabel(v)); };
        return cb;
    }

    private static ComboBox MakePivotTimeframeCombo(string? current, Action<string?> onChanged)
    {
        var cb = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 3, 0), ToolTip = "Pivot source timeframe" };
        cb.Items.Add("Daily"); cb.Items.Add("Weekly"); cb.Items.Add("Monthly");
        cb.SelectedItem = current switch { "W" => "Weekly", "M" => "Monthly", _ => "Daily" };
        cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string v) onChanged(v switch { "Weekly" => "W", "Monthly" => "M", _ => "D" }); };
        return cb;
    }

    private static string LabelFor(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "(inherit)";
        foreach (var (k, label) in TimeframeMap) if (k == key) return label;
        return key;
    }

    private static string? KeyForLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return null;
        foreach (var (k, l) in TimeframeMap) if (l == label) return k;
        return label;
    }

    private static ComboBox MakeFieldCombo(string? current, Action<string> onChanged)
    {
        var cb = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 3, 0), ToolTip = "Field" };
        foreach (var f in new[] { "Open", "High", "Low", "Close", "Volume", "OI" }) cb.Items.Add(f);
        cb.SelectedItem = current ?? "Close";
        cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string v) onChanged(v); };
        return cb;
    }

    private static TextBox MakeOffsetBox(int current, Action<int> onChanged, string tooltip = "Offset (bars back)")
    {
        var tb = new TextBox { Width = 32, Text = current.ToString(CultureInfo.InvariantCulture), VerticalContentAlignment = VerticalAlignment.Top, ToolTip = tooltip };
        tb.LostFocus += (s, e) => { if (int.TryParse(tb.Text, out var v) && v >= 0) onChanged(v); else tb.Text = current.ToString(CultureInfo.InvariantCulture); };
        return tb;
    }
}