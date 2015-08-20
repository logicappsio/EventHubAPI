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
The app has one push trigger (Recieve Message) and one action (Send Message)

### Recieve Message Push Trigger ###
The push trigger has the following inputs

| Input | Description |
| ----- | ----- |
| Connection String | The connection string to access the event hub. |
| Event Hub Name | Name of the event hub (e.g. `myeventhub` ) |
| Consumer Group | Name of the consumer group the API App will subscribe to for messages |
| Partition List *(optional)* | A comma separated list of the partitions to subscribe to (e.g. `1,2,3,4,6,7,8`).  Leaving blank will subscribe to all available partitions for the Event Hub. |

The push trigger will return the message as `@triggers().outputs.body`.  It is returned as a JToken, so you can parse through the JSON like `@triggers().outputs.body.id`.

### Send Message Action ###
The push trigger has the following inputs

| Input | Description |
| ----- | ----- |
| Connection String | The connection string to access the event hub. |
| Event Hub Name | Name of the event hub (e.g. `myeventhub` ) |
| Message | The message to send |