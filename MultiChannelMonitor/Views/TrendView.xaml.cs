using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Presentation.Wpf.Renderers;
using Presentation.Wpf.ViewModels;

namespace Presentation.Wpf.Views;

public partial class TrendView : UserControl
{
    private readonly TrendChartRenderer _renderer = new();
    private TrendViewModel? _viewModel;

    public TrendView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as TrendViewModel);
        _renderer.Invalidate();
        RenderCurrentSnapshot();
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

        AttachViewModel(e.NewValue as TrendViewModel);
        RenderCurrentSnapshot();
    }

    private void OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _renderer.Invalidate();
            RenderCurrentSnapshot();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrendViewModel.CurrentSnapshot))
        {
            RenderCurrentSnapshot();
        }
    }

    private void AttachViewModel(TrendViewModel? viewModel)
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

    private void RenderCurrentSnapshot()
    {
        if (!IsLoaded || !IsVisible)
        {
            return;
        }

        if (_viewModel?.CurrentSnapshot is { } snapshot)
        {
            _renderer.Render(TrendPlot, snapshot);
        }
        else
        {
            _renderer.Clear(TrendPlot);
        }
    }
}
