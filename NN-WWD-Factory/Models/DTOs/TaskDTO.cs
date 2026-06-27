using System.Text.Json.Serialization;

namespace NN_WWD_Factory.Models.DTOs;

public class TaskDTO {
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
