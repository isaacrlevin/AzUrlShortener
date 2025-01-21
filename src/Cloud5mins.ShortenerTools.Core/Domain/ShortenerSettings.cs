namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class ShortenerSettings
    {
        public string DefaultRedirectUrl { get; set; }
        public string CustomDomain { get; set; }
        public string DataStorage { get; set; }
        public string TwitterConsumerKey { get; set; }
        public string TwitterConsumerSecret { get; set; }
        public string TwitterAccessToken { get; set; }
        public string TwitterAccessSecret { get; set; }
        public string MastodonAccessToken { get; set; }
        public string LinkedInAccessToken { get; set; }
        public string BlueskyUserName { get; set; }
        public string BlueskyPassword { get; set; }
        public bool PostSocials { get; set; }
        public string EmailFrom { get; set; }
        public string EmailTo { get; set; }
        public string COMMUNICATION_SERVICES_CONNECTION_STRING { get; set; }
        
        public string ThreadsToken { get; set; }
    }
}