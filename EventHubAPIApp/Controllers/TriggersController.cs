using Microsoft.Azure.AppService.ApiApps.Service;
using Microsoft.ServiceBus.Messaging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Web.Http;
using System.Threading.Tasks;
using System.Web.Hosting;
using System.Text;
using Newtonsoft.Json.Linq;
using TRex.Metadata;

namespace EventHubAPIApp.Controllers
{
    /// <summary>
    /// ApiController for the Event Hub.  Includes methods for the Push trigger to push on new Event Hub Messages, and the Send method to send Event Hub.  Also contains classes
    /// for InMemoryTriggerStore to store the callback URL for the push triggers, and the data model for EventHub Messages
    /// </summary>
    public class TriggersController : ApiController
    {
        /// <summary>
        /// Main Event Hub Push trigger
        /// </summary>
        /// <param name="triggerId">Passed in via Logic Apps to identify the Logic App needing the push notification</param>
        /// <param name="triggerInput">Item that includes trigger inputs like Event Hub parameters</param>
        /// <returns></returns>
        [Trigger(TriggerType.Push, typeof(EventHubMessage))]
        [Metadata("Receive Event Hub Message")]
        [HttpPut, Route("{triggerId}")]
        public HttpResponseMessage EventHubPushTrigger(string triggerId, [FromBody]TriggerInput<EventHubInput, EventHubMessage> triggerInput)
        {
            if (!InMemoryTriggerStore.Instance.GetStore().ContainsKey(triggerId))
            {
                HostingEnvironment.QueueBackgroundWorkItem(async ct => await InMemoryTriggerStore.Instance.RegisterTrigger(triggerId, triggerInput));
            }
            return this.Request.PushTriggerRegistered(triggerInput.GetCallback());
        }

        /// <summary>
        /// Method to send an event to an event hub given the connection string and message.  Returns an "OK" when sent with a copy of the message.
        /// </summary>
        /// <param name="connectionString"></param>
        /// <param name="hubName"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        [Metadata("Send Object to Event Hub")]
        [Route("SendJSON")]
        public async Task<HttpResponseMessage> EventHubSend([Metadata("Connection String")]string connectionString, [Metadata("Event Hub Name")]string hubName, [FromBody][Metadata("JSON Message")]JToken message)
        {
            var client = EventHubClient.CreateFromConnectionString(connectionString, hubName);
            client.Send(new EventData(Encoding.UTF8.GetBytes(message.ToString())));
            return Request.CreateResponse(System.Net.HttpStatusCode.OK, message);
        }

        [Metadata("Send String to Event Hub")]
        [Route("SendString")]
        public async Task<HttpResponseMessage> EventHubSend([Metadata("Connection String")]string connectionString, [Metadata("Event Hub Name")]string hubName, [FromBody][Metadata("String Message")]string message)
        {
            var client = EventHubClient.CreateFromConnectionString(connectionString, hubName);
            client.Send(new EventData(Encoding.UTF8.GetBytes(message)));
            return Request.CreateResponse(System.Net.HttpStatusCode.OK, message);
        }

        /// <summary>
        /// Class for the InMemoryTriggerStore.  How the Logic App works, it will call the PUSH trigger endpoint with the triggerID and CallBackURL - it is the job
        /// of the API App to then call the CallbackURL when an event happens.  I utilize this InMemoryTriggerStore to store the triggers I am watching, so that I don't
        /// continue to spin up new listeners whenever the Logic App asks me to register a trigger.
        /// 
        /// If the API App was ever to reset or lose connection, I would know I need to register new Event Hub Listeners as the InMemoryTriggerStore would be empty.
        /// </summary>
        public class InMemoryTriggerStore
        {
            private static InMemoryTriggerStore instance;
            private IDictionary<string, bool> _store;
            private InMemoryTriggerStore()
            {
                _store = new Dictionary<string, bool>();
            }

            public IDictionary<string, bool> GetStore()
            {
                return _store;
            }

            public static InMemoryTriggerStore Instance
            {
                get
                {
                    if (instance == null)
                    {
                        instance = new InMemoryTriggerStore();
                    }
                    return instance;
                }
            }

            /// <summary>
            /// The method that registers Event Hub listeners and assigns them to a recieve event.  When I receive an event from the event hub listener, I trigger the callbackURL
            /// </summary>
            /// <param name="triggerId"></param>
            /// <param name="triggerInput"></param>
            /// <returns></returns>
            public async Task RegisterTrigger(string triggerId, TriggerInput<EventHubInput, EventHubMessage> triggerInput)
            {
                var client = EventHubClient.CreateFromConnectionString(triggerInput.inputs.eventHubConnectionString, triggerInput.inputs.eventHubName);
                EventHubConsumerGroup group = client.GetConsumerGroup(triggerInput.inputs.consumerGroup);
                string[] partitions;
                
                //If they specified partitions, iterate over their list to only listen to the partitions they specified
                if (!String.IsNullOrEmpty(triggerInput.inputs.eventHubPartitionList))
                {
                    partitions = triggerInput.inputs.eventHubPartitionList.Split(',');
                }

                //If they left it blank, create a list to listen to all partitions
                else
                {
                    partitions = new string[client.GetRuntimeInformation().PartitionCount];
                    for(int x = 0; x < partitions.Length; x++)
                    {
                        partitions[x] = x.ToString();
                    }
                }

                //For ever partition I should listen to, create a thread with a listener on it
                foreach (var p in partitions)
                {
                    p.Trim();
                    var reciever = group.CreateReceiver(client.GetRuntimeInformation().PartitionIds[int.Parse(p)], DateTime.UtcNow);
                    EventHubListener listener = new EventHubListener(reciever);

                    //Register the event.  When I recieve a message, call the method to trigger the logic app
                    listener.MessageReceived += (sender, e) => sendTrigger(sender, e, Runtime.FromAppSettings(), triggerInput.GetCallback());
                    listener.StartListening();
                }

                //Register the triggerID in my store, so on subsequent checks from the logic app I don't spin up a new set of listeners
                _store[triggerId] = true;
            }

            /// <summary>
            /// Method to trigger the logic app.  Called when an event is recieved
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            /// <param name="runtime"></param>
            /// <param name="clientTriggerCallback"></param>
            private void sendTrigger(object sender, EventHubMessage e, Runtime runtime, ClientTriggerCallback<EventHubMessage> clientTriggerCallback)
            {
                clientTriggerCallback.InvokeAsync(runtime, e);
            }
        }

        /// <summary>
        /// Event Hub Listener class.  Keeps track of the receiver, and converts the message to a JOBject when recieved
        /// </summary>
        public class EventHubListener
        {
            private EventHubReceiver reciever;

            public EventHubListener(EventHubReceiver reciever)
            {
                this.reciever = reciever;
            }

            public event EventHandler<EventHubMessage> MessageReceived;

            public async Task StartListening()
            {
                while (true)
                {
                    try
                    {
                        var message = await reciever.ReceiveAsync(new TimeSpan(1, 0, 0));
                        if (message != null)
                        {
                            var info = message.GetBytes();
                            var msg = UnicodeEncoding.UTF8.GetString(info);
                            MessageReceived(this, new EventHubMessage(JObject.Parse(msg)));
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Data model for objects used in app.
        /// </summary>
        public class EventHubMessage
        {
            public JToken body;
            public EventHubMessage(JToken message)
            {
                this.body = message;
            }
        }

        public class EventHubInput
        {
            [Metadata("Connection String")]
            public string eventHubConnectionString { get; set; }
            [Metadata("Partition List (comma separated)", "If left blank, will listen to all partitions", Visibility =VisibilityType.Advanced)]
            public string eventHubPartitionList { get; set; }
            [Metadata("Event Hub Name")]
            public string eventHubName { get; set; }
            [Metadata("Consumer Group")]
            public string consumerGroup { get; set; }
        }
    }
}
