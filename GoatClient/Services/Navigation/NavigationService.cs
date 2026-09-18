using GoatClient.Core;
using GoatClient.Models;
using GoatClient.Services.Logging;
using GoatClient.ViewModels;

namespace GoatClient.Services.Navigation;

public sealed class NavigationService : ObservableObject, INavigationService
{
    private readonly Dictionary<AppPage, Func<ViewModelBase>> _factories = new();
    private readonly Dictionary<AppPage, ViewModelBase> _instances = new();
    private readonly ILogger _logger;

    private AppPage? _currentPage;
    private ViewModelBase? _currentViewModel;

    public NavigationService(ILogger logger)
    {
        _logger = logger;
    }

    public AppPage? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public void Register(AppPage page, Func<ViewModelBase> factory) => _factories[page] = factory;

    public void Navigate(AppPage page)
    {
        if (!_factories.TryGetValue(page, out var factory))
        {
            throw new InvalidOperationException($"No view model registered for page '{page}'.");
        }

        if (!_instances.TryGetValue(page, out var viewModel))
        {
            viewModel = factory();
            _instances[page] = viewModel;
        }

        if (ReferenceEquals(viewModel, CurrentViewModel))
        {
            return;
        }

        CurrentViewModel?.OnNavigatedFrom();
        viewModel.OnNavigatedTo();
        CurrentPage = page;
        CurrentViewModel = viewModel;
        _logger.Debug($"Navigated to {page}.");
    }
}
