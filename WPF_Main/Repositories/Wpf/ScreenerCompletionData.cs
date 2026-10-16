// Wpf/ScreenerCompletionData.cs
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace AlgoTrading.Screener.Wpf;

public sealed class ScreenerCompletionData : ICompletionData
{
    public ScreenerCompletionData(string text, string kind, string description)
    {
        Text = text; Kind = kind; Description = description;
    }

    public string Text { get; }
    public string Kind { get; }
    public object Description { get; }
    public object Content => Text;
    public double Priority => 0;
    public ImageSource? Image => null;

    public void Complete(TextArea area, ISegment segment, EventArgs e)
    {
        area.Document.Replace(segment.Offset, segment.Length, Text);

        // if it's a function, add "()" and place the caret inside
        if (Kind == "function")
        {
            int caret = area.Caret.Offset;
            area.Document.Insert(caret, "()");
            area.Caret.Offset = caret + 1;
        }
    }
}