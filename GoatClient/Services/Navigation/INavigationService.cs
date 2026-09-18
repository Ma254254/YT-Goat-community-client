using System.ComponentModel;
using GoatClient.Models;
using GoatClient.ViewModels;

namespace GoatClient.Services.Navigation;

/// <summary>Single-window navigation: swaps the page view model shown in the main window.</summary>
public interface INavigationService : INotifyPropertyChanged
{
    AppPage? CurrentPage { get; }

    ViewModelBase? CurrentViewModel { get; }

    void Register(AppPage page, Func<ViewModelBase> factory);

    void Navigate(AppPage page);
}
