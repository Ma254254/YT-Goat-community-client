using GoatClient.Core;

namespace GoatClient.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public virtual void OnNavigatedTo()
    {
    }

    public virtual void OnNavigatedFrom()
    {
    }
}
