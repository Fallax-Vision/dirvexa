using Avalonia.Controls;
using Avalonia.Interactivity;
using Dirvexa.App.ViewModels;

namespace Dirvexa.App.Views;

public partial class MainWindow : Window
{
  private bool _allowClose;
  private bool _wasNarrow;

  public MainWindow() => InitializeComponent();

  private async void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
  {
    if (_allowClose || DataContext is not MainWindowViewModel viewModel || !viewModel.IsScanRunning) return;
    eventArgs.Cancel = true;
    if (!await viewModel.HandleCloseRequestAsync()) return;
    _allowClose = true;
    Close();
  }

  private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
  {
    if (DataContext is not MainWindowViewModel viewModel) return;
    var isNarrow = eventArgs.NewSize.Width < 1050;
    if (isNarrow && !_wasNarrow) viewModel.Results.IsTreeVisible = false;
    if (!isNarrow && _wasNarrow) viewModel.Results.IsTreeVisible = true;
    _wasNarrow = isNarrow;
  }
}
