using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Presentation.Wpf.Renderers;
using Presentation.Wpf.ViewModels;

namespace Presentation.Wpf.Views;

public partial class HistoryView : UserControl
{
    private readonly TrendChartRenderer _renderer = new();
    private HistoryViewModel? _viewModel;

    public HistoryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as HistoryViewModel);
        _renderer.Invalidate();
        RenderTrendPreview();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        AttachViewModel(null);

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        AttachViewModel(e.NewValue as HistoryViewModel);
        RenderTrendPreview();
    }

    private void OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _renderer.Invalidate();
            RenderTrendPreview();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.CurrentTrendSnapshot))
        {
            RenderTrendPreview();
        }
    }

    private void AttachViewModel(HistoryViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void RenderTrendPreview()
    {
        if (!IsLoaded || !IsVisible)
        {
            return;
        }

        if (_viewModel?.CurrentTrendSnapshot is { } snapshot)
        {
            _renderer.Render(HistoryTrendPlot, snapshot);
        }
        else
        {
            _renderer.Clear(HistoryTrendPlot);
        }
    }
}
