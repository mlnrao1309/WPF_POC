// Dsl/Lexer.cs
using System.Globalization;

namespace AlgoTrading.Screener.Dsl;

public static class Lexer
{
    public static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "from", "in", "let", "where", "orderby", "asc", "desc", "select", "as", "take","per",

        "and", "or", "not",
        "true", "false",

        // comparison keyword-operands
        "greaterthan", "greaterthanorequal", "greaterthanequalto", "greaterthanorequalto",
        "lessthan", "lessthanorequal", "lessthanequalto",
        "equals", "notequals",
        "between",

        // price action operators
        "crossedabove", "crossedbelow", "crossedover", "crossedunder",
        "closedabove", "closedbelow",
        "touchedabove", "touchedbelow"
    };

    public static List<Tok> Lex(string source)
    {
        var list = new List<Tok>();
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            // // line comment
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            // string literal
            if (c is '"' or '\'')
            {
                char q = c; int start = ++i;
                while (i < source.Length && source[i] != q) i++;
                if (i >= source.Length) throw new FormatException($"Unterminated string at {start - 1}");
                list.Add(new Tok(TokKind.String, source[start..i], start - 1, source[start..i]));
                i++;
                continue;
            }

            // number
            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                int start = i;
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                var raw = source[start..i];
                list.Add(new Tok(TokKind.Number, raw, start, decimal.Parse(raw, CultureInfo.InvariantCulture)));
                continue;
            }

            // identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                var text = source[start..i];
                var kind = Keywords.Contains(text) ? TokKind.Keyword : TokKind.Ident;
                list.Add(new Tok(kind, text, start, text));
                continue;
            }

            // two-char operators
            if (i + 1 < source.Length)
            {
                string two = source.Substring(i, 2);
                if (two is "==" or "!=" or ">=" or "<=" or "&&" or "||")
                {
                    list.Add(new Tok(TokKind.Op, two, i, two)); i += 2; continue;
                }
            }

            if ("+-*/%><=!".Contains(c))
            {
                list.Add(new Tok(TokKind.Op, c.ToString(), i, c.ToString()));
                i++;
                continue;
            }

            if ("()[],.".Contains(c))
            {
                list.Add(new Tok(TokKind.Punct, c.ToString(), i, c.ToString()));
                i++;
                continue;
            }

            throw new FormatException($"Unexpected character '{c}' at {i}");
        }

        list.Add(new Tok(TokKind.Eof, "", source.Length));
        return list;
    }
}