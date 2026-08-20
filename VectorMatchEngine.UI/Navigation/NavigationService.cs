using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace VectorMatchEngine.UI.Navigation;

public class NavigationService : INavigationService
{
    public event Action<object>? PageChanged;

    public void NavigateTo<TViewModel>() where TViewModel : class
        => NavigateTo<TViewModel>(_ => { });

    public void NavigateTo<TViewModel>(Action<TViewModel> configure) where TViewModel : class
    {
        var viewModel = App.Services.GetRequiredService<TViewModel>();

        // Configure before the page is shown so the view never renders a half-initialized state.
        configure(viewModel);

        PageChanged?.Invoke(viewModel);

        if (viewModel is IActivatable activatable)
            _ = ActivateAsync(activatable);
    }

    private static async Task ActivateAsync(IActivatable activatable)
    {
        try
        {
            await activatable.OnActivatedAsync();
        }
        catch (Exception ex)
        {
            // Activation runs detached from any command, so a throw here would otherwise be an
            // unobserved task exception that silently kills page loading.
            Dispatcher.UIThread.Post(() =>
                System.Diagnostics.Debug.WriteLine($"Activation failed: {ex}"));
        }
    }
}
