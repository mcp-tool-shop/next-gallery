using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallery.Domain.Index;
using Gallery.Domain.Routing;

namespace Gallery.App.ViewModels;

/// <summary>
/// ViewModel for CodeComfy workspace mode.
/// Pure projection of GalleryState from IndexLoader.
/// </summary>
public partial class CodeComfyViewModel : ObservableObject, IDisposable
{
    private readonly IndexLoader _indexLoader;
    private readonly IFileReader _fileReader;
    private readonly string _workspaceRoot;
    private readonly string _workspaceKey;

    private CancellationTokenSource? _pollCts;
    private int _consecutiveFailures;
    private const int MaxFailuresBeforeBackoff = 3;
    private DateTime _lastPollTime;
    private bool _disposed;

    // Cached state for transient error recovery
    private IReadOnlyList<JobRow>? _lastKnownGood;

    [ObservableProperty]
    private CodeComfyViewState _currentCodeComfyViewState = CodeComfyViewState.Loading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private FatalReason? _fatalReason;

    [ObservableProperty]
    private ObservableCollection<JobRow> _jobs = new();

    [ObservableProperty]
    private JobRow? _selectedJob;

    [ObservableProperty]
    private BannerInfo _banner = BannerInfo.None;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isPollingEnabled = true;

    [ObservableProperty]
    private string _workspacePath;

    [ObservableProperty]
    private bool _isDiagnosticsVisible;

    [ObservableProperty]
    private string _diagnosticsText = "";

    public string WorkspaceKey => _workspaceKey;

    public CodeComfyViewModel(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
        _workspacePath = workspaceRoot;
        _workspaceKey = Domain.WorkspaceKey.ComputeKey(workspaceRoot);
        _fileReader = RealFileReader.Instance;
        _indexLoader = new IndexLoader(_fileReader);
    }

    /// <summary>
    /// Initialize and perform first load.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshAsync();
        StartPolling();
    }

    /// <summary>
    /// Refresh from index file.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed) return;

        IsRefreshing = true;

        try
        {
            await Task.Run(() =>
            {
                if (_disposed) return;
                var result = _indexLoader.Load(_workspaceRoot, _lastKnownGood);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_disposed) return;
                    ApplyResult(result);
                });
            });

            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_disposed) return;
                Banner = BannerInfo.Warning($"Refresh failed: {ex.Message}");
            });
        }
        finally
        {
            if (!_disposed)
            {
                IsRefreshing = false;
                _lastPollTime = DateTime.UtcNow;
            }
        }
    }

    private void ApplyResult(IndexLoadResult result)
    {
        Banner = result.Banner;

        switch (result.State)
        {
            case GalleryState.Loading:
                CurrentCodeComfyViewState = CodeComfyViewState.Loading;
                break;

            case GalleryState.Empty empty:
                CurrentCodeComfyViewState = CodeComfyViewState.Empty;
                Jobs.Clear();
                break;

            case GalleryState.List list:
                CurrentCodeComfyViewState = CodeComfyViewState.List;
                Jobs = new ObservableCollection<JobRow>(list.Items);
                _lastKnownGood = list.Items;

                // Auto-select first if no selection
                if (SelectedJob == null && Jobs.Count > 0)
                {
                    SelectedJob = Jobs[0];
                }
                break;

            case GalleryState.Fatal fatal:
                CurrentCodeComfyViewState = CodeComfyViewState.Fatal;
                ErrorMessage = fatal.Message;
                FatalReason = fatal.Reason;
                break;
        }
    }

    #region Polling

    private void StartPolling()
    {
        if (_pollCts != null) return;

        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollCts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);

                if (!IsPollingEnabled) continue;
                if (_consecutiveFailures >= MaxFailuresBeforeBackoff) continue;

                // Check if file changed before reloading
                var indexPath = Path.Combine(_workspaceRoot, ".codecomfy", "outputs", "index.json");
                if (_fileReader.FileExists(indexPath))
                {
                    var lastWrite = _fileReader.GetLastWriteTimeUtc(indexPath);
                    if (lastWrite > _lastPollTime)
                    {
                        await RefreshAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                _consecutiveFailures++;
            }
        }
    }

    public void StopPolling()
    {
        if (_pollCts == null) return;

        try
        {
            _pollCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, safe to ignore
        }

        try
        {
            _pollCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, safe to ignore
        }

        _pollCts = null;
    }

    #endregion

    #region Focus Events

    /// <summary>
    /// Called when window gains focus.
    /// </summary>
    public async Task OnWindowActivatedAsync()
    {
        // Reset failure count on focus
        _consecutiveFailures = 0;
        await RefreshAsync();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task OpenOutputsFolderAsync()
    {
        var outputsPath = Path.Combine(_workspaceRoot, ".codecomfy", "outputs");
        if (Directory.Exists(outputsPath))
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(outputsPath)
            });
        }
        else
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(_workspaceRoot)
            });
        }
    }

    [RelayCommand]
    private void SelectJob(JobRow? job)
    {
        SelectedJob = job;
    }

    [RelayCommand]
    private void ToggleDiagnostics()
    {
        IsDiagnosticsVisible = !IsDiagnosticsVisible;
        if (IsDiagnosticsVisible)
        {
            UpdateDiagnosticsText();
        }
    }

    private void UpdateDiagnosticsText()
    {
        var indexPath = Path.Combine(_workspaceRoot, ".codecomfy", "outputs", "index.json");
        var indexExists = _fileReader.FileExists(indexPath);
        var lastWriteTime = indexExists ? _fileReader.GetLastWriteTimeUtc(indexPath) : (DateTime?)null;
        var canonPath = Domain.WorkspaceKey.NormalizePath(_workspaceRoot);

        var pollingStatus = _consecutiveFailures >= MaxFailuresBeforeBackoff
            ? "manual-backoff"
            : IsPollingEnabled ? "active" : "paused";

        var skippedCount = Banner.SkippedCount;

        DiagnosticsText = $"""
            === CodeComfy Diagnostics ===

            Workspace Path: {_workspaceRoot}
            Canon Path:     {canonPath}
            Workspace Key:  {_workspaceKey}
            Pipe Name:      \\.\pipe\codecomfy.nextgallery.{_workspaceKey}

            Index Path:     {indexPath}
            Index Exists:   {indexExists}
            Last Write:     {lastWriteTime?.ToString("o") ?? "N/A"}
            Last Poll:      {(_lastPollTime == default ? "never" : _lastPollTime.ToString("o"))}

            Polling Status: {pollingStatus}
            Failures:       {_consecutiveFailures}/{MaxFailuresBeforeBackoff}
            Jobs Loaded:    {Jobs.Count}
            Skipped:        {skippedCount}
            View State:     {CurrentCodeComfyViewState}
            """;
    }

    #endregion

    #region Activation Handler Integration

    /// <summary>
    /// Handle activation from another instance.
    /// Returns response for IPC.
    /// </summary>
    public ActivationResponsePayload HandleActivation(
        ActivationRequestPayload request,
        IWindowManager windowManager)
    {
        var result = ActivationHandler.HandleSecondInstanceActivation(
            request,
            _workspaceKey,
            windowManager,
            new ViewModelIndexLoader(this));

        return ActivationHandler.ToResponsePayload(result);
    }

    private sealed class ViewModelIndexLoader : IIndexLoader
    {
        private readonly CodeComfyViewModel _vm;
        public ViewModelIndexLoader(CodeComfyViewModel vm) => _vm = vm;
        public void Refresh() => MainThread.BeginInvokeOnMainThread(async () => await _vm.RefreshAsync());
    }

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            StopPolling();
            _lastKnownGood = null;
        }

        _disposed = true;
    }
}

/// <summary>
/// View states for UI binding.
/// </summary>
public enum CodeComfyViewState
{
    Loading,
    Empty,
    List,
    Fatal
}

/// <summary>
/// Static converters for CodeComfyViewState to bool visibility.
/// Used via x:Static in XAML.
/// </summary>
public static class CodeComfyViewStateConverters
{
    public static IValueConverter IsLoading { get; } = new CodeComfyViewStateConverter(CodeComfyViewState.Loading);
    public static IValueConverter IsEmpty { get; } = new CodeComfyViewStateConverter(CodeComfyViewState.Empty);
    public static IValueConverter IsList { get; } = new CodeComfyViewStateConverter(CodeComfyViewState.List);
    public static IValueConverter IsFatal { get; } = new CodeComfyViewStateConverter(CodeComfyViewState.Fatal);

    private sealed class CodeComfyViewStateConverter : IValueConverter
    {
        private readonly CodeComfyViewState _targetState;
        public CodeComfyViewStateConverter(CodeComfyViewState targetState) => _targetState = targetState;

        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value is CodeComfyViewState state && state == _targetState;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
