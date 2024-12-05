using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class LocalizedInformation
{
    [JsonPropertyName("localized")]
    public Dictionary<string, string> Localized { get; set; }
    
    [JsonPropertyName("preferredLocale")]
    public PreferredLocale PreferredLocale { get; set; }
}