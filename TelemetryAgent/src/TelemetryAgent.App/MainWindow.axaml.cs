using Avalonia.Controls;
using Avalonia.Interactivity;
using System.ComponentModel;
using TelemetryAgent.App.ViewModels;

namespace TelemetryAgent.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;

        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isExplicitExit && _vm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void ForceExit()
    {
        _isExplicitExit = true;
        Close();
    }

    public void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void OnRefreshSetupsClicked(object? sender, RoutedEventArgs e)
    {
        await _vm.RefreshSetupsAsync();
    }

    private void OnMinimizeToTrayClicked(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        _vm.ToggleSettings();
    }

    private void OnCloseSettingsClicked(object? sender, RoutedEventArgs e)
    {
        _vm.ToggleSettings();
    }

    private void OnSaveSettingsClicked(object? sender, RoutedEventArgs e)
    {
        _vm.SaveSettings();
    }

    private void OnSwitchToLiveClicked(object? sender, RoutedEventArgs e)
    {
        _vm.SwitchToLive();
    }

    private void OnSwitchToMockClicked(object? sender, RoutedEventArgs e)
    {
        _vm.SwitchToMock();
    }

    private void OnInjectLockupClicked(object? sender, RoutedEventArgs e)
    {
        _vm.InjectLockup();
    }

    private void OnInjectOversteerClicked(object? sender, RoutedEventArgs e)
    {
        _vm.InjectOversteer();
    }

    private void OnInjectFuelClicked(object? sender, RoutedEventArgs e)
    {
        _vm.InjectFuelAlert();
    }

    private async void OnGenerateTestSetupClicked(object? sender, RoutedEventArgs e)
    {
        await _vm.GenerateTestSetupAsync();
    }

    private async void OnToggleRecordClicked(object? sender, RoutedEventArgs e)
    {
        await _vm.ToggleRecordingAsync();
    }
}
