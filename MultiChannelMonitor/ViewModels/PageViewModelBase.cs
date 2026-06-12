using CommunityToolkit.Mvvm.ComponentModel;

namespace Presentation.Wpf.ViewModels;

public abstract partial class PageViewModelBase : ObservableObject
{
    protected PageViewModelBase(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public virtual void Refresh()
    {
    }
}
