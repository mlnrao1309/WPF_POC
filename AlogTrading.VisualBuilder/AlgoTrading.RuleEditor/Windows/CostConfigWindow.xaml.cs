using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace AlgoTrading.RuleEditor.Windows;

public partial class CostConfigWindow : Window
{
    private static readonly (string Key, string Label)[] Fields =
    {
        ("brokeragePerOrder",   "Brokerage per order (Rs)"),
        ("sttBuyPct",           "STT buy (%)"),
        ("sttSellPct",          "STT sell (%)"),
        ("exchangeTxnPct",      "Exchange txn (%)"),
        ("sebiRupeesPerCrore",  "SEBI turnover (Rs/crore)"),
        ("stampDutyBuyPct",     "Stamp duty buy (%)"),
        ("gstPct",              "GST (%)")
    };

    private static readonly Dictionary<string, decimal> DeliveryDefaults = new()
    {
        ["brokeragePerOrder"] = 0m,
        ["sttBuyPct"] = 0.100m,
        ["sttSellPct"] = 0.100m,
        ["exchangeTxnPct"] = 0.00345m,
        ["sebiRupeesPerCrore"] = 10m,
        ["stampDutyBuyPct"] = 0.015m,
        ["gstPct"] = 18m
    };

    private static readonly Dictionary<string, decimal> IntradayDefaults = new()
    {
        ["brokeragePerOrder"] = 20m,
        ["sttBuyPct"] = 0m,
        ["sttSellPct"] = 0.025m,
        ["exchangeTxnPct"] = 0.00345m,
        ["sebiRupeesPerCrore"] = 10m,
        ["stampDutyBuyPct"] = 0.003m,
        ["gstPct"] = 18m
    };

    private readonly string _configPath;
    private readonly Dictionary<string, TextBox> _deliveryBoxes = new();
    private readonly Dictionary<string, TextBox> _intradayBoxes = new();

    public CostConfigWindow(string configPath)
    {
        _configPath = configPath;
        InitializeComponent();
        PathLabel.Text = "Config: " + configPath;
        BuildGrids();
        LoadFromDisk();
    }

    private void BuildGrids()
    {
        for (int i = 0; i < Fields.Length; i++)
        {
            var (key, label) = Fields[i];
            AddRow(DeliveryGrid, i, label, key, _deliveryBoxes);
            AddRow(IntradayGrid, i, label, key, _intradayBoxes);
        }
    }

    private static void AddRow(Grid grid, int row, string label, string key,
                               Dictionary<string, TextBox> bag)
    {
        if (grid.ColumnDefinitions.Count == 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 8, 6)
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        var tb = new TextBox
        {
            Margin = new Thickness(0, 6, 0, 6),
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        bag[key] = tb;
    }

    private void LoadFromDisk()
    {
        var delivery = new Dictionary<string, decimal>(DeliveryDefaults);
        var intraday = new Dictionary<string, decimal>(IntradayDefaults);

        if (File.Exists(_configPath))
        {
            try
            {
                var text = File.ReadAllText(_configPath);
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("equity", out var eq))
                {
                    if (eq.TryGetProperty("delivery", out var d))
                        delivery = ReadSection(d, DeliveryDefaults);
                    if (eq.TryGetProperty("intraday", out var i))
                        intraday = ReadSection(i, IntradayDefaults);
                }
            }
            catch { }
        }

        Fill(_deliveryBoxes, delivery);
        Fill(_intradayBoxes, intraday);
    }

    private static Dictionary<string, decimal> ReadSection(JsonElement el,
                                                           Dictionary<string, decimal> defaults)
    {
        var result = new Dictionary<string, decimal>(defaults);
        foreach (var key in defaults.Keys.ToList())
        {
            if (el.TryGetProperty(key, out var v) &&
                v.ValueKind == JsonValueKind.Number &&
                v.TryGetDecimal(out var dec))
            {
                result[key] = dec;
            }
        }
        return result;
    }

    private static void Fill(Dictionary<string, TextBox> boxes, Dictionary<string, decimal> values)
    {
        foreach (var (key, value) in values)
        {
            if (boxes.TryGetValue(key, out var tb))
                tb.Text = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var delivery = ReadBoxes(_deliveryBoxes);
            var intraday = ReadBoxes(_intradayBoxes);

            var json = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["equity"] = new JsonObject
                {
                    ["delivery"] = ToJson(delivery),
                    ["intraday"] = ToJson(intraday)
                }
            };

            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var text = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, text);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to save: " + ex.Message, "Save",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Dictionary<string, decimal> ReadBoxes(Dictionary<string, TextBox> boxes)
    {
        var d = new Dictionary<string, decimal>();
        foreach (var (key, tb) in boxes)
        {
            if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                d[key] = v;
            else
                d[key] = 0m;
        }
        return d;
    }

    private static JsonObject ToJson(Dictionary<string, decimal> values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values) obj[key] = value;
        return obj;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}