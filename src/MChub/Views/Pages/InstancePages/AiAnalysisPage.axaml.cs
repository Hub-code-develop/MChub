using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MChub.Core.Services;
using MChub.Core.Services.LogShare;
using MChub.Localization;
using MChub.Module;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common.Extensions;

namespace MChub.Views.Pages.InstancePages;

public partial class AiAnalysisPage : UserControl, ITioTabPage, INotifyPropertyChanged
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> AnalysisCache = new();

    private readonly LogShareBundle _bundle;
    private readonly string _displayName;
    private string _resultText = string.Empty;
    private string? _errorMessage;
    private bool _isAnalyzing;
    private bool _isComplete;
    private bool _isFailed;

    public AiAnalysisPage() : this(
        new LogShareBundle([], string.Empty, null, null, null, null), string.Empty)
    {
    }

    public AiAnalysisPage(LogShareBundle bundle, string displayName)
    {
        _bundle = bundle;
        _displayName = displayName;
        PageInfo = new PageInfo
        {
            Title = string.Format(CommonLanguageManager.Instance.aiAnalysis_pageTitle.CurrentValue(), displayName),
            IconGlyph = "\ue62b", IconFont = IconResources.FontFamilyName
        };
        InitializeComponent();
        DataContext = this;
        _ = AnalyzeAsync();
    }

    public string ResultText
    {
        get => _resultText;
        private set
        {
            if (_resultText == value) return;
            _resultText = value;
            OnPropertyChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (_isAnalyzing == value) return;
            _isAnalyzing = value;
            OnPropertyChanged();
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set
        {
            if (_isComplete == value) return;
            _isComplete = value;
            OnPropertyChanged();
        }
    }

    public bool IsFailed
    {
        get => _isFailed;
        private set
        {
            if (_isFailed == value) return;
            _isFailed = value;
            OnPropertyChanged();
        }
    }

    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnClose()
    {
        DataContext = null;
    }

    public static void Open(LogShareBundle bundle, string displayName, TopLevel sender)
    {
        if (sender is not TioTabWindowBase window)
            return;
        var tab = new TabEntry(window, new AiAnalysisPage(bundle, displayName));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private async Task AnalyzeAsync()
    {
        if (_bundle.Files.Count == 0)
            return;

        IsAnalyzing = true;
        IsComplete = false;
        IsFailed = false;

        try
        {
            var task = AnalysisCache.GetOrAdd(ComputeBundleKey(_bundle),
                _ => new Lazy<Task<string>>(() => LogSharingService.AnalyseAsync(_bundle, CancellationToken.None)))
                .Value;
            var result = await task;
            ResultText = result;
            IsComplete = true;
        }
        catch (Exception ex)
        {
            AnalysisCache.TryRemove(ComputeBundleKey(_bundle), out _);
            ErrorMessage = ex.Message;
            IsFailed = true;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static string ComputeBundleKey(LogShareBundle bundle)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in bundle.Files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Name));
            hash.AppendData(Encoding.UTF8.GetBytes(file.Content));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        await topLevel.Clipboard!.SetTextAsync(ResultText);
        topLevel.Notice(CommonLanguageManager.Instance.aiAnalysis_copied.CurrentValue(), NotificationType.Success);
    }

    private void Retry_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = AnalyzeAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
