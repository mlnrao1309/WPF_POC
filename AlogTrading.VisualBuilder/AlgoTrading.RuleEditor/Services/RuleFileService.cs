using System.IO;
using System.Text;
using AlgoTrading.Models;

namespace AlgoTrading.RuleEditor.Services;

public sealed class RuleFileService
{
    public RuleDefinition Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path);
        return RuleDefinitionJson.Deserialize(json);
    }

    public void Save(RuleDefinition rule, string path)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var json = RuleDefinitionJson.Serialize(rule);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public static string ComputeSavePath(
        string strategiesFolder, string strategyName,
        uint? instrumentToken, Timeframe? targetTimeframe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategiesFolder);
        var safeName = SanitizeName(strategyName);

        string fileName;
        if (instrumentToken.HasValue && targetTimeframe.HasValue)
        {
            fileName = safeName + "_" + instrumentToken.Value + "_" +
                       targetTimeframe.Value.ToString() + ".json";
        }
        else
        {
            fileName = safeName + ".json";
        }
        return Path.Combine(strategiesFolder, fileName);
    }

    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "strategy";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
            else sb.Append('_');
        }
        var s = sb.ToString();
        return s.Length > 80 ? s[..80] : s;
    }
}