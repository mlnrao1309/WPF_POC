using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AlgoTrading.RuleEditor.Services;

namespace AlgoTrading.RuleEditor.Windows;

public partial class InstrumentPickerWindow : Window
{
    private readonly InstrumentLookupService _lookup;
    private readonly bool _allowMultiSelect;

    public uint? SelectedToken { get; private set; }
    public string? SelectedSymbol { get; private set; }
    public IReadOnlyList<InstrumentInfoDto> SelectedInstruments { get; private set; }
        = Array.Empty<InstrumentInfoDto>();

    public InstrumentPickerWindow(InstrumentLookupService lookup, bool allowMultiSelect = false)
    {
        _lookup = lookup;
        _allowMultiSelect = allowMultiSelect;
        InitializeComponent();

        if (_allowMultiSelect)
        {
            Grid.SelectionMode = DataGridSelectionMode.Extended;
            MultiHint.Visibility = Visibility.Visible;
        }

        Loaded += (s, e) =>
        {
            SearchBox.Focus();
            RefreshResults();
        };
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => RefreshResults();

    private void RefreshResults()
    {
        var results = _lookup.Search(SearchBox.Text ?? "", 200);
        Grid.ItemsSource = results;
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_allowMultiSelect) return;
        var count = Grid.SelectedItems.Count;
        SelCountText.Text = count == 0 ? "" : count + " selected";
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e) => Commit();

    private void OnOkClick(object sender, RoutedEventArgs e) => Commit();

    private void Commit()
    {
        if (_allowMultiSelect)
        {
            var list = new List<InstrumentInfoDto>();
            foreach (var item in Grid.SelectedItems)
                if (item is InstrumentInfoDto dto) list.Add(dto);
            if (list.Count == 0) { DialogResult = false; Close(); return; }
            SelectedInstruments = list;
            DialogResult = true;
            Close();
            return;
        }

        if (Grid.SelectedItem is InstrumentInfoDto single)
        {
            SelectedToken = single.Token;
            SelectedSymbol = single.Symbol;
            DialogResult = true;
        }
        else
        {
            DialogResult = false;
        }
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}