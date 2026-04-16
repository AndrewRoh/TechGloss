using System.Windows;
using System.Windows.Documents;
using TechGloss.Wpf.ViewModels;

namespace TechGloss.Wpf;

public partial class MainWindow : Window
{
    private Paragraph? _currentParagraph;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.TranslationStarted += OnTranslationStarted;
        vm.ChunkReceived += OnChunkReceived;
        vm.CopyRequested += OnCopyRequested;
    }

    private void OnTranslationStarted()
    {
        Dispatcher.Invoke(() =>
        {
            resultRichTextBox.Document.Blocks.Clear();
            _currentParagraph = new Paragraph();
            resultRichTextBox.Document.Blocks.Add(_currentParagraph);
        });
    }

    private void OnChunkReceived(string chunk)
    {
        Dispatcher.Invoke(() =>
        {
            _currentParagraph ??= new Paragraph();
            if (!resultRichTextBox.Document.Blocks.Contains(_currentParagraph))
                resultRichTextBox.Document.Blocks.Add(_currentParagraph);
            _currentParagraph.Inlines.Add(new Run(chunk));
            resultRichTextBox.ScrollToEnd();
        });
    }

    private void OnCopyRequested()
    {
        var range = new TextRange(
            resultRichTextBox.Document.ContentStart,
            resultRichTextBox.Document.ContentEnd);
        var text = range.Text.Trim();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }
}
