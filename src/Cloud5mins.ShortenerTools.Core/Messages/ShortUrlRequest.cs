using Cloud5mins.ShortenerTools.Core.Domain;
using System.ComponentModel.DataAnnotations;

namespace Cloud5mins.ShortenerTools.Core.Messages
{
    public class ShortUrlRequest
    {
        private string _vanity;

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public bool PostToSocial { get; set; } = true;

        public string Vanity
        {
            get
            {
                return _vanity != null ? _vanity : string.Empty;
            }
            set
            {
                _vanity = value;
            }
        }

        [Required]
        [Url]
        public string Url { get; set; }
    }
}