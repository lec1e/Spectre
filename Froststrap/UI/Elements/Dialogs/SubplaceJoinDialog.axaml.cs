// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Input;
using Avalonia.Interactivity;
using Froststrap.UI.ViewModels.Dialogs;

namespace Froststrap.UI.Elements.Dialogs;

internal partial class SubplaceJoinDialog : Base.AvaloniaWindow
{
    public SubplaceJoinDialog()
    {
        InitializeComponent();
        Froststrap.UI.Utility.SpectreChrome.Apply(this);
    }

    public SubplaceJoinDialog(SubplaceJoinDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as IDisposable)?.Dispose();
    }
}