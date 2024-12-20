﻿using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;

public class MediaUploadHttpRequest
{
    
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; }
    
    /// <summary>
    /// Use this URL to upload the media
    /// </summary>
    [JsonPropertyName("uploadUrl")]
    public string UploadUrl { get; set; }
}