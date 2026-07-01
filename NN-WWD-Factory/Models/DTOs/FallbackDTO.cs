using System.Text.Json.Serialization;

namespace NN_WWD_Factory.Models.DTOs;

public class FallbackDTO {
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("config_path")]
    public string ConfigPath { get; set; } = string.Empty;
}