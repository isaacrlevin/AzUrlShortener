using ShortenerTools.Core.Domain;

namespace ShortenerTools.Core.Messages
{
    public class ShortRequest
    {
        public string Vanity { get; set; }

        public string Url { get; set; }

        public string Title { get; set; }

        public string Message { get; set; }

        public bool PostToSocial { get; set; } = true;
    }
}