using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class UploadRegistrationResponse
{
    [JsonPropertyName("value")]
    public Value Value { get; set; }
}