using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RemapUSB.Ui;

/// <summary>
/// Custom dialog result enum
/// </summary>
public enum CustomDialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Yes = 6,
    No = 7
}

/// <summary>
/// Dialog icon types
/// </summary>
public enum DialogIcon
{
    None,
    Information,
    Warning,
    Error,
    Question
}

/// <summary>
/// Custom styled dialog window matching the app's Fluent theme
/// </summary>
public partial class CustomDialog : Window
{
    private CustomDialogResult _result = CustomDialogResult.None;
    private CustomDialogResult _defaultResult = CustomDialogResult.OK;

    public CustomDialogResult Result => _result;

    public CustomDialog(string title, string message, DialogIcon icon, string okText, string? cancelText = null, string? yesText = null, string? noText = null)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;

        // Set icon
        var iconChar = icon switch
        {
            DialogIcon.Information => "\uE946",
            DialogIcon.Warning => "\uE7BA",
            DialogIcon.Error => "\uE783",
            DialogIcon.Question => "\uE8A1",
            _ => ""
        };
        IconText.Text = iconChar;
        IconText.Visibility = string.IsNullOrEmpty(iconChar) ? Visibility.Collapsed : Visibility.Visible;

        // Setup buttons based on what's provided
        var hasYesNo = !string.IsNullOrEmpty(yesText) || !string.IsNullOrEmpty(noText);
        var hasCancel = !string.IsNullOrEmpty(cancelText);

        if (hasYesNo)
        {
            YesButton.Content = yesText ?? "Sim";
            NoButton.Content = noText ?? "Não";
            YesButton.Visibility = Visibility.Visible;
            NoButton.Visibility = Visibility.Visible;
            OkButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            _defaultResult = CustomDialogResult.Yes;
        }
        else if (hasCancel)
        {
            OkButton.Content = okText ?? "OK";
            CancelButton.Content = cancelText ?? "Cancelar";
            OkButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Visible;
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;
            _defaultResult = CustomDialogResult.OK;
        }
        else
        {
            OkButton.Content = okText ?? "OK";
            OkButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;
            _defaultResult = CustomDialogResult.OK;
        }

        Loaded += (_, _) =>
        {
            if (hasYesNo) YesButton.Focus();
            else if (hasCancel) OkButton.Focus();
            else OkButton.Focus();
        };

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _result = hasCancel ? CustomDialogResult.Cancel : (hasYesNo ? CustomDialogResult.No : CustomDialogResult.Cancel);
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            _result = _defaultResult;
            Close();
        }
    }

    private bool hasYesNo => YesButton.Visibility == Visibility.Visible;
    private bool hasCancel => CancelButton.Visibility == Visibility.Visible;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _result = CustomDialogResult.OK;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _result = CustomDialogResult.Cancel;
        Close();
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        _result = CustomDialogResult.Yes;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        _result = CustomDialogResult.No;
        Close();
    }
}

/// <summary>
/// Helper for showing custom dialogs
/// </summary>
public static class DialogHelper
{
    public static CustomDialogResult Show(Window owner, string title, string message, DialogIcon icon = DialogIcon.Information, string okText = "OK")
    {
        var dialog = new CustomDialog(title, message, icon, okText);
        dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static CustomDialogResult ShowOkCancel(Window owner, string title, string message, DialogIcon icon = DialogIcon.Question, string okText = "OK", string cancelText = "Cancelar")
    {
        var dialog = new CustomDialog(title, message, icon, okText, cancelText);
        dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static CustomDialogResult ShowYesNo(Window owner, string title, string message, DialogIcon icon = DialogIcon.Question, string yesText = "Sim", string noText = "Não")
    {
        var dialog = new CustomDialog(title, message, icon, yesText, null, yesText, noText);
        dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static CustomDialogResult ShowYesNoCancel(Window owner, string title, string message, DialogIcon icon = DialogIcon.Question, string yesText = "Sim", string noText = "Não", string cancelText = "Cancelar")
    {
        var dialog = new CustomDialog(title, message, icon, yesText, cancelText, yesText, noText);
        dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }
}