using Newtonsoft.Json;

namespace Modal
{
    public class ScoreResponse
    {
        public int EventID { get; set; }

        [JsonProperty("scoreUrl")]
        public string ScoreUrl { get; set; }

        [JsonProperty("streamingUrl")]
        public string StreamingUrl { get; set; }
    }
}
