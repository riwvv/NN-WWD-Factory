using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Windows;
using System.IO;
using Serilog;
using NN_WWD_Factory.Extensions;
using NN_WWD_Factory.ViewModels;
using NN_WWD_Factory.Views.Windows;

namespace NN_WWD_Factory;

public partial class App : Application {
    private static readonly Mutex _mutex = new(true, "NN_WWD_Factory_App_Mutex");
    private readonly IHost _host;
    private Process? _serverProcess;

    public App() => _host = CreateHostBuilder().Build();

    private IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .UseSerilog((context, services, config) => config.ReadFrom.Configuration(context.Configuration)
                .WriteTo.Debug())
        .ConfigureServices((context, services) => services
            .AddServices()
            .AddHttpClients()
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

            StartPythonServer();

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

        StopPythonServer();

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

    private void StartPythonServer() {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string pythonPath = Path.Combine(baseDir, "NN-WWD-Factory-Server", ".venv", "Scripts", "python.exe");
        string scriptPath = Path.Combine(baseDir, "NN-WWD-Factory-Server", "PythonServer", "main.py");

        if (!File.Exists(pythonPath)) {
            MessageBox.Show($"Python не найден по пути: {pythonPath}");
            return;
        }

        if (!File.Exists(scriptPath)) {
            MessageBox.Show($"Скрипт не найден: {scriptPath}");
            return;
        }

        _serverProcess = new Process();
        _serverProcess.StartInfo.FileName = pythonPath;
        _serverProcess.StartInfo.Arguments = scriptPath;
        _serverProcess.StartInfo.UseShellExecute = false;
        _serverProcess.StartInfo.CreateNoWindow = true;
        _serverProcess.StartInfo.RedirectStandardOutput = true;
        _serverProcess.StartInfo.RedirectStandardError = true;

        _serverProcess.OutputDataReceived += (s, e) => {
            if (!string.IsNullOrEmpty(e.Data)) Log.Information($"[Server] {e.Data}");
        };
        _serverProcess.ErrorDataReceived += (s, e) => {
            if (!string.IsNullOrEmpty(e.Data)) Log.Error($"[Server Error] {e.Data}");
        };

        _serverProcess.Start();

        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        Task.Delay(3000).Wait();
    }

    private void StopPythonServer() {
        try {
            if (_serverProcess != null && !_serverProcess.HasExited) {
                _serverProcess.Kill();
                _serverProcess.WaitForExit(3000);
                _serverProcess.Dispose();
                _serverProcess = null;
                Log.Information("Сервер остановлен");
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка остановки сервера");
        }
    }
}
