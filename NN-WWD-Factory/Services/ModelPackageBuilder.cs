using Jarvis.WakeWord;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace NN_WWD_Factory.Services {
    public class ModelPackageBuilder {
        public async Task<byte[]> BuildPackageAsync(byte[] modelData, string configJson, string wakeWord) {
            // Пытаемся десериализовать config, но если не получается - используем fallback
            ModelConfig? config;
            try {
                config = JsonSerializer.Deserialize<ModelConfig>(configJson);
                if (config == null || string.IsNullOrWhiteSpace(config.wake_word) || config.wake_word.Trim() == "") {
                    // Если wake_word битый, исправляем
                    config = JsonSerializer.Deserialize<ModelConfig>(configJson);
                    if (config != null) {
                        config.wake_word = wakeWord;
                        config.model_name = wakeWord;
                    }
                }
            }
            catch {
                config = null;
            }

            if (config == null) {
                // Создаём fallback конфиг
                config = new ModelConfig {
                    input_height = 128,
                    input_width = 128,
                    n_mels = 128,
                    n_fft = 512,
                    hop_length = 160,
                    sample_rate = 24000,
                    wake_word = wakeWord,
                    model_name = wakeWord,
                    epochs = 10,
                    batch_size = 32,
                    best_val_acc = 0.0f,
                    created_at = DateTime.Now.ToString("o")
                };
            }

            // Убеждаемся, что wake_word правильный
            if (string.IsNullOrWhiteSpace(config.wake_word) || config.wake_word.Trim() == "") {
                config.wake_word = wakeWord;
                config.model_name = wakeWord;
            }

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true)) {
                // 1. Добавляем .pth с правильным именем
                var modelEntry = archive.CreateEntry($"{wakeWord}_model.pth");
                using (var modelStream = modelEntry.Open()) {
                    await modelStream.WriteAsync(modelData, 0, modelData.Length);
                }

                // 2. Добавляем config.json (перезаписываем с правильным wake_word)
                var configJsonFixed = JsonSerializer.Serialize(config, new JsonSerializerOptions {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(
                        System.Text.Unicode.UnicodeRanges.All
                    )
                });
                var configEntry = archive.CreateEntry("config.json");
                using (var configStream = configEntry.Open())
                using (var writer = new StreamWriter(configStream, Encoding.UTF8)) {
                    await writer.WriteAsync(configJsonFixed);
                }

                // 3. Генерируем и добавляем C# файлы
                AddCSharpFiles(archive, config);
            }

            return memoryStream.ToArray();
        }

        // Остальной код без изменений...
        private void AddCSharpFiles(ZipArchive archive, ModelConfig config) {
            if (config == null) {
                throw new Exception("Конфиг модели не может быть null");
            }

            // Генерируем WakeWordModel.cs
            var modelCode = GenerateWakeWordModel(config);
            var modelEntry = archive.CreateEntry("WakeWordModel.cs");
            using (var modelStream = modelEntry.Open())
            using (var modelWriter = new StreamWriter(modelStream, Encoding.UTF8)) {
                modelWriter.Write(modelCode);
            }

            // Генерируем ModelLoader.cs
            var loaderCode = GenerateModelLoader();
            var loaderEntry = archive.CreateEntry("ModelLoader.cs");
            using (var loaderStream = loaderEntry.Open())
            using (var loaderWriter = new StreamWriter(loaderStream, Encoding.UTF8)) {
                loaderWriter.Write(loaderCode);
            }

            // Генерируем AudioPreprocessor.cs
            var audioCode = GenerateAudioPreprocessor();
            var audioEntry = archive.CreateEntry("AudioPreprocessor.cs");
            using (var audioStream = audioEntry.Open())
            using (var audioWriter = new StreamWriter(audioStream, Encoding.UTF8)) {
                audioWriter.Write(audioCode);
            }

            // Генерируем WakeWordDetector.cs
            var detectorCode = GenerateWakeWordDetector();
            var detectorEntry = archive.CreateEntry("WakeWordDetector.cs");
            using (var detectorStream = detectorEntry.Open())
            using (var detectorWriter = new StreamWriter(detectorStream, Encoding.UTF8)) {
                detectorWriter.Write(detectorCode);
            }
        }

        private string GenerateWakeWordModel(ModelConfig config) => $@"using TorchSharp;
                    using static TorchSharp.torch;
                    using static TorchSharp.torch.nn;

                    namespace Jarvis.WakeWord
                    {{
                        public class WakeWordModel : nn.Module<Tensor, Tensor>
                        {{
                            private readonly nn.Module<Tensor, Tensor> conv1;
                            private readonly nn.Module<Tensor, Tensor> conv2;
                            private readonly nn.Module<Tensor, Tensor> pool;
                            private readonly nn.Module<Tensor, Tensor> fc1;
                            private readonly nn.Module<Tensor, Tensor> fc2;
                            private readonly nn.Module<Tensor, Tensor> dropout;

                            public WakeWordModel(int inputHeight, int inputWidth, int numClasses = 2) : base(""WakeWordModel"")
                            {{
                                conv1 = nn.Conv2d(1, 16, 3, padding: 1);
                                conv2 = nn.Conv2d(16, 32, 3, padding: 1);
                                pool = nn.MaxPool2d(2, 2);

                                int h = inputHeight / 4;
                                int w = inputWidth / 4;
                                int flattenedSize = 32 * h * w;

                                fc1 = nn.Linear(flattenedSize, 64);
                                fc2 = nn.Linear(64, numClasses);
                                dropout = nn.Dropout(0.3);

                                RegisterComponents();
                            }}

                            public override Tensor forward(Tensor input)
                            {{
                                var x = input;
                                x = pool.forward(nn.functional.relu(conv1.forward(x)));
                                x = pool.forward(nn.functional.relu(conv2.forward(x)));
                                x = x.view(x.shape[0], -1);
                                x = nn.functional.relu(fc1.forward(x));
                                x = dropout.forward(x);
                                return fc2.forward(x);
                            }}
                        }}
                    }}";

        private string GenerateModelLoader() => @"using TorchSharp;
                    using static TorchSharp.torch;
                    using TorchSharp.PyBridge;

                    namespace Jarvis.WakeWord
                    {
                        public static class ModelLoader
                        {
                            public static WakeWordModel LoadModel(string modelPath, int inputHeight, int inputWidth)
                            {
                                var model = new WakeWordModel(inputHeight, inputWidth);
                                using (no_grad())
                                {
                                    model.load_py(modelPath);
                                    model.eval();
                                }
                                return model;
                            }
                        }
                    }";

        private string GenerateAudioPreprocessor() => @"using TorchSharp;
                    using static TorchSharp.torch;
                    using MathNet.Numerics.IntegralTransforms;

                    namespace Jarvis.WakeWord
                    {
                        public static class AudioPreprocessor
                        {
                            public static Tensor ToMelSpectrogram(float[] audioSamples, int sampleRate = 16000)
                            {
                                const int N_FFT = 512;
                                const int HOP_LENGTH = 160;
                                const int INPUT_WIDTH = 128;
                                const int N_MELS = 128;

                                int nFrames = (audioSamples.Length - N_FFT) / HOP_LENGTH + 1;
                                var stft = new float[N_FFT / 2 + 1, nFrames];

                                for (int frame = 0; frame < nFrames; frame++)
                                {
                                    var window = new Complex32[N_FFT];
                                    for (int i = 0; i < N_FFT; i++)
                                    {
                                        int idx = frame * HOP_LENGTH + i;
                                        window[i] = (idx < audioSamples.Length) ? (Complex32)audioSamples[idx] : Complex32.Zero;
                                    }

                                    for (int i = 0; i < N_FFT; i++)
                                        window[i] *= (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (N_FFT - 1)));

                                    Fourier.Forward(window, FourierOptions.Matlab);

                                    for (int i = 0; i < N_FFT / 2 + 1; i++)
                                        stft[i, frame] = (float)window[i].Magnitude;
                                }

                                var melSpec = new float[N_MELS, nFrames];
                                for (int mel = 0; mel < N_MELS; mel++)
                                    for (int frame = 0; frame < nFrames; frame++)
                                    {
                                        double sum = 0;
                                        for (int f = 0; f < N_FFT / 2 + 1; f++)
                                            sum += stft[f, frame] * 1.0;
                                        melSpec[mel, frame] = (float)Math.Log(sum + 1e-8);
                                    }

                                if (melSpec.GetLength(1) > INPUT_WIDTH)
                                {
                                    var trimmed = new float[N_MELS, INPUT_WIDTH];
                                    for (int i = 0; i < N_MELS; i++)
                                        for (int j = 0; j < INPUT_WIDTH; j++)
                                            trimmed[i, j] = melSpec[i, j];
                                    melSpec = trimmed;
                                }

                                var tensor = torch.tensor(melSpec);
                                tensor = tensor.unsqueeze(0).unsqueeze(0);
                                return tensor.to(torch.float32);
                            }
                        }
                    }";

        private string GenerateWakeWordDetector() => @"using System;
                    using System.Text.Json;
                    using System.IO;
                    using TorchSharp;
                    using static TorchSharp.torch;

                    namespace Jarvis.WakeWord
                    {
                        public class WakeWordDetector : IDisposable
                        {
                            private readonly WakeWordModel _model;
                            private readonly int _inputHeight;
                            private readonly int _inputWidth;
                            private readonly int _sampleRate;
                            private readonly float _threshold;
                            private bool _disposed = false;

                            public WakeWordDetector(string modelPath, string configPath, float threshold = 0.8f)
                            {
                                var configJson = File.ReadAllText(configPath);
                                var config = JsonSerializer.Deserialize<ModelConfig>(configJson);
                                if (config == null) throw new InvalidOperationException(""Не удалось загрузить конфиг модели"");

                                _inputHeight = config.input_height;
                                _inputWidth = config.input_width;
                                _sampleRate = config.sample_rate;
                                _threshold = threshold;

                                _model = ModelLoader.LoadModel(modelPath, _inputHeight, _inputWidth);
                            }

                            public bool Detect(float[] audioSamples)
                            {
                                if (audioSamples == null || audioSamples.Length == 0) return false;

                                try
                                {
                                    var melSpec = AudioPreprocessor.ToMelSpectrogram(audioSamples, _sampleRate);
                                    using (no_grad())
                                    {
                                        var output = _model.forward(melSpec);
                                        var probabilities = torch.softmax(output, 1);
                                        var positiveProb = probabilities[0, 1].item<float>();
                                        return positiveProb > _threshold;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($""Ошибка детекции: {ex.Message}"");
                                    return false;
                                }
                            }

                            public void Dispose()
                            {
                                if (!_disposed)
                                {
                                    _model?.Dispose();
                                    _disposed = true;
                                }
                            }
                        }

                        public class ModelConfig
                        {
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
                    }";
        }
}