using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dirvexa.App.Infrastructure;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public enum NavigationSection
{
  Scan,
  History,
  Settings
}

public enum DialogChoice
{
  Primary,
  Secondary,
  Cancel
}

public enum CancelDisposition
{
  None,
  Keep,
  Discard
}

public sealed partial class MainWindowViewModel : ObservableObject
{
  private readonly IScanService _scanService;
  private readonly IScanRepository _repository;
  private readonly AppLog _log;
  private CancellationTokenSource? _scanCancellation;
  private Task<ScanOutcome>? _activeScanTask;
  private CancelDisposition _cancelDisposition;
  private object _scanWorkspace;
  private TaskCompletionSource<DialogChoice>? _dialogCompletion;

  public MainWindowViewModel(
    IScanService scanService,
    IScanRepository repository,
    AppSettingsService settingsService,
    AppPaths paths,
    IPlatformFileActions fileActions,
    IUpdateService updateService,
    SemanticVersion currentVersion,
    AppDistribution distribution,
    Func<Task<string?>> pickFolder,
    Action<ThemePreference> applyTheme,
    AppLog log)
  {
    _scanService = scanService;
    _repository = repository;
    _log = log;
    ScanLanding = new ScanLandingViewModel(repository, pickFolder, StartScanAsync, OpenScanAsync, NavigateHistoryAsync);
    Results = new ResultsViewModel(repository, fileActions, NewScanAsync);
    History = new HistoryViewModel(repository, OpenScanAsync, ConfirmAsync);
    Settings = new SettingsViewModel(
      settingsService,
      paths,
      repository,
      fileActions,
      updateService,
      currentVersion,
      distribution,
      applyTheme,
      ConfirmAsync,
      log);
    _scanWorkspace = ScanLanding;
    _currentPage = ScanLanding;
  }

  public ScanLandingViewModel ScanLanding { get; }
  public ResultsViewModel Results { get; }
  public HistoryViewModel History { get; }
  public SettingsViewModel Settings { get; }

  [ObservableProperty] private object _currentPage;
  [ObservableProperty] private NavigationSection _currentSection = NavigationSection.Scan;
  [ObservableProperty] private bool _isScanRunning;
  [ObservableProperty] private bool _isDialogOpen;
  [ObservableProperty] private string _dialogTitle = string.Empty;
  [ObservableProperty] private string _dialogMessage = string.Empty;
  [ObservableProperty] private string _dialogPrimaryText = "Continue";
  [ObservableProperty] private string _dialogSecondaryText = string.Empty;
  [ObservableProperty] private string _dialogCancelText = "Cancel";
  [ObservableProperty] private bool _hasDialogSecondary;
  [ObservableProperty] private bool _isDialogPrimaryDanger;

  public bool IsScanSelected => CurrentSection == NavigationSection.Scan;
  public bool IsHistorySelected => CurrentSection == NavigationSection.History;
  public bool IsSettingsSelected => CurrentSection == NavigationSection.Settings;
  public bool HasActiveScan => IsScanRunning;
  public bool IsDialogPrimarySafe => !IsDialogPrimaryDanger;

  partial void OnCurrentSectionChanged(NavigationSection value)
  {
    OnPropertyChanged(nameof(IsScanSelected));
    OnPropertyChanged(nameof(IsHistorySelected));
    OnPropertyChanged(nameof(IsSettingsSelected));
  }

  partial void OnIsScanRunningChanged(bool value)
  {
    OnPropertyChanged(nameof(HasActiveScan));
  }

  partial void OnIsDialogPrimaryDangerChanged(bool value) => OnPropertyChanged(nameof(IsDialogPrimarySafe));

  public async Task InitializeAsync()
  {
    try
    {
      await _repository.EnforceRetentionAsync(20);
      await Task.WhenAll(ScanLanding.ReloadAsync(), Settings.ReloadAsync());
    }
    catch (Exception exception)
    {
      await _log.WriteAsync("Application initialization failed.", exception);
      ScanLanding.ErrorMessage = "Dirvexa could not load its saved scan history.";
    }
  }

  [RelayCommand]
  private void NavigateScan()
  {
    CurrentSection = NavigationSection.Scan;
    CurrentPage = _scanWorkspace;
  }

  [RelayCommand]
  private async Task NavigateHistoryAsync()
  {
    await History.ReloadAsync();
    CurrentSection = NavigationSection.History;
    CurrentPage = History;
  }

  [RelayCommand]
  private async Task NavigateSettingsAsync()
  {
    await Settings.ReloadAsync();
    CurrentSection = NavigationSection.Settings;
    CurrentPage = Settings;
  }

  public async Task<bool> HandleCloseRequestAsync()
  {
    if (!IsScanRunning) return true;
    var choice = await ShowDialogAsync(
      "A scan is still running",
      "Choose whether to keep the partial results, discard them, or continue scanning.",
      "Keep partial",
      "Discard",
      "Continue scan",
      hasSecondary: true,
      primaryDanger: false);
    if (choice == DialogChoice.Cancel) return false;
    _cancelDisposition = choice == DialogChoice.Primary ? CancelDisposition.Keep : CancelDisposition.Discard;
    _scanCancellation?.Cancel();
    if (_activeScanTask is not null) await _activeScanTask;
    return true;
  }

  [RelayCommand]
  private void ChooseDialogPrimary() => CompleteDialog(DialogChoice.Primary);

  [RelayCommand]
  private void ChooseDialogSecondary() => CompleteDialog(DialogChoice.Secondary);

  [RelayCommand]
  private void ChooseDialogCancel() => CompleteDialog(DialogChoice.Cancel);

  private async Task StartScanAsync(string input)
  {
    if (IsScanRunning) return;
    var validation = PathValidator.ValidateLocalDirectory(input);
    if (!validation.IsValid || validation.NormalizedPath is null)
    {
      ScanLanding.ErrorMessage = validation.Error ?? "Choose a valid local folder.";
      return;
    }

    ScanLanding.PathInput = validation.NormalizedPath;
    ScanLanding.ErrorMessage = string.Empty;
    _scanCancellation = new CancellationTokenSource();
    _cancelDisposition = CancelDisposition.None;
    var progressPage = new ProgressViewModel(validation.NormalizedPath, RequestCancelAsync);
    _scanWorkspace = progressPage;
    CurrentSection = NavigationSection.Scan;
    CurrentPage = progressPage;
    IsScanRunning = true;

    try
    {
      var progress = new Progress<ScanProgress>(progressPage.Update);
      _activeScanTask = _scanService.StartAsync(new ScanRequest(validation.NormalizedPath), progress, _scanCancellation.Token);
      var outcome = await _activeScanTask;

      if (outcome.Status == ScanStatus.CanceledPartial && _cancelDisposition == CancelDisposition.Discard)
      {
        await _repository.DiscardScanAsync(outcome.ScanId);
        await ScanLanding.ReloadAsync();
        _scanWorkspace = ScanLanding;
        if (ReferenceEquals(CurrentPage, progressPage)) CurrentPage = ScanLanding;
      }
      else
      {
        await _repository.EnforceRetentionAsync(20);
        await Results.LoadAsync(outcome.ScanId);
        _scanWorkspace = Results;
        if (ReferenceEquals(CurrentPage, progressPage)) CurrentPage = Results;
        await ScanLanding.ReloadAsync();
      }
    }
    catch (Exception exception)
    {
      await _log.WriteAsync("The scan failed unexpectedly.", exception);
      ScanLanding.ErrorMessage = $"The scan failed: {exception.Message}";
      _scanWorkspace = ScanLanding;
      if (ReferenceEquals(CurrentPage, progressPage)) CurrentPage = ScanLanding;
    }
    finally
    {
      IsScanRunning = false;
      _activeScanTask = null;
      _scanCancellation.Dispose();
      _scanCancellation = null;
      _cancelDisposition = CancelDisposition.None;
      await Settings.ReloadAsync();
    }
  }

  private async Task RequestCancelAsync()
  {
    if (!IsScanRunning) return;
    var choice = await ShowDialogAsync(
      "Cancel this scan?",
      "Choose whether to keep the partial results, discard them, or keep scanning.",
      "Keep partial",
      "Discard",
      "Continue scan",
      hasSecondary: true,
      primaryDanger: false);
    if (choice == DialogChoice.Cancel) return;
    _cancelDisposition = choice == DialogChoice.Primary ? CancelDisposition.Keep : CancelDisposition.Discard;
    _scanCancellation?.Cancel();
  }

  private async Task OpenScanAsync(Guid scanId)
  {
    await Results.LoadAsync(scanId);
    _scanWorkspace = Results;
    CurrentSection = NavigationSection.Scan;
    CurrentPage = Results;
  }

  private async Task NewScanAsync()
  {
    await ScanLanding.ReloadAsync();
    _scanWorkspace = ScanLanding;
    CurrentSection = NavigationSection.Scan;
    CurrentPage = ScanLanding;
  }

  private async Task<bool> ConfirmAsync(string title, string message)
  {
    var choice = await ShowDialogAsync(title, message, "Delete", string.Empty, "Cancel", hasSecondary: false, primaryDanger: true);
    return choice == DialogChoice.Primary;
  }

  private Task<DialogChoice> ShowDialogAsync(
    string title,
    string message,
    string primary,
    string secondary,
    string cancel,
    bool hasSecondary,
    bool primaryDanger)
  {
    if (_dialogCompletion is not null) return Task.FromResult(DialogChoice.Cancel);
    DialogTitle = title;
    DialogMessage = message;
    DialogPrimaryText = primary;
    DialogSecondaryText = secondary;
    DialogCancelText = cancel;
    HasDialogSecondary = hasSecondary;
    IsDialogPrimaryDanger = primaryDanger;
    IsDialogOpen = true;
    _dialogCompletion = new TaskCompletionSource<DialogChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
    return _dialogCompletion.Task;
  }

  private void CompleteDialog(DialogChoice choice)
  {
    var completion = _dialogCompletion;
    _dialogCompletion = null;
    IsDialogOpen = false;
    completion?.TrySetResult(choice);
  }
}
