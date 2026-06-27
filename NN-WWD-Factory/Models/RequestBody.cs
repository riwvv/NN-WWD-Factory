using System.Text.Json.Serialization;

namespace NN_WWD_Factory.Models;

public class RequestBody {
    [JsonPropertyName("wake_word")]
    public string WakeWord { get; set; } = string.Empty;
    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; } = 24000;
    [JsonPropertyName("count_per_text")]
    public int CountPerText { get; set; }
    [JsonPropertyName("negative_count")]
    public int NegativeCount { get; set; }
    [JsonPropertyName("epochs")]
    public int Epochs { get; set; }
}
