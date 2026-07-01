using TorchSharp;
using TorchSharp.PyBridge; // <-- ВАЖНО: добавить этот using

namespace Jarvis.WakeWord;

public static class ModelLoader {
    /// <summary>
    /// Загружает модель из .pth файла
    /// </summary>
    public static WakeWordModel LoadModel(string modelPath, int inputHeight, int inputWidth) {
        var model = new WakeWordModel(inputHeight, inputWidth);

        using (torch.no_grad()) {
            model.load_py(modelPath); // <-- теперь работает
            model.eval();
        }

        return model;
    }
}