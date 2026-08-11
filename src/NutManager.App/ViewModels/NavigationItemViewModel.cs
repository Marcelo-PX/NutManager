using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NutManager.App.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
    public NavigationItemViewModel(AppPage page, string title, string symbol, ICommand navigateCommand)
    {
        Page = page;
        _title = title;
        Symbol = symbol;
        NavigateCommand = navigateCommand;
    }

    public AppPage Page { get; }

    [ObservableProperty]
    private string _title;

    public string Symbol { get; }

    public ICommand NavigateCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}
