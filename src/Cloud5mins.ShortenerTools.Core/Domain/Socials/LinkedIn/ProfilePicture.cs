using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class ProfilePicture
{
    [JsonPropertyName("displayImage")]
    public string DisplayImage { get; set; }
}