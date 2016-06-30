using EventHubAPIApp.Models;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using System.Web.Http;
using TRex.Metadata;

namespace EventHubAPIApp.Controllers
{
    /// <summary>
    /// ApiController for the Event Hub.  Includes methods for the Push trigger to push on new Event Hub Messages, and the Send method to send Event Hub.  Also contains classes
    /// for InMemoryTriggerStore to store the callback URL for the push triggers, and the data model for EventHub Messages
    /// </summary>
    public class TriggersController : ApiController
    {
        #region Trigger - Receive message
        /// <summary>
        /// Receive a subscription to a webhook.  
        /// </summary>
        /// <returns></returns>
        [HttpPost, Route("Subscribe")]
        public HttpResponseMessage Subscribe([FromBody] EventHubInput input)
        {
            if (!String.IsNullOrEmpty(input.callbackUrl))
            {
                if (!InMemoryTriggerStore.Instance.GetStore().ContainsKey(input.callbackUrl))
                {
                    HostingEnvironment.QueueBackgroundWorkItem(async ct => await InMemoryTriggerStore.Instance.RegisterTrigger(input));
                }
            }
            return Request.CreateResponse();
        }

        /// <summary>
        /// Unsubscribe
        /// </summary>
        /// <param name="callbackUrl"></param>
        /// <returns></returns>
        [HttpPost, Route("Unsubscribe")]
        public HttpResponseMessage Unsubscribe([FromBody] UnsubscribeMessage message)
        {
            if (!String.IsNullOrEmpty(message.callbackUrl))
            {
                if (InMemoryTriggerStore.Instance.GetStore().ContainsKey(message.callbackUrl))
                {
                    InMemoryTriggerStore.Instance.UnregisterTrigger(message.callbackUrl);
                }
            }
            return Request.CreateResponse();
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
            private IDictionary<string, List<CancellationTokenSource>> _store;
            private InMemoryTriggerStore()
            {
                _store = new Dictionary<string, List<CancellationTokenSource>>();
            }

            public IDictionary<string, List<CancellationTokenSource>> GetStore()
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
            /// The method that registers Event Hub listeners and assigns them to a Receive event.  When I receive an event from the event hub listener, I trigger the callbackURL
            /// </summary>
            /// <param name="triggerId"></param>
            /// <param name="triggerInput"></param>
            public async Task RegisterTrigger(EventHubInput input)
            {
                var client = EventHubClient.CreateFromConnectionString(input.eventHubConnectionString, input.eventHubName);

                EventHubConsumerGroup group = String.IsNullOrEmpty(input.consumerGroup) ?
                    client.GetDefaultConsumerGroup() : client.GetConsumerGroup(input.consumerGroup);

                string[] partitions;

                //If they specified partitions, iterate over their list to only listen to the partitions they specified
                if (!String.IsNullOrEmpty(input.eventHubPartitionList))
                {
                    partitions = input.eventHubPartitionList.Split(',');
                }

                //If they left it blank, create a list to listen to all partitions
                else
                {
                    partitions = new string[client.GetRuntimeInformation().PartitionCount];
                    for (int x = 0; x < partitions.Length; x++)
                    {
                        partitions[x] = x.ToString();
                    }
                }

                List<CancellationTokenSource> tokenSources = new List<CancellationTokenSource>();

                //For ever partition I should listen to, create a thread with a listener on it
                foreach (var p in partitions)
                {
                    p.Trim();
                    var Receiver = group.CreateReceiver(client.GetRuntimeInformation().PartitionIds[int.Parse(p)], DateTime.UtcNow);
                    EventHubListener listener = new EventHubListener(Receiver);

                    //Register the event.  When I Receive a message, call the method to trigger the logic app
                    listener.MessageReceived += (sender, e) => TriggerLogicApps(input.callbackUrl, e);

                    var ts = new CancellationTokenSource();
                    CancellationToken ct = ts.Token;
                    listener.StartListening(ct);

                    tokenSources.Add(ts);
                }

                //Register the triggerID in my store, so on subsequent checks from the logic app I don't spin up a new set of listeners
                _store[input.callbackUrl] = tokenSources;
            }

            public async void TriggerLogicApps(string callbackUrl, JToken message)
            {
                using (HttpClient client = new HttpClient())
                {
                    await client.PostAsync(callbackUrl, message, new JsonMediaTypeFormatter(), "application/json");
                }
            }

            public void UnregisterTrigger(string callbackUrl)
            {
                List<CancellationTokenSource> tokenSources = _store[callbackUrl];

                foreach (var ts in tokenSources)
                {
                    ts.Cancel();
                }

                _store.Remove(callbackUrl);
            }
        }

        /// <summary>
        /// Event Hub Listener class.  Keeps track of the receiver, and converts the message to a JOBject when Received
        /// </summary>
        public class EventHubListener
        {
            private EventHubReceiver receiver;

            public EventHubListener(EventHubReceiver receiver)
            {
                this.receiver = receiver;
            }

            public event EventHandler<JToken> MessageReceived;

            public async Task StartListening(CancellationToken token)
            {
                while (true)
                {
                    string messageString = String.Empty;

                    try
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        var eventData = await receiver.ReceiveAsync(new TimeSpan(1, 0, 0));
                        if (eventData != null)
                        {
                            var info = eventData.GetBytes();
                            messageString = UnicodeEncoding.UTF8.GetString(info);
                            MessageReceived(this, JToken.Parse(messageString));
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError(String.Format("Error: {0} while processing message: {1}", ex.Message, messageString));
                    }
                }
            }

        }

        #endregion

        #region Action - Send message
        [Metadata("Send Message to Event Hub")]
        [Swashbuckle.Swagger.Annotations.SwaggerResponse(HttpStatusCode.BadRequest, "An exception occured", typeof(Exception))]
        [Swashbuckle.Swagger.Annotations.SwaggerResponse(System.Net.HttpStatusCode.Created)]
        [Route("SendString")]
        public HttpResponseMessage EventHubSend([Metadata("Connection String")]string connectionString, [FromBody]EventHubActionMessage input)
        {
            try
            {

                var client = EventHubClient.CreateFromConnectionString(connectionString, input.hubName);
                string message = input.message;
                var bodyBytes = Encoding.UTF8.GetBytes(message);
                var eventMessage = new EventData(bodyBytes);
                eventMessage.PartitionKey = string.IsNullOrEmpty(input.partitionKey) ? eventMessage.PartitionKey : input.partitionKey;

                if (!string.IsNullOrEmpty(input.propertiesString))
                {
                    var properties = JObject.Parse(input.propertiesString);
                    foreach (var property in properties)
                    {

                        switch (property.Value.Type.ToString())
                        {
                            case "Boolean":
                                eventMessage.Properties[property.Key] = (bool)property.Value;
                                break;
                            case "String":
                                eventMessage.Properties[property.Key] = (string)property.Value;
                                break;
                            case "DateTime":
                                eventMessage.Properties[property.Key] = (DateTime)property.Value;
                                break;
                            case "Int64":
                                eventMessage.Properties[property.Key] = (int)property.Value;
                                break;
                            case "Decimal":
                                eventMessage.Properties[property.Key] = (decimal)property.Value;
                                break;
                            case "Double":
                                eventMessage.Properties[property.Key] = (double)property.Value;
                                break;
                            default:
                                eventMessage.Properties[property.Key] = (string)property.Value;
                                break;
                        }
                    }
                }
                client.Send(eventMessage);
                return Request.CreateResponse(System.Net.HttpStatusCode.Created);
            }
            catch (NullReferenceException ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, @"The input Received by the API was null.  This sometimes happens if the message in the Logic App is malformed.  Check the message to make sure there are no escape characters like '\'.", ex);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }

        #endregion
    }


    ///DEMOTED
    /// <summary>
    /// Method to send an event to an event hub given the connection string and message.  Returns an "OK" when sent with a copy of the message.
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="hubName"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    //[Metadata("Send Object to Event Hub")]
    //[Route("SendJSON")]
    //public async Task<HttpResponseMessage> EventHubSend([Metadata("Connection String")]string connectionString, [Metadata("Event Hub Name")]string hubName, [FromBody][Metadata("JSON Message")]JObject message)
    //{
    //    var client = EventHubClient.CreateFromConnectionString(connectionString, hubName);
    //    client.Send(new EventData(Encoding.UTF8.GetBytes(message.ToString())));
    //    return Request.CreateResponse(System.Net.HttpStatusCode.OK, message);
    //}
}
