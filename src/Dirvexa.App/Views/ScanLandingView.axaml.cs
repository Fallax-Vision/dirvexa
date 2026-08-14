using Avalonia.Controls;
using Avalonia.Input;
using Dirvexa.App.ViewModels;

namespace Dirvexa.App.Views;

public partial class ScanLandingView : UserControl
{
  public ScanLandingView() => InitializeComponent();

  private void OnPathInputKeyDown(object? sender, KeyEventArgs eventArgs)
  {
    if (eventArgs.Key != Key.Enter) return;
    eventArgs.Handled = true;
    if (DataContext is ScanLandingViewModel viewModel && viewModel.StartCommand.CanExecute(null))
      viewModel.StartCommand.Execute(null);
  }
}
