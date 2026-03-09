using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitterScheduler.Models
{
    public class SocialsConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Dictionary<string, string>);

        public override object ReadJson(
          JsonReader reader,
          Type objectType,
          object existingValue,
          JsonSerializer serializer)
        {
            Dictionary<string, string> dictionary = new Dictionary<string, string>();
            foreach (JObject jobject in JArray.Load(reader))
            {
                foreach (JProperty property in jobject.Properties())
                    dictionary[property.Name] = property.Value.ToString();
            }
            return dictionary;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotImplementedException();
    }
}
