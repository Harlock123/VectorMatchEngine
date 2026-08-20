namespace VectorMatchEngine.UI.Navigation;

public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : class;
    void NavigateTo<TViewModel>(Action<TViewModel> configure) where TViewModel : class;
    event Action<object>? PageChanged;
}
