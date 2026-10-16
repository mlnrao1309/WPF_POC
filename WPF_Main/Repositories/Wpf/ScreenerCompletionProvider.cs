// Wpf/ScreenerCompletionProvider.cs
using AlgoTrading.Screener.Dsl;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;

namespace AlgoTrading.Screener.Wpf;

/// <summary>
/// Builds the list of completion items for the current caret context.
/// AvalonEdit has no provider interface — this is just a helper
/// that ScreenerTextBox calls to fill CompletionWindow.
/// </summary>
public sealed class ScreenerCompletionProvider
{
    private readonly CompletionCatalog _catalog;
    public ScreenerCompletionProvider(CompletionCatalog catalog) => _catalog = catalog;

    public IEnumerable<ICompletionData> GetCompletions(TextArea area)
    {
        string text = area.Document.Text;
        int caret = area.Caret.Offset;
        string prefix = ReadPrefix(text, caret);

        // context 1: member access `s.`
        if (caret > 0 && text[caret - 1] == '.')
        {
            return _catalog.Fields.Select(f => new ScreenerCompletionData(f, "field", ""));
        }

        // context 2: "from x in "
        if (IsAfterKeyword(text, caret, "in"))
        {
            return _catalog.Collections
                .Select(c => new ScreenerCompletionData(c, "collection", ""));
        }

        // inside GetCompletions, before the general keyword fallback:
        if (IsAfterKeyword(text, caret, "per"))
        {
            return new[]
            {
        new ScreenerCompletionData("bar",    "keyword", "one row per matching bar"),
        new ScreenerCompletionData("symbol", "keyword", "one row per matching symbol (default)")
    };
        }

        var list = new List<ICompletionData>();

        list.AddRange(_catalog.Keywords.Select(k => new ScreenerCompletionData(k, "keyword", "")));
        list.AddRange(_catalog.ContextVars.Select(v => new ScreenerCompletionData(v, "variable", "")));
        list.AddRange(_catalog.Functions.Select(f =>
            new ScreenerCompletionData(f.Name, "function", f.Signature)));

        return list.Where(c => prefix.Length == 0
            || c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadPrefix(string text, int caret)
    {
        int i = caret;
        while (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')) i--;
        return text[i..caret];
    }

    private static bool IsAfterKeyword(string text, int caret, string kw)
    {
        if (caret == 0) return false;
        var tokens = Lexer.Lex(text[..caret]);
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            if (tokens[i].Kind == TokKind.Eof) continue;
            return string.Equals(tokens[i].Text, kw, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

}