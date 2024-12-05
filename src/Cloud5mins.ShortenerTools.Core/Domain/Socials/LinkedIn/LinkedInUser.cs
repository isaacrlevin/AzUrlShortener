using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;





public class LinkedInUser
{
    [JsonPropertyName("sub")]
    public string Sub { get; set; }

    [JsonPropertyName("email_verified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("locale")]
    public Locale Locale { get; set; }

    [JsonPropertyName("given_name")]
    public string GivenName { get; set; }

    [JsonPropertyName("family_name")]
    public string FamilyName { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("picture")]
    public string Picture { get; set; }
}

public class Locale
{
    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }
}
