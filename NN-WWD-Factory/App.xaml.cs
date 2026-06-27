using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NN_WWD_Factory.Extensions;
using NN_WWD_Factory.ViewModels;
using NN_WWD_Factory.Views.Windows;
using Serilog;
using System.Windows;

namespace NN_WWD_Factory;

public partial class App : Application {
    private static readonly Mutex _mutex = new(true, "NN_WWD_Factory_App_Mutex");
    private readonly IHost _host;

    public App() => _host = CreateHostBuilder().Build();

    private IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .UseSerilog((context, services, config) => config.ReadFrom.Configuration(context.Configuration)
                .WriteTo.Debug())
        .ConfigureServices((context, services) => services
            .AddServices()
            .AddViewModels()
            .AddViews());

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        if (!_mutex.WaitOne(TimeSpan.Zero, true)) {
            MessageBox.Show("Фабрика уже запущена!", "Предупреждение",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        if (_host == null) {
            Log.Error("При запуске хост равен нулю.");
            Shutdown();
            return;
        }

        try {
            await _host.StartAsync();
            Log.Information("Приложение запускается...");

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            mainWindow.Show();

            Log.Information("Приложение успешно запущено");
        }
        catch (Exception ex) {
            Log.Error(ex, "Критическая ошибка при запуске приложения");
            MessageBox.Show(
                $"Ошибка запуска: {ex.Message}\n\nПодробности в логах",
                "Критическая ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        Log.Information("Приложение завершает работу...");
        try {
            if (_host != null) {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка при остановке хоста");
        }

        _mutex.ReleaseMutex();
        _mutex.Dispose();

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
