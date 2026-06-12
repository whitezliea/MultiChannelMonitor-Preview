using System.Collections.ObjectModel;
using Application.DTOs.MeasurementMap;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Presentation.Wpf.Models;
using Presentation.Wpf.Renderers;

namespace Presentation.Wpf.ViewModels;

public sealed partial class MeasurementMapViewModel : PageViewModelBase
{
    private readonly HeatmapRenderer _heatmapRenderer;
    private bool _synchronizingSelection;

    public MeasurementMapViewModel()
        : this(new HeatmapRenderer())
    {
    }

    internal MeasurementMapViewModel(HeatmapRenderer heatmapRenderer)
        : base("Measurement Map")
    {
        _heatmapRenderer = heatmapRenderer;
        AvailableScaleModes = Enum.GetValues<MatrixScaleMode>();
        AvailablePalettes = Enum.GetValues<MatrixPalette>();
    }

    public ObservableCollection<HeatmapCellModel> Cells { get; } = [];
    public ObservableCollection<AbnormalMatrixPointModel> AbnormalPoints { get; } = [];
    public ObservableCollection<string> ColumnHeaders { get; } = [];
    public ObservableCollection<string> RowHeaders { get; } = [];
    public IReadOnlyList<MatrixScaleMode> AvailableScaleModes { get; }
    public IReadOnlyList<MatrixPalette> AvailablePalettes { get; }

    public MatrixDisplayOptionsDto DisplayOptions => new(
        SelectedScaleMode,
        FixedScaleMin,
        FixedScaleMax,
        SelectedPalette,
        MatrixType,
        Unit);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayOptions))]
    private MatrixScaleMode selectedScaleMode = MatrixScaleMode.AutoCurrentFrame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayOptions))]
    private MatrixPalette selectedPalette = MatrixPalette.IndustrialHeat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayOptions))]
    private double fixedScaleMin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayOptions))]
    private double fixedScaleMax = 2000d;

    [ObservableProperty]
    private bool showValues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FreezeStatusText))]
    private bool isFrozen;

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private int rows;

    [ObservableProperty]
    private int columns = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayOptions))]
    private string matrixType = "Light Intensity";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayOptions))]
    private string unit = "lux";

    [ObservableProperty]
    private string timestampText = "--";

    [ObservableProperty]
    private string matrixSizeText = "--";

    [ObservableProperty]
    private string qualityText = "Waiting";

    [ObservableProperty]
    private string dataStatusText = "Stopped";

    [ObservableProperty]
    private string minimumText = "--";

    [ObservableProperty]
    private string maximumText = "--";

    [ObservableProperty]
    private string averageText = "--";

    [ObservableProperty]
    private string stdDevText = "--";

    [ObservableProperty]
    private string uniformityText = "--";

    [ObservableProperty]
    private string validCountText = "--";

    [ObservableProperty]
    private string invalidCountText = "--";

    [ObservableProperty]
    private string abnormalCountText = "--";

    [ObservableProperty]
    private string legendMinimumText = "--";

    [ObservableProperty]
    private string legendMaximumText = "--";

    [ObservableProperty]
    private HeatmapCellModel? selectedPoint;

    [ObservableProperty]
    private AbnormalMatrixPointModel? selectedAbnormalPoint;

    public string FreezeStatusText => IsFrozen ? "Frozen" : "Live";

    public void Refresh(MeasurementMapSnapshotDto? snapshot, string dataStatus = "Live")
    {
        if (IsFrozen)
        {
            return;
        }

        if (snapshot is null)
        {
            ClearState();
            return;
        }

        DataStatusText = dataStatus;

        var selectedCoordinate = SelectedPoint is null
            ? ((int Row, int Column)?)null
            : (SelectedPoint.Row, SelectedPoint.Column);

        ApplySummary(snapshot);
        BuildHeaders(snapshot.Frame.Rows, snapshot.Frame.Columns);
        Replace(
            Cells,
            snapshot.Cells.Select(cell => _heatmapRenderer.CreateCellModel(cell, snapshot.Unit, ShowValues)));
        Replace(
            AbnormalPoints,
            snapshot.AbnormalPoints.Select(point => new AbnormalMatrixPointModel
            {
                Row = point.Row,
                Column = point.Column,
                Value = point.Value,
                ValueText = double.IsFinite(point.Value) ? $"{point.Value:0.###} {snapshot.Unit}" : "NA",
                Type = point.Type,
                Severity = point.Severity,
                Message = point.Message
            }));

        var selectedCell = selectedCoordinate.HasValue
            ? Cells.FirstOrDefault(cell =>
                cell.Row == selectedCoordinate.Value.Row &&
                cell.Column == selectedCoordinate.Value.Column)
            : null;
        SelectCell(selectedCell, synchronizeAbnormalSelection: true);
    }

    [RelayCommand]
    private void SelectPoint(HeatmapCellModel? cell) =>
        SelectCell(cell, synchronizeAbnormalSelection: true);

    partial void OnShowValuesChanged(bool value)
    {
        foreach (var cell in Cells)
        {
            cell.ShowValue = value;
        }
    }

    partial void OnSelectedAbnormalPointChanged(AbnormalMatrixPointModel? value)
    {
        if (_synchronizingSelection || value is null)
        {
            return;
        }

        var cell = Cells.FirstOrDefault(item => item.Row == value.Row && item.Column == value.Column);
        SelectCell(cell, synchronizeAbnormalSelection: false);
    }

    private void ApplySummary(MeasurementMapSnapshotDto snapshot)
    {
        HasData = true;
        Rows = snapshot.Frame.Rows;
        Columns = Math.Max(1, snapshot.Frame.Columns);
        MatrixType = snapshot.MatrixType;
        Unit = snapshot.Unit;
        TimestampText = snapshot.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        MatrixSizeText = $"{snapshot.Frame.Rows} x {snapshot.Frame.Columns}";
        QualityText = snapshot.QualityState.ToString();
        MinimumText = FormatValue(snapshot.Statistics.MinValue, snapshot.Unit);
        MaximumText = FormatValue(snapshot.Statistics.MaxValue, snapshot.Unit);
        AverageText = FormatValue(snapshot.Statistics.AverageValue, snapshot.Unit);
        StdDevText = FormatValue(snapshot.Statistics.StdDev, snapshot.Unit);
        UniformityText = double.IsFinite(snapshot.Statistics.UniformityMinMax)
            ? snapshot.Statistics.UniformityMinMax.ToString("P1")
            : "--";
        ValidCountText = snapshot.Statistics.ValidCount.ToString();
        InvalidCountText = snapshot.Statistics.InvalidCount.ToString();
        AbnormalCountText = snapshot.AbnormalPoints.Count.ToString();
        LegendMinimumText = FormatValue(snapshot.ScaleRange.MinValue, snapshot.Unit);
        LegendMaximumText = FormatValue(snapshot.ScaleRange.MaxValue, snapshot.Unit);
    }

    private void BuildHeaders(int rowCount, int columnCount)
    {
        Replace(ColumnHeaders, Enumerable.Range(0, columnCount).Select(column => $"C{column:00}"));
        Replace(RowHeaders, Enumerable.Range(0, rowCount).Select(row => $"R{row:00}"));
    }

    private void SelectCell(HeatmapCellModel? cell, bool synchronizeAbnormalSelection)
    {
        if (!ReferenceEquals(SelectedPoint, cell))
        {
            if (SelectedPoint is not null)
            {
                SelectedPoint.IsSelected = false;
            }

            SelectedPoint = cell;
            if (SelectedPoint is not null)
            {
                SelectedPoint.IsSelected = true;
            }
        }

        if (!synchronizeAbnormalSelection)
        {
            return;
        }

        _synchronizingSelection = true;
        try
        {
            SelectedAbnormalPoint = cell is null
                ? null
                : AbnormalPoints.FirstOrDefault(point => point.Row == cell.Row && point.Column == cell.Column);
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void ClearState()
    {
        HasData = false;
        Rows = 0;
        Columns = 1;
        TimestampText = "--";
        MatrixSizeText = "--";
        QualityText = "Waiting";
        DataStatusText = "Stopped";
        MinimumText = "--";
        MaximumText = "--";
        AverageText = "--";
        StdDevText = "--";
        UniformityText = "--";
        ValidCountText = "--";
        InvalidCountText = "--";
        AbnormalCountText = "--";
        LegendMinimumText = "--";
        LegendMaximumText = "--";
        Cells.Clear();
        AbnormalPoints.Clear();
        ColumnHeaders.Clear();
        RowHeaders.Clear();
        SelectCell(null, synchronizeAbnormalSelection: true);
    }

    private static string FormatValue(double value, string valueUnit) =>
        double.IsFinite(value) ? $"{value:0.0} {valueUnit}" : "--";

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
