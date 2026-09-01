using Modal;

namespace BusinessServices.Interface
{
    public interface IScorecardCacheService
    {
        ScoreResponse? Get(string eventId, int link, int pid);
        void Set(string eventId, int link, int pid, ScoreResponse response);
        void Remove(string eventId);
        void RemoveAll();
    }
}
