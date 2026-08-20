using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VectorMatchEngine.UI.Views;

public partial class MatchJobsView : UserControl
{
    public MatchJobsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
