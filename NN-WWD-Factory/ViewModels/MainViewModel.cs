using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.IO;
using NN_WWD_Factory.Models;
using NN_WWD_Factory.Services;

namespace NN_WWD_Factory.ViewModels;

public partial class MainViewModel(ConnectionToFactoryServerService _service, ILogger<MainViewModel> _logger) : ObservableObject {
    [ObservableProperty]
    private string _wakeWordName = string.Empty;

    [ObservableProperty]
    private int _positive = 0;

    [ObservableProperty]
    private int _negative = 0;

    [ObservableProperty]
    private int _epochsCount = 0;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private int _progress = 0;

    private CancellationTokenSource? _cts;

    [RelayCommand]
    private async Task StartCreating() {
        _logger.LogInformation("Начало создания");
        _logger.LogInformation($"Указано ключевое слово: {WakeWordName}");

        var request = new RequestBody {
            WakeWord = WakeWordName,
            SampleRate = 24000,
            CountPerText = Positive,
            NegativeCount = Negative,
            Epochs = EpochsCount
        };

        _cts = new CancellationTokenSource();

        var progress = new Progress<(int Progress, string Message)>(p => {
            Progress = p.Progress;
            Message = p.Message;
        });

        var (success, modelData, message) = await _service.TrainAndDownloadAsync(
            request,
            progress,
            _cts.Token
        );

        _logger.LogInformation($"Статус: {success}");

        if (success && modelData != null) {
            var savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{WakeWordName}_model.pth");
            await File.WriteAllBytesAsync(savePath, modelData);
            Message = $"Модель сохранена: {savePath}";
        }
        else {
            if (message != null && !string.IsNullOrEmpty(message))
                Message = message;
        }
    }

    [RelayCommand]
    private void PauseCreating() {

    }

    [RelayCommand]
    private void CancelCreating() {
        _logger.LogInformation("Отмена операции...");

        _cts?.Cancel();

        try {
            using var httpClient = new HttpClient();
            var response = httpClient.PostAsync("http://localhost:8000/shutdown", null).Result;
            if (response.IsSuccessStatusCode) {
                _logger.LogInformation("Сервер остановлен");
            }
        }
        catch {
            _logger.LogWarning("Не удалось остановить сервер через API");
        }
    }
}
