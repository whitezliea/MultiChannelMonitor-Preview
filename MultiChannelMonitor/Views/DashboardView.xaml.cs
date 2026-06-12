using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Presentation.Wpf.Renderers;
using Presentation.Wpf.ViewModels;

namespace Presentation.Wpf.Views;

public partial class DashboardView : UserControl
{
    private readonly TrendChartRenderer _trendRenderer = new();
    private DashboardViewModel? _viewModel;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as DashboardViewModel);
        _trendRenderer.Invalidate();
        RenderRecentTrend();
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

        AttachViewModel(e.NewValue as DashboardViewModel);
        RenderRecentTrend();
    }

    private void OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _trendRenderer.Invalidate();
            RenderRecentTrend();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.CurrentTrendSeries))
        {
            RenderRecentTrend();
        }
    }

    private void AttachViewModel(DashboardViewModel? viewModel)
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

    private void RenderRecentTrend()
    {
        if (!IsLoaded || !IsVisible)
        {
            return;
        }

        if (_viewModel?.CurrentTrendSeries is { } series)
        {
            _trendRenderer.RenderPreview(RecentTrendPlot, series);
        }
        else
        {
            _trendRenderer.Clear(RecentTrendPlot);
        }
    }
}
