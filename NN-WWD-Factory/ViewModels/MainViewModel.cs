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
    private string _wakeWordName = "джарвис"; // значение по умолчанию

    [ObservableProperty]
    private int _positive = 500;

    [ObservableProperty]
    private int _negative = 1000;

    [ObservableProperty]
    private int _epochsCount = 20;

    [ObservableProperty]
    private string _message = "Готов к работе";

    [ObservableProperty]
    private int _progress = 0;

    [ObservableProperty]
    private bool _isBusy = false;

    private CancellationTokenSource? _cts;

    [RelayCommand]
    private async Task StartCreating() {
        // Проверка, что введено слово
        if (string.IsNullOrWhiteSpace(WakeWordName)) {
            Message = "⚠️ Введите ключевое слово";
            return;
        }

        _logger.LogInformation($"Начало создания для слова: {WakeWordName}");

        var request = new RequestBody {
            WakeWord = WakeWordName.Trim(),
            SampleRate = 24000,
            CountPerText = Positive,
            NegativeCount = Negative,
            Epochs = EpochsCount
        };

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        Message = $"🚀 Запуск обучения для '{WakeWordName}'...";

        try {
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
                // Модель уже сохранена через SaveFileDialog в сервисе
                Message = message ?? "✅ Модель успешно сохранена";
                Progress = 100;
            }
            else {
                Message = $"❌ {message ?? "Ошибка обучения"}";
            }
        }
        catch (OperationCanceledException) {
            Message = "⏹️ Операция отменена";
            _logger.LogInformation("Операция отменена пользователем");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка обучения");
            Message = $"❌ Ошибка: {ex.Message}";
        }
        finally {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PauseCreating() {
        Message = "⏸️ Пауза (не реализовано)";
    }

    [RelayCommand]
    private void CancelCreating() {
        _logger.LogInformation("Отмена операции...");
        _cts?.Cancel();
        Message = "⏹️ Отмена запрошена...";
    }
}