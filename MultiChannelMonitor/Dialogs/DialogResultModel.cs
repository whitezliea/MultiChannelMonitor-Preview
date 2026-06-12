namespace Presentation.Wpf.Dialogs;

public sealed record DialogResultModel(bool IsConfirmed, string? Message = null);
