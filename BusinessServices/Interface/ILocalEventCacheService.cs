using Modal.ConcurrentDic;

namespace BusinessServices.Interface
{
    public interface ILocalEventCacheService
    {
        bool Exists(int eventid);
        EventState? Get(int eventid);
        Task<EventState?> GetAsync(int eventid);
        List<EventState> GetAll();
        EventState Upsert(int eventid, object? scoredata, object? shortscoredata, string eventstatus);
        Task<EventState> UpsertAsync(int eventid, object? scoredata, object? shortscoredata, string eventstatus);
        void UpdateConnectionTime(int eventid);
        Task UpdateConnectionTimeAsync(int eventid);
        void Remove(int eventid);
    }
}
