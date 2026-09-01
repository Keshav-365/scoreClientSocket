using Modal;

namespace BusinessServices.Interface
{
    public interface IScoreIframeService
    {
        Task<clsResponse> GetAsync(string eventId, int link, string color, string font);
        Task<ScoreResponse?> GetScorecardAsync(string eventId, int link, int pid);
    }
}
