using Jarvis.WakeWord;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NN_WWD_Factory.Models;
using NN_WWD_Factory.Models.DTOs;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NN_WWD_Factory.Services;

public class ConnectionToFactoryServerService(IHttpClientFactory httpClientFactory, ILogger<ConnectionToFactoryServerService> logger, ModelPackageBuilder packageBuilder) : IDisposable {
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("NN-WWD-Server");
    private readonly ILogger<ConnectionToFactoryServerService> _logger = logger;
    private readonly ModelPackageBuilder _packageBuilder = packageBuilder;

    public async Task<(bool Success, byte[]? PackageData, string? Message)> TrainAndDownloadAsync(
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

            progress.Report((85, "Скачивание ZIP-пакета..."));

            // Скачиваем ZIP-архив с сервера
            var zipData = await DownloadPackageAsync(taskId);

            // Распаковываем ZIP в памяти
            using var zipStream = new MemoryStream(zipData);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Извлекаем model.pth
            var modelEntry = archive.Entries.FirstOrDefault(e =>
                !string.IsNullOrEmpty(e.Name) &&
                e.Name.EndsWith(".pth", StringComparison.OrdinalIgnoreCase)
            );

            if (modelEntry == null) throw new Exception("Не найден .pth файл модели в архиве");

            byte[] modelData;
            using (var modelStream = modelEntry.Open())
            using (var ms = new MemoryStream()) {
                await modelStream.CopyToAsync(ms);
                modelData = ms.ToArray();
            }

            // Извлекаем config.json
            var configEntry = archive.Entries.FirstOrDefault(e =>
                !string.IsNullOrEmpty(e.Name) &&
                e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            );

            if (configEntry == null) throw new Exception("Не найден .json файл конфига в архиве");

            // Читаем config.json как байты и конвертируем в строку
            string configJson;
            using (var configStream = configEntry.Open()) {
                using var ms = new MemoryStream();
                await configStream.CopyToAsync(ms);
                var configBytes = ms.ToArray();

                // Пробуем UTF-8
                configJson = Encoding.UTF8.GetString(configBytes);

                // Удаляем BOM если есть
                if (configJson.StartsWith("\uFEFF")) {
                    configJson = configJson.Substring(1);
                }
            }

            // Логируем для отладки
            _logger.LogInformation($"Config JSON: {configJson}");

            // Проверяем, что wake_word не битый
            try {
                var testConfig = JsonSerializer.Deserialize<ModelConfig>(configJson);
                if (testConfig != null) {
                    _logger.LogInformation($"Wake word from config: '{testConfig.wake_word}'");
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Ошибка десериализации config.json");
            }

            progress.Report((95, "Сборка пакета для интеграции..."));

            // Собираем финальный пакет (с C# файлами)
            var finalPackageData = await _packageBuilder.BuildPackageAsync(modelData, configJson, request.WakeWord);

            // Диалог сохранения
            var saveFileDialog = new SaveFileDialog {
                Filter = "ZIP архив|*.zip",
                Title = "Сохранить пакет модели",
                FileName = $"{request.WakeWord}_model_package.zip"
            };

            if (saveFileDialog.ShowDialog() == true) {
                await File.WriteAllBytesAsync(saveFileDialog.FileName, finalPackageData, cancellationToken);
                progress.Report((100, $"Пакет сохранён: {saveFileDialog.FileName}"));
                return (true, finalPackageData, $"Пакет сохранён: {saveFileDialog.FileName}");
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
            // Убедись, что JSON сериализуется с правильной кодировкой
            var options = new JsonSerializerOptions {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(
                    System.Text.Unicode.UnicodeRanges.All
                )
            };

            var content = JsonContent.Create(request, options: options);
            var response = await _httpClient.PostAsync("/train-full-pipeline", content);
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

    public void Dispose() => _httpClient?.Dispose();
}