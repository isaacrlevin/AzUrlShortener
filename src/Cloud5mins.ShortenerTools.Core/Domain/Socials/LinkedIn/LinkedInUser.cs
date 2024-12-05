using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

//public class LinkedInUser
//{
//    [JsonPropertyName("id")]
//    public string Id { get; set; }

//    [JsonPropertyName("profilePicture")]
//    public ProfilePicture ProfilePicture { get; set; }

//    [JsonPropertyName("vanityName")]
//    public string VanityName { get; set; }

//    [JsonPropertyName("localizedFirstName")]
//    public string FirstName { get; set; }

//    [JsonPropertyName("localizedLastName")]
//    public string LastName { get; set; }

//    [JsonPropertyName("localizedHeadline")]
//    public string Headline { get; set; }

//    [JsonPropertyName("firstName")]
//    public LocalizedInformation LocalizedFirstName { get; set; }

//    [JsonPropertyName("lastName")]
//    public LocalizedInformation LocalizedLastName { get; set; }

//    [JsonPropertyName("headline")]
//    public LocalizedInformation LocalizedHeadline { get; set; }
//}



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
