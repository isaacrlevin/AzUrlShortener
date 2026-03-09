using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitterScheduler.Models
{
    public class Guest
    {
        public string PartitionKey { get; set; }

        public int RowKey { get; set; }

        public string DateTimeAsString { get; set; }

        public DateTime DateTimeUTC { get; set; }

        public string GuestName { get; set; }

        public string GuestHandle { get; set; }

        public string GuestLink { get; set; }

        public bool IsPublished { get; set; }

        public string Topic { get; set; }

        public string YouTubeVideoId { get; set; }

        public string GuestBio { get; set; }

        public bool HaveAudio { get; set; }

        public string SpotifyLink { get; set; }

        public string GPLink { get; set; }

        public string APLink { get; set; }

        [JsonConverter(typeof(SocialsConverter))]
        public Dictionary<string, string> Socials { get; set; }

        public List<Social> SocialsConverted { get; set; }
    }
}
