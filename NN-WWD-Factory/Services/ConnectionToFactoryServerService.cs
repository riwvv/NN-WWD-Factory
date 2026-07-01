using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NN_WWD_Factory.Models;
using NN_WWD_Factory.Models.DTOs;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

namespace NN_WWD_Factory.Services;

public class ConnectionToFactoryServerService(IHttpClientFactory httpClientFactory, ILogger<ConnectionToFactoryServerService> _logger) {
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("NN-WWD-Server");

    public async Task<(bool Success, byte[]? ModelData, string? Message)> TrainAndDownloadAsync(
    RequestBody request,
    IProgress<(int Progress, string Message)> progress,
    CancellationToken cancellationToken = default) {

        try {
            progress.Report((0, "Запуск обучения..."));
            var taskId = await StartTrainingAsync(request);

            FallbackDTO status;
            do {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(2000, cancellationToken);

                status = await GetStatusAsync(taskId);
                progress.Report((status.Progress, status.Message));

                _logger.LogInformation($"Статус: {status.Status}, Прогресс: {status.Progress}%");
            } while (status.Status == "processing");

            if (status.Status == "failed") {
                return (false, null, status.Message);
            }

            progress.Report((90, "Скачивание пакета..."));
            var packageData = await DownloadPackageAsync(taskId);

            // Диалог сохранения
            var saveFileDialog = new SaveFileDialog {
                Filter = "ZIP архив|*.zip",
                Title = "Сохранить модель",
                FileName = $"{request.WakeWord}_model_package.zip"
            };

            if (saveFileDialog.ShowDialog() == true) {
                await File.WriteAllBytesAsync(saveFileDialog.FileName, packageData, cancellationToken);
                progress.Report((100, $"Модель сохранена: {saveFileDialog.FileName}"));
                return (true, packageData, $"Модель сохранена: {saveFileDialog.FileName}");
            }
            else {
                return (false, null, "Сохранение отменено пользователем");
            }
        }
        catch (OperationCanceledException) {
            return (false, null, "Операция отменена пользователем");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка в TrainAndDownloadAsync");
            return (false, null, $"Ошибка: {ex.Message}");
        }
    }

    private async Task<string> StartTrainingAsync(RequestBody request) {
        try {
            var response = await _httpClient.PostAsJsonAsync("/train-full-pipeline", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TaskDTO>();
            return result?.TaskId ?? throw new Exception("Не удалось получить TaskId");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка запуска обучения");
            throw;
        }
    }

    private async Task<FallbackDTO> GetStatusAsync(string taskId) {
        try {
            var response = await _httpClient.GetAsync($"/generate-status/{taskId}");
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<FallbackDTO>();
            return result ?? new FallbackDTO { Status = "error", Message = "Не удалось получить статус" };
        }
        catch (Exception ex) {
            _logger.LogError(ex, $"Ошибка получения статуса для {taskId}");
            return new FallbackDTO {
                Status = "error",
                Message = $"Ошибка: {ex.Message}",
                Progress = 0
            };
        }
    }

    private async Task<byte[]> DownloadPackageAsync(string taskId) {
        try {
            var response = await _httpClient.GetAsync($"/download-package/{taskId}");
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex) {
            _logger.LogError(ex, $"Ошибка скачивания пакета для {taskId}");
            throw;
        }
    }

    // Старый метод DownloadModelAsync можно удалить или оставить для обратной совместимости
    private async Task<byte[]> DownloadModelAsync(string taskId) {
        try {
            var response = await _httpClient.GetAsync($"/download/{taskId}");
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex) {
            _logger.LogError(ex, $"Ошибка скачивания модели для {taskId}");
            throw;
        }
    }
}