using System.Text.Json.Serialization;

namespace ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class ProfilePicture
{
    [JsonPropertyName("displayImage")]
    public string DisplayImage { get; set; }
}