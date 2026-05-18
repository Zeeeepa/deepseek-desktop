using System.Windows;

namespace DeepSeekBrowser.Views;

public partial class DsMessageDialog : Window
{
    private DsMessageDialog()
    {
        InitializeComponent();
    }

    public static bool Confirm(
        Window? owner,
        string message,
        string title = "确认",
        string confirmText = "确定",
        string cancelText = "取消")
    {
        var dlg = Create(owner, title, message, confirmText, cancelText, showCancel: true);
        return dlg.ShowDialog() == true;
    }

    public static void Info(Window? owner, string message, string title = "提示") =>
        Alert(owner, message, title, "确定", showCancel: false);

    public static void Warning(Window? owner, string message, string title = "提示") =>
        Alert(owner, message, title, "确定", showCancel: false);

    private static void Alert(Window? owner, string message, string title, string okText, bool showCancel)
    {
        var dlg = Create(owner, title, message, okText, "取消", showCancel);
        dlg.ShowDialog();
    }

    private static DsMessageDialog Create(
        Window? owner,
        string title,
        string message,
        string okText,
        string cancelText,
        bool showCancel)
    {
        var dlg = new DsMessageDialog
        {
            Owner = owner,
            Title = string.IsNullOrWhiteSpace(title) ? "DeepSeek" : title
        };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkButton.Content = okText;
        dlg.CancelButton.Content = cancelText;
        dlg.CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        if (!showCancel)
            dlg.OkButton.Margin = new Thickness(0);
        return dlg;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
