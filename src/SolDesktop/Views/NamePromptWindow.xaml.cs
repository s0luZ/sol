using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SolDesktop.Interop;

namespace SolDesktop.Views;

public partial class NamePromptWindow : Window
{
    public NamePromptWindow(string title, string initial, string confirmLabel = "Create")
    {
        InitializeComponent();
        Title = title;
        PromptTitle.Text = title;
        NameBox.Text = initial;
        NameBox.SelectAll();
        ConfirmButton.Content = confirmLabel;
        Loaded += (_, _) => NameBox.Focus();
    }

    public string Result => NameBox.Text.Trim();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.UseRoundedCorners(new WindowInteropHelper(this).Handle);
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirm(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnCancel(sender, e);
            e.Handled = true;
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (Result.Length == 0) return;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
