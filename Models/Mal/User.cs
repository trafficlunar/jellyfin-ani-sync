using System;
using System.Text.Json.Serialization;

namespace jellyfin_ani_sync.Models.Mal; 

public class User {
    [JsonPropertyName("id")] 
    public int Id { get; init; }
    [JsonPropertyName("name")] 
    public string Name { get; init; }
    [JsonPropertyName("location")] 
    public string Location { get; init; }
    [JsonPropertyName("joined_at")] 
    public DateTime JoinedAt { get; init; }
    [JsonPropertyName("picture")] 
    public string Picture { get; init; }
}