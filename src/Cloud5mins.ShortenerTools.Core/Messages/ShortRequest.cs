using Cloud5mins.ShortenerTools.Core.Domain;

namespace Cloud5mins.ShortenerTools.Core.Messages
{
    public class ShortRequest
    {
        public string Vanity { get; set; }

        public string Url { get; set; }

        public string Title { get; set; }

        public string Message { get; set; }

        public bool PostToSocial { get; set; } = true;

        public Schedule[] Schedules { get; set; }
    }
}