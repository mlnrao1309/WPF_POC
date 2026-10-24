using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AlgoTrading.RuleEditor.Models;

namespace AlgoTrading.RuleEditor.Controls;

public partial class ParameterPanel : UserControl
{
    private readonly ObservableCollection<EditorParameter> _rows = new();
    public event EventHandler? Changed;

    public ParameterPanel()
    {
        InitializeComponent();
        ParamItems.ItemsSource = _rows;
        _rows.CollectionChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Bind(IEnumerable<EditorParameter> parameters)
    {
        _rows.Clear();
        foreach (var p in parameters) _rows.Add(p);
    }

    public IReadOnlyList<EditorParameter> GetParameters() => _rows.ToList();

    private void OnParameterSourceUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        _rows.Add(new EditorParameter
        {
            Name = "p" + (_rows.Count + 1),
            Default = 0m,
            Minimum = 0m,
            Maximum = 100m,
            Step = 1m
        });
    }

    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is EditorParameter p)
            _rows.Remove(p);
    }
}
