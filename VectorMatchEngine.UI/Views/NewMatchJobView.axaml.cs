using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VectorMatchEngine.UI.Views;

public partial class NewMatchJobView : UserControl
{
    public NewMatchJobView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
