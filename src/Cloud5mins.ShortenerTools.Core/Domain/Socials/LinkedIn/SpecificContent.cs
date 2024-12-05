using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class SpecificContent
{
    [JsonPropertyName("com.linkedin.ugc.ShareContent")]
    public ShareContent ShareContent { get; set; }
}