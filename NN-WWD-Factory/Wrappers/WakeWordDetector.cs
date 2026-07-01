using System.Text.Json;
using System.IO;
using TorchSharp;
using static TorchSharp.torch;

namespace Jarvis.WakeWord;

/// <summary>
/// Детектор wake word на основе обученной модели
/// </summary>
public class WakeWordDetector : IDisposable {
    private readonly WakeWordModel _model;
    private readonly int _inputHeight;
    private readonly int _inputWidth;
    private readonly int _sampleRate;
    private readonly float _threshold;

    private bool _disposed = false;

    public WakeWordDetector(string modelPath, string configPath, float threshold = 0.8f) {
        // 1. Загружаем конфиг
        var configJson = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ModelConfig>(configJson);

        if (config == null)
            throw new InvalidOperationException("Не удалось загрузить конфиг модели");

        _inputHeight = config.input_height;
        _inputWidth = config.input_width;
        _sampleRate = config.sample_rate;
        _threshold = threshold;

        // 2. Загружаем модель
        _model = ModelLoader.LoadModel(modelPath, _inputHeight, _inputWidth);
    }

    /// <summary>
    /// Проверяет, содержит ли аудио-фрагмент wake word
    /// </summary>
    /// <param name="audioSamples">Аудио-данные (float массив, частота 16000 Гц)</param>
    /// <returns>true, если wake word обнаружен</returns>
    public bool Detect(float[] audioSamples) {
        if (audioSamples == null || audioSamples.Length == 0)
            return false;

        try {
            // 1. Преобразуем аудио в Mel-спектрограмму
            var melSpec = AudioPreprocessor.ToMelSpectrogram(audioSamples, _sampleRate);

            // 2. Инференс
            using (no_grad()) {
                var output = _model.forward(melSpec);
                var probabilities = torch.softmax(output, 1);
                var positiveProb = probabilities[0, 1].item<float>();

                return positiveProb > _threshold;
            }
        }
        catch (Exception ex) {
            // Логирование ошибки (можно заменить на ILogger)
            Console.WriteLine($"Ошибка детекции: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Проверяет, содержит ли аудио-фрагмент wake word (с возвратом вероятности)
    /// </summary>
    public (bool detected, float probability) DetectWithProbability(float[] audioSamples) {
        if (audioSamples == null || audioSamples.Length == 0)
            return (false, 0f);

        try {
            var melSpec = AudioPreprocessor.ToMelSpectrogram(audioSamples, _sampleRate);

            using (no_grad()) {
                var output = _model.forward(melSpec);
                var probabilities = torch.softmax(output, 1);
                var positiveProb = probabilities[0, 1].item<float>();

                return (positiveProb > _threshold, positiveProb);
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Ошибка детекции: {ex.Message}");
            return (false, 0f);
        }
    }

    public void Dispose() {
        if (!_disposed) {
            _model?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Модель конфигурации (соответствует config.json)
/// </summary>
public class ModelConfig {
    public int input_height { get; set; }
    public int input_width { get; set; }
    public int n_mels { get; set; }
    public int n_fft { get; set; }
    public int hop_length { get; set; }
    public int sample_rate { get; set; }
    public string wake_word { get; set; } = string.Empty;
    public string model_name { get; set; } = string.Empty;
    public int epochs { get; set; }
    public int batch_size { get; set; }
    public float best_val_acc { get; set; }
    public string created_at { get; set; } = string.Empty;
}