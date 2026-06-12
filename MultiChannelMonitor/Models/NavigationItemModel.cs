using CommunityToolkit.Mvvm.ComponentModel;
using Presentation.Wpf.Navigation;

namespace Presentation.Wpf.Models;

public sealed partial class NavigationItemModel : ObservableObject
{
    public NavigationItemModel(NavigationPage page, string title, string glyph)
    {
        Page = page;
        Title = title;
        Glyph = glyph;
    }

    public NavigationPage Page { get; }
    public string Title { get; }
    public string Glyph { get; }
}
