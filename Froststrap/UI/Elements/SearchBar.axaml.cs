using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Froststrap.UI.ViewModels;

namespace Froststrap.UI.Elements;

public partial class SearchBar : UserControl
{
    public SearchBar()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is SearchBarViewModel vm)
            {
                vm.PropertyChanged -= OnViewModelPropertyChanged;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchBarViewModel.IsSearchFlyoutOpen) &&
            sender is SearchBarViewModel vm)
        {
            if (vm.IsSearchFlyoutOpen)
                AlignResults();
        }
    }

    private void OnResultsOpened(object? sender, EventArgs e) => AlignResults();

    private void AlignResults()
    {
        var width = SearchHost.Bounds.Width;
        if (width > 0)
            ResultsPanel.Width = width;
    }

    public void FocusSearch()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            if (scrollViewer.Offset.Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 50)
            {
                if (DataContext is SearchBarViewModel vm && vm.CanLoadMore && !vm.IsGameSearchLoading)
                {
                    vm.LoadMoreGamesCommand.Execute(null);
                }
            }
        }
    }
}