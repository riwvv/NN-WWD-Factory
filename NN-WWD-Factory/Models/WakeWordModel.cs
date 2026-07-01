using TorchSharp;
using static TorchSharp.torch;

namespace Jarvis.WakeWord {
    public class WakeWordModel : nn.Module<Tensor, Tensor> {
        private readonly nn.Module<Tensor, Tensor> conv1;
        private readonly nn.Module<Tensor, Tensor> conv2;
        private readonly nn.Module<Tensor, Tensor> pool;
        private readonly nn.Module<Tensor, Tensor> fc1;
        private readonly nn.Module<Tensor, Tensor> fc2;
        private readonly nn.Module<Tensor, Tensor> dropout;

        public WakeWordModel(int inputHeight, int inputWidth, int numClasses = 2) : base("WakeWordModel") {
            // Свёрточные слои
            conv1 = nn.Conv2d(1, 16, 3, padding: 1);      // kernelSize → 3 (позиционный аргумент)
            conv2 = nn.Conv2d(16, 32, 3, padding: 1);
            pool = nn.MaxPool2d(2, 2);

            // Вычисляем размер после свёрток и пулинга
            int h = inputHeight / 4;
            int w = inputWidth / 4;
            int flattenedSize = 32 * h * w;

            // Полносвязные слои
            fc1 = nn.Linear(flattenedSize, 64);
            fc2 = nn.Linear(64, numClasses);
            dropout = nn.Dropout(0.3);

            RegisterComponents();
        }

        public override Tensor forward(Tensor input) {
            var x = input;
            x = pool.forward(nn.functional.relu(conv1.forward(x)));
            x = pool.forward(nn.functional.relu(conv2.forward(x)));
            x = x.view(x.shape[0], -1);
            x = nn.functional.relu(fc1.forward(x));
            x = dropout.forward(x);
            return fc2.forward(x);
        }
    }
}