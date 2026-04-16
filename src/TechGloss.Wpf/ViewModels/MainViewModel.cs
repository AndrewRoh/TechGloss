using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.Wpf.Bridge;

namespace TechGloss.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IGlossaryClient _glossary;
    private readonly TranslationOrchestrator _orchestrator;
    private string _sourceText = "";
    private string _lookupQuery = "";
    private string _statusMessage = "준비";
    private Brush _statusBrush = Brushes.Gray;
    private TranslationDirection _selectedDirection = TranslationDirection.EnToKo;
    private CancellationTokenSource? _cts;

    public event Action? TranslationStarted;
    public event Action<string>? ChunkReceived;
    public event Action? CopyRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(IGlossaryClient glossary, TranslationOrchestrator orchestrator)
    {
        _glossary = glossary;
        _orchestrator = orchestrator;
        TranslateCommand = new RelayCommand(async _ => await ExecuteTranslateAsync(), _ => !IsTranslating);
        CopyResultCommand = new RelayCommand(_ => CopyRequested?.Invoke());
    }

    public ICommand TranslateCommand { get; }
    public ICommand CopyResultCommand { get; }

    public ObservableCollection<GlossaryLookupRow> LookupResults { get; } = new();

    public IReadOnlyList<TranslationDirection> Directions { get; } =
        [TranslationDirection.EnToKo, TranslationDirection.KoToEn];

    public bool IsTranslating { get; private set; }

    public TranslationDirection SelectedDirection
    {
        get => _selectedDirection;
        set { _selectedDirection = value; OnPropertyChanged(); }
    }

    public string SourceText
    {
        get => _sourceText;
        set { _sourceText = value; OnPropertyChanged(); }
    }

    public string LookupQuery
    {
        get => _lookupQuery;
        set
        {
            _lookupQuery = value;
            OnPropertyChanged();
            _ = ExecuteLookupAsync(value);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set { _statusBrush = value; OnPropertyChanged(); }
    }

    private async Task ExecuteTranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceText)) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsTranslating = true;
        CommandManager.InvalidateRequerySuggested();
        SetStatus("번역 중...", isError: false);
        TranslationStarted?.Invoke();

        var (sourceLang, targetLang) = SelectedDirection.ToLangPair();
        try
        {
            await _orchestrator.RunStreamingAsync(
                SourceText, sourceLang, targetLang, categorySlug: null,
                new Progress<string>(chunk => ChunkReceived?.Invoke(chunk)),
                _cts.Token);
            SetStatus("번역 완료", isError: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("번역 취소됨", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"오류: {ex.Message}", isError: true);
        }
        finally
        {
            IsTranslating = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ExecuteLookupAsync(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            LookupResults.Clear();
            return;
        }
        try
        {
            var rows = await _glossary.LookupAsync(q);
            LookupResults.Clear();
            foreach (var r in rows) LookupResults.Add(r);
        }
        catch { /* 조회 실패 무시 */ }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusBrush = isError ? Brushes.Red : Brushes.Gray;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        : this(p => { execute(p); return Task.CompletedTask; }, canExecute) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _ = _execute(parameter);
}
