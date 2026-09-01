namespace Modal.ConcurrentDic
{
    public class EventState
    {
        public int eventid { get; set; }
        public DateTime lastupdatetime { get; set; }
        public DateTime lastconnectiontime { get; set; }
        public object? scoredata { get; set; }
        public object? shortscoredata { get; set; }
        public string eventstatus { get; set; } = "live";
    }
}
