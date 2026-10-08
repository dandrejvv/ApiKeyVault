using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ApiKeyVault.UI.ViewModels;

namespace ApiKeyVault.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && DataContext is MainWindowViewModel { IsUnlocked: true, IsAddKeyDialogOpen: false })
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnListPaneSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ListPane.Classes.Set("compact", e.NewSize.Width < 540);
        ListPane.Classes.Set("narrow", e.NewSize.Width < 420);
    }

    private void OnEntryListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (e.Source is Visual visual)
            {
                var listBoxItem = visual.FindAncestorOfType<ListBoxItem>();
                if (listBoxItem?.DataContext is EntryItemViewModel item)
                {
                    vm.SelectedEntry = item;
                    vm.IsInspectorVisible = true;
                    return;
                }
            }

            if (vm.SelectedEntry != null)
            {
                vm.IsInspectorVisible = true;
            }
        }
    }
}
