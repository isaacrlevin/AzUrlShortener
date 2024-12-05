using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class PreferredLocale
{
    [JsonPropertyName("country")]
    public string Country { get; set; }
    
    [JsonPropertyName("language")]
    public string Language { get; set; }
}