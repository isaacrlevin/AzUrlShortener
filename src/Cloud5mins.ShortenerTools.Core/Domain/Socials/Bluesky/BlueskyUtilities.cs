using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Embed;
using FishyFlip.Lexicon.App.Bsky.Richtext;
using FishyFlip.Models;
using FishyFlip.Tools;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;


namespace Cloud5mins.ShortenerTools.Core.Domain.Socials.Bluesky
{
    public static class BlueskyUtilities
    {
        public static (int promptStart, int promptEnd) ParsePrompt(string haystack, string needle)
        {
            int promptStart = haystack.IndexOf(needle, StringComparison.InvariantCulture);
            int promptEnd = promptStart + Encoding.Default.GetBytes(needle).Length;

            return (promptStart, promptEnd);
        }

        public static List<string> ExtractHashtags(string post)
        {
            var hashtags = new List<string>();
            var regex = new Regex(@"#\w+");
            var matches = regex.Matches(post);

            foreach (Match match in matches)
            {
                hashtags.Add(match.Value);
            }

            return hashtags;
        }

        public static async Task<ATDid> GetDid(string handle, ATProtocol atProtocol)
        {
            var handleResolution = (await atProtocol.Identity.ResolveHandleAsync(ATHandle.Create(handle))).HandleResult();
            return handleResolution?.Did;
        }

        public static async Task<Image> UploadImage(string url, ATProtocol atProtocol, List<Facet> facets, string postTemplate)
        {
            string encodedUrl = HtmlEncoder.Default.Encode(url);

            HttpClient client = new HttpClient();
            var card = await client.GetFromJsonAsync<BlueSkyCard>($"https://cardyb.bsky.app/v1/extract?url={encodedUrl}");

            if (card == null)
            {
                return null;
            }
            else
            {
                var uri = new Uri(card.image);


                var uriWithoutQuery = uri.GetLeftPart(UriPartial.Path);
                var fileExtension = Path.GetExtension(uriWithoutQuery);

                var fileName = card.title;

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

                        (int promptStart, int promptEnd) = BlueskyUtilities.ParsePrompt(postTemplate, url);

                        facets.Add(Facet.CreateFacetLink(promptStart, promptEnd, url));
                    },
                    async error =>
                    {
                        Console.WriteLine($"Error: {error.StatusCode} {error.Detail}");
                    }
                    );
                return image;
            }
        }
    }
}
