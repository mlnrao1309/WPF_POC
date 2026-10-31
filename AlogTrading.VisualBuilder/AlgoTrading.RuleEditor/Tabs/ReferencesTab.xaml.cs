using System.Windows;
using System.Windows.Controls;
using AlgoTrading.RuleEditor.Controls;
using AlgoTrading.RuleEditor.Models;
using AlgoTrading.RuleEditor.Services;

namespace AlgoTrading.RuleEditor.Tabs;

public partial class ReferencesTab : UserControl
{
    public event EventHandler? Changed;
    private List<EditorReferenceBlock> _references = new();
    private MainWindow? _owner;

    public ReferencesTab() => InitializeComponent();
    public void Configure(MainWindow owner) => _owner = owner;
    public void Bind(EditorStrategy? strategy)
    {
        _references = strategy is null ? new() : ConditionTreeService.LoadReferences(strategy.Rule);
        RebuildUI();
    }

    public List<EditorReferenceBlock> GetReferences() => _references;

    private void RebuildUI()
    {
        ItemsPanel.Children.Clear();

        foreach (var block in _references.ToArray())
        {
            var card = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0xF9, 0xF9))
            };

            var layout = new StackPanel();

            // Block Header
            var header = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock { Text = "Block Name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var nameBox = new TextBox { Width = 120, Text = block.Name, Margin = new Thickness(0, 0, 12, 0) };
            nameBox.LostFocus += (s, e) => { block.Name = nameBox.Text; RaiseChanged(); };
            header.Children.Add(nameBox);

            header.Children.Add(new TextBlock { Text = "Timeframe:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var tfBox = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 12, 0) };
            foreach (var tf in new[] { "1m", "5m", "15m", "30m", "1H", "D", "W", "M" }) tfBox.Items.Add(tf);
            tfBox.SelectedItem = block.Timeframe;
            tfBox.SelectionChanged += (s, e) => { if (tfBox.SelectedItem is string v) { block.Timeframe = v; RaiseChanged(); } };
            header.Children.Add(tfBox);

            var delBtn = new Button { Content = "Delete Block", Padding = new Thickness(8, 2, 8, 2) };
            delBtn.Click += (s, e) => { _references.Remove(block); RebuildUI(); RaiseChanged(); };
            header.Children.Add(delBtn);
            layout.Children.Add(header);

            // Trigger
            layout.Children.Add(new TextBlock { Text = "Trigger (When to capture):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
            var triggerCtrl = new ConditionGroupControl(
                model: block.Trigger, depth: 0, isRoot: true,
                parameterNamesProvider: () => _owner?.CurrentParameterNames() ?? Array.Empty<string>(),
                referenceNamesProvider: () => _owner?.CurrentReferenceNames() ?? Array.Empty<string>(),
                onChanged: RaiseChanged, onDelete: null);
            layout.Children.Add(triggerCtrl);

            // Captures
            layout.Children.Add(new TextBlock { Text = "Captures (Variables to assign):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });

            var capturesPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 0, 0, 0) };
            foreach (var capture in block.Captures.ToArray())
            {
                var capRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                capRow.Children.Add(new TextBlock { Text = "let", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });

                var idBox = new TextBox { Width = 100, Text = capture.Id, Margin = new Thickness(0, 0, 4, 0) };
                idBox.LostFocus += (s, e) => { capture.Id = idBox.Text; RaiseChanged(); };
                capRow.Children.Add(idBox);

                capRow.Children.Add(new TextBlock { Text = "=", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });

                var valCtrl = new ExpressionEditorControl(
                    model: capture.Value, depth: 0,
                    parameterNamesProvider: () => _owner?.CurrentParameterNames() ?? Array.Empty<string>(),
                    referenceNamesProvider: () => _owner?.CurrentReferenceNames() ?? Array.Empty<string>(),
                    onChanged: RaiseChanged);
                capRow.Children.Add(valCtrl);

                var capDelBtn = new Button { Content = "X", Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0) };
                capDelBtn.Click += (s, e) => { block.Captures.Remove(capture); RebuildUI(); RaiseChanged(); };
                capRow.Children.Add(capDelBtn);

                capturesPanel.Children.Add(capRow);
            }
            layout.Children.Add(capturesPanel);

            var addCapBtn = new Button { Content = "+ Capture Variable", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 4, 0, 0) };
            addCapBtn.Click += (s, e) =>
            {
                block.Captures.Add(new EditorReferenceCapture { Id = "var" + (block.Captures.Count + 1), Value = new EditorExpression { Kind = "field", Field = "Close" } });
                RebuildUI();
                RaiseChanged();
            };
            layout.Children.Add(addCapBtn);

            card.Child = layout;
            ItemsPanel.Children.Add(card);
        }
    }

    private void OnAddReferenceClick(object sender, RoutedEventArgs e)
    {
        _references.Add(new EditorReferenceBlock
        {
            Name = "Ref" + (_references.Count + 1),
            Timeframe = "15m",
            Trigger = new EditorNode { Kind = "group", Logic = "All", Enabled = true, Children = { new EditorNode { Kind = "condition", Enabled = true, Left = new EditorExpression { Kind = "field", Field = "Close" }, Operator = "CrossedAbove", Right = new EditorExpression { Kind = "number", Number = 0m } } } },
            Captures = { new EditorReferenceCapture { Id = "var1", Value = new EditorExpression { Kind = "field", Field = "High" } } }
        });
        RebuildUI();
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}