using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace AlgoTrading.Screener.Wpf;

public sealed class ScreenerTextBox : UserControl
{
    private readonly TextEditor _editor = new();
    private CompletionWindow? _window;

    public TextEditor Editor => _editor;
    public CompletionCatalog Catalog { get; set; } = new();

    /// <summary>Fired by Ctrl+Enter or by the host window's Run button.</summary>
    public event EventHandler? RunRequested;
    public bool IsRunEnabled { get; set; } = true;

    public string Query
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    public ScreenerTextBox()
    {
        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.PreviewKeyDown += OnPreviewKeyDown;

        Content = _editor;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsRunEnabled) return;

        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            RunRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text is not { Length: > 0 }) return;
        char c = e.Text[0];

        if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
        {
            if (_window is null) OpenWindow();
        }
        else
        {
            _window?.Close();
        }
    }

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (_window is null || e.Text.Length == 0) return;
        char c = e.Text[0];

        if (char.IsLetterOrDigit(c) || c == '_' || c == '.' ||
            c == ' ' || c == '(' || c == '[')
            return;

        _window.CompletionList.RequestInsertion(e);
    }

    private void OpenWindow()
    {
        var provider = new ScreenerCompletionProvider(Catalog);

        // 1. Use _editor.CaretOffset instead of .Caret
        int caretOffset = _editor.CaretOffset;
        var text = _editor.Text;

        // 2. Find the start of the word being typed
        int startOffset = caretOffset;
        while (startOffset > 0 && (char.IsLetterOrDigit(text[startOffset - 1]) || text[startOffset - 1] == '_'))
        {
            startOffset--;
        }

        // 3. Pass the StartOffset and EndOffset to the window
        _window = new CompletionWindow(_editor.TextArea)
        {
            CloseWhenCaretAtBeginning = true,
            Width = 320,
            StartOffset = startOffset,   // <--- Replaces the word you already typed
            EndOffset = caretOffset      // <--- Stops at the current cursor
        };

        foreach (var item in provider.GetCompletions(_editor.TextArea))
            _window.CompletionList.CompletionData.Add(item);

        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
