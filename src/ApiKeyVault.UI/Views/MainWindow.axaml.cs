using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ApiKeyVault.UI.ViewModels;

namespace ApiKeyVault.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        PeekButton.AddHandler(InputElement.PointerPressedEvent, OnPeekPointerPressed, RoutingStrategies.Tunnel);
        PeekButton.AddHandler(InputElement.PointerReleasedEvent, OnPeekPointerReleased, RoutingStrategies.Tunnel);
        PeekButton.AddHandler(InputElement.PointerCaptureLostEvent, OnPeekPointerCaptureLost, RoutingStrategies.Tunnel);
        PeekButton.AddHandler(InputElement.KeyDownEvent, OnPeekKeyDown, RoutingStrategies.Tunnel);
        PeekButton.AddHandler(InputElement.KeyUpEvent, OnPeekKeyUp, RoutingStrategies.Tunnel);
        PeekButton.LostFocus += OnPeekLostFocus;
        Deactivated += (_, _) => EndPeek();
    }

    private void OnPeekPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(PeekButton).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.BeginPeekSecretCommand.Execute(null);
        }
    }

    private void OnPeekPointerReleased(object? sender, PointerReleasedEventArgs e) => EndPeek();

    private void OnPeekPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndPeek();

    private void OnPeekKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter && DataContext is MainWindowViewModel vm)
        {
            vm.BeginPeekSecretCommand.Execute(null);
        }
    }

    private void OnPeekKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            EndPeek();
        }
    }

    private void OnPeekLostFocus(object? sender, RoutedEventArgs e) => EndPeek();

    private void EndPeek()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.EndPeekSecretCommand.Execute(null);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && DataContext is MainWindowViewModel { IsUnlocked: true, IsAddKeyDialogOpen: false, IsRecoveryDialogOpen: false })
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
        ListPane.Classes.Set("wide", e.NewSize.Width >= 660);
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
