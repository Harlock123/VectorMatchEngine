namespace VectorMatchEngine.UI.Navigation;

/// <summary>
/// Implemented by ViewModels that load data when they become the active page.
///
/// The spec called for ReactiveUI's IActivatableViewModel/WhenActivated, but this app is built on
/// CommunityToolkit.Mvvm, which has no activation concept. This is the equivalent hook, invoked by
/// NavigationService immediately after a page is shown.
/// </summary>
public interface IActivatable
{
    Task OnActivatedAsync();
}
