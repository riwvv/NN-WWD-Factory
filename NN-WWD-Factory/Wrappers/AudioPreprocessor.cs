using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using TorchSharp;
using static TorchSharp.torch;

namespace Jarvis.WakeWord;

public static class AudioPreprocessor {
    private const int N_MELS = 128;
    private const int N_FFT = 512;
    private const int HOP_LENGTH = 160;
    private const int INPUT_WIDTH = 128;

    /// <summary>
    /// Преобразует аудио-массив в Mel-спектрограмму
    /// </summary>
    /// <param name="audioSamples">Аудио-массив (частота 16000 Гц)</param>
    /// <param name="sampleRate">Частота дискретизации (должна совпадать с config.json)</param>
    public static Tensor ToMelSpectrogram(float[] audioSamples, int sampleRate = 16000) {
        // 1. Если длина не кратна HOP_LENGTH, обрезаем или дополняем
        int nFrames = (audioSamples.Length - N_FFT) / HOP_LENGTH + 1;
        if (nFrames < INPUT_WIDTH) {
            // Дополняем нулями
            var padded = new float[INPUT_WIDTH * HOP_LENGTH + N_FFT];
            Array.Copy(audioSamples, padded, Math.Min(audioSamples.Length, padded.Length));
            audioSamples = padded;
            nFrames = (audioSamples.Length - N_FFT) / HOP_LENGTH + 1;
        }

        // 2. STFT (упрощённая версия, для реального проекта используй библиотеку)
        var stft = ComputeSTFT(audioSamples, N_FFT, HOP_LENGTH);

        // 3. Mel-фильтры
        var melFilterbank = CreateMelFilterbank(sampleRate, N_FFT, N_MELS);

        // 4. Применяем Mel-фильтры и берём логарифм
        var melSpec = ApplyMelFilterbank(stft, melFilterbank);

        // 5. Приводим к размеру INPUT_WIDTH
        // 5. Приводим к размеру INPUT_WIDTH
        if (melSpec.GetLength(1) > INPUT_WIDTH) {
            var trimmed = new float[melSpec.GetLength(0), INPUT_WIDTH];
            for (int i = 0; i < melSpec.GetLength(0); i++) {
                for (int j = 0; j < INPUT_WIDTH; j++) {
                    trimmed[i, j] = melSpec[i, j];
                }
            }
            melSpec = trimmed;
        }

        // 6. Преобразуем в тензор и нормализуем
        var tensor = torch.tensor(melSpec);
        tensor = tensor.unsqueeze(0).unsqueeze(0); // добавляем batch и channel
        tensor = tensor.to(torch.float32);

        return tensor;
    }

    private static float[,] ComputeSTFT(float[] audio, int nFft, int hopLength) {
        int nFrames = (audio.Length - nFft) / hopLength + 1;
        var stft = new float[nFft / 2 + 1, nFrames];

        for (int frame = 0; frame < nFrames; frame++) {
            var window = new Complex32[nFft];
            for (int i = 0; i < nFft; i++) {
                int idx = frame * hopLength + i;
                window[i] = (idx < audio.Length) ? (Complex32)audio[idx] : Complex32.Zero;
            }

            // Оконная функция Хэмминга
            for (int i = 0; i < nFft; i++) {
                window[i] *= (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (nFft - 1)));
            }

            Fourier.Forward(window, FourierOptions.Matlab);

            for (int i = 0; i < nFft / 2 + 1; i++) {
                stft[i, frame] = (float)window[i].Magnitude;
            }
        }

        return stft;
    }

    private static float[,] CreateMelFilterbank(int sampleRate, int nFft, int nMel) {
        // Упрощённая версия Mel-фильтров
        // Для реального проекта лучше использовать готовую библиотеку
        var melBins = new float[nMel, nFft / 2 + 1];
        // ... реализация Mel-фильтров (сложная часть)
        return melBins;
    }

    private static float[,] ApplyMelFilterbank(float[,] stft, float[,] melFilterbank) {
        int nFrames = stft.GetLength(1);
        int nMel = melFilterbank.GetLength(0);
        var melSpec = new float[nMel, nFrames];

        for (int mel = 0; mel < nMel; mel++) {
            for (int frame = 0; frame < nFrames; frame++) {
                double sum = 0;
                for (int f = 0; f < stft.GetLength(0); f++) {
                    sum += stft[f, frame] * melFilterbank[mel, f];
                }
                melSpec[mel, frame] = (float)Math.Log(sum + 1e-8);
            }
        }

        return melSpec;
    }
}