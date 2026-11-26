using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WiringSim.UI.Services;

namespace WiringSim.UI.Dialogs;

/// <summary>
/// Hộp thoại nhập liệu nhanh đồng bộ Theme Sáng/Tối và Đa ngôn ngữ (LocalizationManager).
/// </summary>
public sealed class InputPromptDialog : Window
{
    private readonly TextBox _inputBox;
    public string ResultText => _inputBox.Text.Trim();

    public InputPromptDialog(Window? owner, string title, string promptLabel, string initialValue)
    {
        Owner = owner;
        Title = title;
        Icon = ThemeManager.GetAppIcon();
        Width = 390;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SurfaceBgBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");

        SourceInitialized += (_, _) => ThemeManager.ApplyWindowDwmDarkMode(this, ThemeManager.IsEffectiveDark);

        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

        var lbl = new TextBlock
        {
            Text = promptLabel,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        root.Children.Add(lbl);

        _inputBox = new TextBox
        {
            Text = initialValue,
            FontSize = 14,
            Padding = new Thickness(9, 7, 9, 7),
            BorderThickness = new Thickness(1.4)
        };
        _inputBox.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        _inputBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _inputBox.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        _inputBox.SetResourceReference(TextBox.BorderBrushProperty, "AccentCyanBrush");
        root.Children.Add(_inputBox);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var btnCancel = new Button
        {
            Content = LocalizationManager.Get("Str.Dialog.Cancel"),
            MinWidth = 88,
            Margin = new Thickness(0, 0, 8, 0)
        };
        btnCancel.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };

        var btnOk = new Button
        {
            Content = LocalizationManager.Get("Str.Dialog.Apply"),
            IsDefault = true,
            MinWidth = 88
        };
        btnOk.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        btnOk.SetResourceReference(BorderBrushProperty, "AccentCyanBrush");
        btnOk.Click += (_, _) => { DialogResult = true; Close(); };

        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnOk);
        root.Children.Add(btnRow);

        Content = root;

        Loaded += (_, _) =>
        {
            _inputBox.Focus();
            _inputBox.SelectAll();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        };
    }

    public static string? Prompt(Window? owner, string title, string label, string defaultValue)
    {
        var dlg = new InputPromptDialog(owner, title, label, defaultValue);
        return dlg.ShowDialog() == true ? dlg.ResultText : null;
    }
}
