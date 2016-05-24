using TRex.Metadata;

namespace EventHubAPIApp.Models
{
    public class EventHubInput
    {
        [Metadata("Connection String")]
        public string eventHubConnectionString { get; set; }
        [Metadata("Partition List (comma separated)", "If left blank, will listen to all partitions", Visibility = VisibilityType.Advanced)]
        public string eventHubPartitionList { get; set; }
        [Metadata("Event Hub Name")]
        public string eventHubName { get; set; }
        [Metadata("Consumer Group")]
        public string consumerGroup { get; set; }
        public string callbackUrl { get; set; }
    }
}
