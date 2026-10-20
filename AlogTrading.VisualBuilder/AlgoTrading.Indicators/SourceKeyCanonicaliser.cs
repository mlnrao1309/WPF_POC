using System;
using System.Globalization;
using System.Text;
using AlgoTrading.Models;

namespace AlgoTrading.Indicators;

/// <summary>
/// Produces a deterministic, culture-invariant string for a ValueExpression tree.
/// Used as the SourceKey component of IndicatorCacheKey.
/// </summary>
public static class SourceKeyCanonicaliser
{
    public static string Canonicalise(ValueExpression expr)
    {
        if (expr == null) throw new ArgumentNullException(nameof(expr));
        var sb = new StringBuilder(64);
        Append(sb, expr);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, ValueExpression expr)
    {
        switch (expr)
        {
            case FieldExpression f:
                sb.Append("field:");
                sb.Append(f.Timeframe?.ToString() ?? "default");
                sb.Append('@').Append(f.Offset);
                sb.Append(':').Append(f.Field);
                break;

            case NumberExpression n:
                sb.Append("number:");
                sb.Append(n.Value.ToString("G29", CultureInfo.InvariantCulture));
                break;

            case ParameterExpression p:
                sb.Append("parameter:").Append(p.Name);
                break;

            case IndicatorExpression ind:
                sb.Append("indicator:");
                sb.Append(ind.Timeframe?.ToString() ?? "default");
                sb.Append('@').Append(ind.Offset);
                sb.Append(':').Append(ind.Indicator);
                sb.Append(':').Append(ind.Output ?? string.Empty);
                sb.Append(':');
                if (ind.Params != null && ind.Params.Count > 0)
                {
                    var keys = new string[ind.Params.Count];
                    ind.Params.Keys.CopyTo(keys, 0);
                    Array.Sort(keys, StringComparer.Ordinal);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (i > 0) sb.Append(';');
                        sb.Append(keys[i]).Append('=')
                          .Append(ind.Params[keys[i]].ToString("G29", CultureInfo.InvariantCulture));
                    }
                }
                sb.Append(':');
                if (ind.Source != null) Append(sb, ind.Source);
                break;

            case CalculationExpression c:
                sb.Append("calculation:").Append(c.Op).Append(':');
                Append(sb, c.Left);
                sb.Append('|');
                Append(sb, c.Right);
                break;

            default:
                sb.Append("unknown:").Append(expr.GetType().Name);
                break;
        }
    }
}