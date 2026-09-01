using BusinessServices.Interface;
using Modal;
using Newtonsoft.Json;
using System.Text;

namespace BusinessServices.Implementation
{
    public class ScoreIframeService : IScoreIframeService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;

        public ScoreIframeService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            _baseUrl = AppCache.Settings.ScoreProvider.BaseUrl.TrimEnd('/');
        }

        public async Task<clsResponse> GetAsync(string eventId, int link, string color, string font)
        {
            if (string.IsNullOrEmpty(_baseUrl))
                return null;

            var client = _httpClientFactory.CreateClient();
            var body = JsonConvert.SerializeObject(new { eventID = eventId, link, color, font });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{_baseUrl}/api/ScoreIframe", content);
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<clsResponse>(json);
        }

        // Calls ScoreProvider's api/Scorecard WITHOUT color/font so the caller can cache
        // the clean base URL and apply decorators per-request.
        public async Task<ScoreResponse?> GetScorecardAsync(string eventId, int link, int pid)
        {
            if (string.IsNullOrEmpty(_baseUrl))
                return null;

            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{_baseUrl}/api/Scorecard?eventId={Uri.EscapeDataString(eventId)}&link={link}&pid={pid}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ScoreResponse>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
