using AppBsky.Embed;
using AppBsky.Richtext;
using CarpaNet;
using CarpaNet.Blob;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;


namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.Bluesky
{
    public static class BlueskyUtilities
    {
        public static List<(int start, int end, string mention)> ExtractMentions(string text)
        {
            List<(int start, int end, string mention)> facets = new List<(int start, int end, string mention)>();
            var mentionRegex = new Regex(@"[$|\W](@([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)", RegexOptions.Compiled);
            var textBytes = Encoding.UTF8.GetBytes(text);

            foreach (Match m in mentionRegex.Matches(Encoding.UTF8.GetString(textBytes)))
            {
                var mention = m.Groups[1];
                facets.Add((GetUtf8ByteIndex(text, mention.Index), GetUtf8ByteIndex(text, mention.Index + mention.Length), mention.Value));
            }

            return facets;
        }

        public static async Task<List<(int start, int end, string url)>> ExtractUrls(string text)
        {
            List<(int start, int end, string url)> facets = new List<(int start, int end, string url)>();

            var urlRegex = new Regex(@"[$|\W](https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*[-a-zA-Z0-9@%_\+~#//=])?)", RegexOptions.Compiled);
            var textBytes = Encoding.UTF8.GetBytes(text);

            foreach (Match m in urlRegex.Matches(Encoding.UTF8.GetString(textBytes)))
            {
                var url = m.Groups[1];
                facets.Add((GetUtf8ByteIndex(text, url.Index), GetUtf8ByteIndex(text, url.Index + url.Length), url.Value));
            }

            return facets;
        }

        public static List<(int start, int end, string tag)> ExtractTags(string text)
        {
            List<(int start, int end, string tag)> facets = new List<(int start, int end, string tag)>();
            var hashtagRegex = new Regex(@"(?:^|\s)(#[^\d\s]\S*)(?=\s)?", RegexOptions.Compiled);
            foreach (Match match in hashtagRegex.Matches(text))
            {
                var tagMatch = match.Groups[1];
                string tag = tagMatch.Value;
                tag = tag.Trim().TrimEnd('.', ',', ';', '!', '?');

                if (tag.Length > 66) continue;

                int index = GetUtf8ByteIndex(text, tagMatch.Index);

                facets.Add((index, GetUtf8ByteIndex(text, tagMatch.Index + tag.Length), tag));
            }

            return facets;
        }

        private static int GetUtf8ByteIndex(string text, int charIndex)
        {
            return Encoding.UTF8.GetByteCount(text.AsSpan(0, charIndex));
        }

        public static async Task<ATDid?> GetDid(string handle, IATProtoClient atProtoClient)
        {
            var result = await atProtoClient.ComAtprotoIdentityResolveHandleAsync(
                new ComAtproto.Identity.ResolveHandleParameters { Handle = new ATHandle(handle.Replace("@", "")) });
            return result?.Did;
        }

        public static async Task<ImagesImage?> UploadImage(string url, IATProtoClient atProtoClient, List<Facet> facets, string postTemplate)
        {
            string encodedUrl = HtmlEncoder.Default.Encode(url);

            HttpClient client = new HttpClient();
            var card = await client.GetFromJsonAsync<BlueSkyCard>($"https://cardyb.bsky.app/v1/extract?url={encodedUrl}");

            if (card == null || string.IsNullOrEmpty(card.image))
            {
                return null;
            }
            else
            {
                var uri = new Uri(card.image);


                var uriWithoutQuery = uri.GetLeftPart(UriPartial.Path);
                var fileExtension = Path.GetExtension(uriWithoutQuery);

                var fileName = SanitizeFileName(card.title);

                try
                {
                    var imageBytes = await client.GetByteArrayAsync(uri);
                    await File.WriteAllBytesAsync(Path.Combine(Path.GetTempPath(), $"{fileName}.jpg"), imageBytes);

                    var blobRef = await atProtoClient.UploadBlobFromFileAsync(
                        Path.Combine(Path.GetTempPath(), $"{fileName}.jpg"),
                        "image/jpg");

                    var blob = new ATBlob(
                        new ATCid(blobRef.Ref?.Link ?? string.Empty),
                        blobRef.MimeType ?? "image/jpg",
                        blobRef.Size);

                    return new ImagesImage
                    {
                        Image = blob,
                        Alt = $"Embed Card for {url}"
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            return null;
        }

        public static JsonElement CreatePostRecord(string text, List<Facet> facets, ImagesImage? image)
        {
            var record = new BlueskyPostRecord
            {
                Text = text,
                CreatedAt = DateTimeOffset.UtcNow,
                Facets = facets.Select(CreateFacetRecord).ToList(),
                Embed = image is null ? null : new BlueskyImagesEmbed
                {
                    Images = new List<BlueskyImage> { new() { Image = image.Image, Alt = image.Alt } }
                }
            };

            return JsonSerializer.SerializeToElement(record);
        }

        public static string SanitizeFileName(string input)
        {
            // Define a regular expression to match invalid characters
            var regex = new Regex("[^a-zA-Z0-9_-]");

            // Replace invalid characters with an empty string
            var sanitized = regex.Replace(input, string.Empty);

            return sanitized;
        }

        private static BlueskyFacet CreateFacetRecord(Facet facet)
        {
            return new BlueskyFacet
            {
                Index = facet.Index,
                Features = facet.Features.Select(CreateFacetFeatureRecord).ToList()
            };
        }

        private static object CreateFacetFeatureRecord(IFacetFeatures feature)
        {
            return feature switch
            {
                FacetTag tag => new BlueskyFacetTag { Tag = tag.Tag },
                FacetLink link => new BlueskyFacetLink { Uri = link.Uri },
                FacetMention mention => new BlueskyFacetMention { Did = mention.Did },
                _ => throw new InvalidOperationException($"Unsupported Bluesky facet feature type: {feature.GetType().FullName}")
            };
        }

        private sealed class BlueskyPostRecord
        {
            [JsonPropertyName("$type")]
            public string Type => "app.bsky.feed.post";

            [JsonPropertyName("text")]
            public required string Text { get; set; }

            [JsonPropertyName("facets")]
            public required List<BlueskyFacet> Facets { get; set; }

            [JsonPropertyName("embed")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public BlueskyImagesEmbed? Embed { get; set; }

            [JsonPropertyName("createdAt")]
            public required DateTimeOffset CreatedAt { get; set; }
        }

        private sealed class BlueskyFacet
        {
            [JsonPropertyName("index")]
            public required FacetByteSlice Index { get; set; }

            [JsonPropertyName("features")]
            public required List<object> Features { get; set; }
        }

        private sealed class BlueskyFacetTag
        {
            [JsonPropertyName("$type")]
            public string Type => FacetTag.TypeId;

            [JsonPropertyName("tag")]
            public required string Tag { get; set; }
        }

        private sealed class BlueskyFacetLink
        {
            [JsonPropertyName("$type")]
            public string Type => FacetLink.TypeId;

            [JsonPropertyName("uri")]
            public required string Uri { get; set; }
        }

        private sealed class BlueskyFacetMention
        {
            [JsonPropertyName("$type")]
            public string Type => FacetMention.TypeId;

            [JsonPropertyName("did")]
            public required ATDid Did { get; set; }
        }

        private sealed class BlueskyImagesEmbed
        {
            [JsonPropertyName("$type")]
            public string Type => AppBsky.Embed.Images.TypeId;

            [JsonPropertyName("images")]
            public required List<BlueskyImage> Images { get; set; }
        }

        private sealed class BlueskyImage
        {
            [JsonPropertyName("image")]
            public required ATBlob Image { get; set; }

            [JsonPropertyName("alt")]
            public required string Alt { get; set; }
        }
    }
}
