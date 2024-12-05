using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class ShareResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("serviceErrorCode")]
    public int? ServiceErrorCode { get; set; }
    
    [JsonPropertyName("status")]
    public int? Status { get; set; }
    
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    public bool IsSuccess => !string.IsNullOrEmpty(Id);
}