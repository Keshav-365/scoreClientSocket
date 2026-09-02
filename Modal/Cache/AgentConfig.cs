using System.Collections.Generic;

namespace Modal.Cache
{
    public class AgentConfig
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string ExpiryDate { get; set; }
        public List<string> AllowedProvider { get; set; } = new();
        // Per-agent IP whitelist. Empty = allowed from any IP.
        public List<string> AllowedIPs { get; set; } = new();
    }
}
