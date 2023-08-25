using HiveMQtt.Client;
using HiveMQtt.Client.Options;
using HiveMQtt.MQTT5.ReasonCodes;
using HiveMQtt.MQTT5.Types;
using System.Text.Json;

namespace AdsStressTester
{
    public class MQTTClient
    {
        private HiveMQClient _client;

        public async Task<bool> Connect()
        {
            var options = new HiveMQClientOptions
            {
                Host = "localhost",
                Port = 1883,
                UseTLS = false,
            };

            _client = new HiveMQClient(options);
            Console.WriteLine($"Connecting to {options.Host} on port {options.Port} ...");

            // Connect
            HiveMQtt.Client.Results.ConnectResult connectResult;
            try
            {
                connectResult = await _client.ConnectAsync().ConfigureAwait(false);
                if (connectResult.ReasonCode == ConnAckReasonCode.Success)
                {
                    Console.WriteLine($"Connect successful: {connectResult}");
                }
                else
                {
                    // FIXME: Add ToString
                    Console.WriteLine($"Connect failed: {connectResult}");
                    Environment.Exit(-1);
                }
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Console.WriteLine($"Error connecting to the MQTT Broker with the following socket error: {e.Message}");
                Environment.Exit(-1);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error connecting to the MQTT Broker with the following message: {e.Message}");
                Environment.Exit(-1);
            }

            // Message Handler
            _client.OnMessageReceived += (sender, args) =>
            {
                string received_message = args.PublishMessage.PayloadAsString;
                Console.WriteLine(received_message);
            };

            // Subscribe
            await _client.SubscribeAsync("testTopic/#").ConfigureAwait(false);

            Console.WriteLine("Publishing message...");

            //initialise telemetry values
            double temperature = 25.1;
            double humidity = 77.5;
            var rand = new Random();

            double currentTemperature = temperature + rand.NextDouble();
            double currentHumidity = humidity + rand.NextDouble();
            var msg = JsonSerializer.Serialize(
                new
                {
                    temperature = currentTemperature,
                    humidity = currentHumidity,
                });
            //Publish MQTT messages
            var result = await _client.PublishAsync("testTopic", msg, QualityOfService.AtLeastOnceDelivery).ConfigureAwait(false);

            while (true)
            {

            }
        }
    }
}
