using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    public class TypeMetadata
    {
        public string Table { get; set; }
        public Dictionary<string, JoinMapping> JoinMappings { get; set; } = new();
    }

    public class JoinMapping
    {
        public string LeftColumn { get; set; }
        public string RightColumn { get; set; }
    }
}
