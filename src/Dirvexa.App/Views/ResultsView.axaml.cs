using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dirvexa.App.ViewModels;
using Dirvexa.Core;

namespace Dirvexa.App.Views;

public partial class ResultsView : UserControl
{
  public ResultsView() => InitializeComponent();

  private async void OnFolderSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
  {
    if (DataContext is ResultsViewModel viewModel && sender is TreeView tree)
      await viewModel.SelectFolderAsync(tree.SelectedItem as FolderNodeViewModel);
  }

  private async void OnSorting(object? sender, DataGridColumnEventArgs eventArgs)
  {
    if (DataContext is not ResultsViewModel viewModel) return;
    eventArgs.Handled = true;
    var sort = eventArgs.Column.SortMemberPath switch
    {
      "Name" => EntrySort.Name,
      "Category" => EntrySort.Category,
      "Modified" => EntrySort.Modified,
      "Path" => EntrySort.Path,
      _ => EntrySort.Size
    };
    await viewModel.SortAsync(sort);
  }

  private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
  {
    if (eventArgs.Key == Key.Enter && DataContext is ResultsViewModel viewModel && viewModel.ApplyFiltersCommand.CanExecute(null))
      viewModel.ApplyFiltersCommand.Execute(null);
  }
}
