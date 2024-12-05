using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class UploadRegistrationRequest
{
    [JsonPropertyName("registerUploadRequest")]
    public RegisterUploadRequest RegisterUploadRequest { get; set; }
}