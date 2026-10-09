using System.Text.Json.Serialization;

namespace ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class UploadRegistrationResponse
{
    [JsonPropertyName("value")]
    public Value Value { get; set; }
}