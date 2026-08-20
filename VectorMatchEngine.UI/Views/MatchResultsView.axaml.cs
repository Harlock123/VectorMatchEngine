using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using VectorMatchEngine.UI.ViewModels;

namespace VectorMatchEngine.UI.Views;

public partial class MatchResultsView : UserControl
{
    private MatchResultsViewModel? _subscribedViewModel;

    public MatchResultsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Detach from the previous ViewModel so navigating back and forth does not leak handlers.
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is MatchResultsViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // The rows may already be loaded if this view was re-created for a live ViewModel.
            BuildColumns(viewModel);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MatchResultsViewModel.GridRows) &&
            sender is MatchResultsViewModel viewModel)
        {
            BuildColumns(viewModel);
        }
    }

    /// <summary>
    /// Rebuilds the grid's columns from the ViewModel's column descriptors. Avalonia's DataGrid
    /// cannot declare these in XAML because the set is only known once the match job is loaded.
    /// </summary>
    private void BuildColumns(MatchResultsViewModel viewModel)
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid is null)
            return;

        grid.Columns.Clear();

        foreach (var column in viewModel.GridColumns)
        {
            if (column.IsSimilarity)
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding(column.Key) { StringFormat = "0.0000" },
                    Width = new DataGridLength(90)
                });
            }
            else
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding(column.Key),
                    Width = new DataGridLength(150)
                });
            }
        }
    }
}
