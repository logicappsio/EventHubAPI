# Azure Event Hub API App
[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://azuredeploy.net/)

## Deploying ##
Click the "Deploy to Azure" button above.  You can create new resources or reference existing ones (resource group, gateway, service plan, etc.)  **Site Name and Gateway must be unique URL hostnames.**  The deployment script will deploy the following:
 * Resource Group (optional)
 * Service Plan (if you don't reference exisiting one)
 * Gateway (if you don't reference existing one)
 * API App (EventHubAPI)
 * API App Host (this is the site behind the api app that this github code deploys to)

## API Documentation ##
The app has one webhook trigger (Receive Message) and one action (Send Message)

### Receive Message webhook Trigger ###
The trigger has the following inputs

| Input | Description |
| ----- | ----- |
| Connection String | The connection string to access the event hub. |
| Event Hub Name | Name of the event hub (e.g. `myeventhub` ) |
| Consumer Group | Name of the consumer group the API App will subscribe to for messages (uses default consumer group if left blank) |
| Partition List *(optional)* | A comma separated list of the partitions to subscribe to (e.g. `1,2,3,4,6,7,8`).  Leaving blank will subscribe to all available partitions for the Event Hub. |

The trigger will return the message as `@{triggerBody()}`.  It is returned as a JToken, so you can parse through the JSON like `@{triggerBody()[0].eventtoken}`.

Sample webhook subscription:
"triggers": {
    "EhubTrigger": {
        "conditions": [],
        "inputs": {
            "subscribe": {
                "body": {
                    "callbackUrl": "@{listCallbackUrl()}",
                    "eventHubConnectionString": "Endpoint=sb://[namespace].servicebus.windows.net/;SharedAccessKeyName=[keyname];SharedAccessKey=[key]",
                    "eventHubName": "[hubname]"
                },
                "headers": {
                    "content-type": "application/json"
                },
                "method": "POST",
                "uri": "https://eventhubapiapp.azurewebsites.net:443/Subscribe"
            },
            "unsubscribe": {
                "body": {
                    "callbackUrl": "@{listCallbackUrl()}"
                },
                "headers": {
                    "content-type": "application/json"
                },
                "method": "POST",
                "uri": "https://eventhubapiapp.azurewebsites.net:443/Unsubscribe"
            }
        },
        "type": "HttpWebhook"
    }
}

### Send Message Action ###
The action has the following inputs.  It returns an [EventData object](https://msdn.microsoft.com/en-us/library/microsoft.servicebus.messaging.eventdata.aspx)

| Input | Description |
| ----- | ----- |
| Connection String | The connection string to access the event hub. |
| Event Hub Name | Name of the event hub (e.g. `myeventhub` ) |
| Message | The message to send |
| Message Properties | The custom properties for the event hub message.  Should be a JSON Object (e.g. `{"foo": "bar", "awesome": true }` |
| Partition Key | Partition Key for the event hub message |
