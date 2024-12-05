using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class UploadMechanism
{
    /// <summary>
    /// Details to upload media
    /// </summary>
    [JsonPropertyName("com.linkedin.digitalmedia.uploading.MediaUploadHttpRequest")]
    public MediaUploadHttpRequest MediaUploadHttpRequest { get; set; }
}