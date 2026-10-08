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
