using DeepSeek.Desktop.ViewModels;
using DeepSeekBrowser.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DeepSeek.Desktop.Pages;

public sealed partial class AgentPage : Page
{
    private readonly AgentViewModel _vm = new();

    public AgentPage()
    {
        InitializeComponent();
        MessageList.ItemsSource = _vm.Messages;
        _vm.StateChanged += () =>
        {
            SendButton.IsEnabled = !_vm.IsRunning;
            StopButton.IsEnabled = _vm.IsRunning;
        };
        DeepThinkToggle.IsOn = _vm.DeepThinking;
        WebSearchToggle.IsOn = _vm.WebSearch;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.MainWindow is MainWindow mw)
            await mw.EnsureHostInitializedAsync();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.InputText = InputBox.Text;
        _vm.DeepThinking = DeepThinkToggle.IsOn;
        _vm.WebSearch = WebSearchToggle.IsOn;
        _vm.Strategy = StrategyBox.SelectedIndex == 1 ? AgentStrategies.Blueprint : AgentStrategies.Execute;
        await _vm.SendAsync();
        InputBox.Text = "";
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => _vm.Stop();
}
