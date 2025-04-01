using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Embed;
using FishyFlip.Lexicon.App.Bsky.Richtext;
using FishyFlip.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
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
                facets.Add((m.Index, m.Index + m.Length, m.Groups[1].Value));
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
                facets.Add((m.Index, m.Index + m.Length, m.Groups[1].Value));
            }

            return facets;
        }

        public static List<(int start, int end, string tag)> ExtractTags(string text)
        {
            List<(int start, int end, string tag)> facets = new List<(int start, int end, string tag)>();
            var hashtagRegex = new Regex(@"(?:^|\s)(#[^\d\s]\S*)(?=\s)?", RegexOptions.Compiled);
            foreach (Match match in hashtagRegex.Matches(text))
            {
                string tag = match.Groups[1].Value;
                bool hasLeadingSpace = Regex.IsMatch(tag, @"^\s");
                tag = tag.Trim().TrimEnd('.', ',', ';', '!', '?');

                if (tag.Length > 66) continue;

                int index = match.Index + (hasLeadingSpace ? 1 : 0);

                facets.Add((index, index + tag.Length, tag));
            }

            return facets;
        }

        public static async Task<ATDid> GetDid(string handle, ATProtocol atProtocol)
        {
            var handleResolution = (await atProtocol.Identity.ResolveHandleAsync(ATHandle.Create(handle.Replace("@","")))).HandleResult();
            return handleResolution?.Did;
        }

        public static async Task<Image> UploadImage(string url, ATProtocol atProtocol, List<Facet> facets, string postTemplate)
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

                    var stream = File.OpenRead(Path.Combine(Path.GetTempPath(), $"{fileName}.jpg"));
                    var content = new StreamContent(stream);
                    content.Headers.ContentLength = stream.Length;

                    content.Headers.ContentType = new MediaTypeHeaderValue("image/jpg");
                    var blobResult = await atProtocol.Repo.UploadBlobAsync(content);

                    Image image = null;
                    await blobResult.SwitchAsync(
                        async success =>
                        {
                            //image = success.Blob.ToImage();

                            image = new Image(
                                image: success.Blob,
                                 alt: $"Embed Card for {url}"
                                );

                        },
                        async error =>
                        {
                            Console.WriteLine($"Error: {error.StatusCode} {error.Detail}");
                        }
                        );
                    return image;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            return null;
        }

        public static string SanitizeFileName(string input)
        {
            // Define a regular expression to match invalid characters
            var regex = new Regex("[^a-zA-Z0-9_-]");

            // Replace invalid characters with an empty string
            var sanitized = regex.Replace(input, string.Empty);

            return sanitized;
        }
    }
}
