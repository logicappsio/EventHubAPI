using System.ComponentModel.DataAnnotations;
using TRex.Metadata;

namespace EventHubAPIApp.Models
{
    public class EventHubActionMessage
    {
        [Metadata("Event Hub Name", null)]
        [Required(AllowEmptyStrings = false)]
        public string hubName { get; set; }
        [Metadata("Message Properties", "Object of Key/Value Properties for message", VisibilityType.Advanced)]
        public string propertiesString { get; set; }
        [Metadata("Message", null)]
        public string message { get; set; }
        [Metadata("Partition Key", null, VisibilityType.Advanced)]
        public string partitionKey { get; set; }

    }
}
