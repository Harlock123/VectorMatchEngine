using CommunityToolkit.Mvvm.ComponentModel;
using VectorMatchEngine.UI.Navigation;

namespace VectorMatchEngine.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _currentPage;

    public MainViewModel(INavigationService navigation)
    {
        navigation.PageChanged += viewModel => CurrentPage = viewModel;
        navigation.NavigateTo<HomeViewModel>();
    }
}
