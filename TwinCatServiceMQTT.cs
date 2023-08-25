using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using HiveMQtt.Client;
using HiveMQtt.Client.Options;
using HiveMQtt.MQTT5.ReasonCodes;
using HiveMQtt.MQTT5.Types;
using HiveMQtt.Client.Events;
using HiveMQtt.Client.Results;
using System.Threading.Tasks;

namespace AdsStressTester
{
    public class TwinCatServiceMQTT : IDisposable
    {
        private readonly ILogger<TwinCatServiceMQTT> _logger;
        private HiveMQClient _client;
        private readonly IConfiguration _config;                
        private int _port;
        private int _minBitSize = 1000; // 26429;
        private int _defaultBitValue = 30000;
        public bool busyRpc = false;
        public bool isBusy = false;
        private readonly string _mqttSubscriptionTopicPrefix = "p7s/res/851/";
        private readonly string _mqttPublishTopicPrefix = "p7s/req/851/";
        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<bool> _taskCompletionSource;
        private string _mqttResult;

        public TwinCatServiceMQTT(IConfiguration config, ILogger<TwinCatServiceMQTT> logger)
        {
            _config = config;
            _logger = logger;
            _port = _config.GetValue<Int16>("TwinCatPort");
            var options = new HiveMQClientOptions
            {
                Host = "localhost",
                Port = 1883,
                UseTLS = false,
            };
            _client = new HiveMQClient(options);            
            _client.OnMessageReceived += OnMQTTMessageReceived;
        }

        private void OnMQTTMessageReceived(object? sender, OnMessageReceivedEventArgs e)
        {
            _mqttResult = e.PublishMessage.PayloadAsString;
            _logger.LogDebug(_mqttResult);
            _taskCompletionSource.SetResult(true);
        }

        public bool IsConnected
        {
            get
            {
                if (_client.IsConnected())
                {
                    return true;
                }
                return Connect().Result;
            }
        }

        private async Task<bool> Connect()
        {
            semaphore.Wait();

            ConnectResult connectResult;
            try
            {
                connectResult = await _client.ConnectAsync().ConfigureAwait(false);
                if (connectResult.ReasonCode == ConnAckReasonCode.Success)
                {
                    _logger.LogInformation($"Connect successful: {connectResult}");                    
                    
                    return true;
                }
                else
                {
                    _logger.LogError($"Connect failed: {connectResult}");
                    return false;
                }
            }
            catch (System.Net.Sockets.SocketException e)
            {
                _logger.LogError($"Error connecting to the MQTT Broker with the following socket error: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error connecting to the MQTT Broker with the following message: {e.Message}");
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<object> ReadSymbolValue(string symbolName, int extraSize = 0)
        {            
            string result = string.Empty;            
            try
            {
                if (IsConnected)
                {
                    await semaphore.WaitAsync();

                    //Setup unique subscription for this call
                    var rand = new Random();
                    string id = rand.Next(10000).ToString();
                    string subscription_topic = _mqttSubscriptionTopicPrefix + id;
                    _logger.LogDebug($"Subscribing to topic: {subscription_topic}");
                    SubscribeResult subscribeResult = await _client.SubscribeAsync(subscription_topic).ConfigureAwait(false);
                    _logger.LogDebug(subscribeResult.ToString());

                    //Publish MQTT messages
                    _taskCompletionSource = new TaskCompletionSource<bool>();
                    string publishTopic = _mqttPublishTopicPrefix + id;
                    PublishResult publishResult = await _client.PublishAsync(publishTopic, symbolName, QualityOfService.AtLeastOnceDelivery).ConfigureAwait(false);

                    // Wait until the result is back
                    await _taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                    // Release resources
                    await _client.UnsubscribeAsync(_mqttSubscriptionTopicPrefix + id).ConfigureAwait(false);
                    _taskCompletionSource = null;
                   
                }

                return _mqttResult.TrimEnd('\u0000');
            }
            finally
            {                
                semaphore.Release();
            }
        }

        public async Task<object> WriteSymbolValue(JsonDto variable)
        {
            await semaphore.WaitAsync();

            try
            {
                string obj = "{\"symbol\":\"" + variable.Symbol.ToString() + "\"," + "\"value\":" + variable.Value.ToString() + "}";

                //_writeData = new byte[obj.Length + 1];
                //_writeStream = new MemoryStream(_writeData);
                //_writer = new BinaryWriter(_writeStream);
                //_writer.Write(Encoding.ASCII.GetBytes(obj));

                //_readData = new byte[_defaultBitValue];

                //await _client.ReadWriteAsync(0xf070, 0, _readData, _writeData, _cancel);
                //return Encoding.ASCII.GetString(_readData).TrimEnd('\u0000');
                return "";

            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<object> InvokeRpcMethod(string symbolPath, string methodName, string vars)
        {
            await semaphore.WaitAsync();

            try
            {
                dynamic methodVars = new object[] { };

                /*
                 * variables should come as a stringify object from the client in the form of {variableName: value, variableName2: value2}
                 * where variableNames are the exact input variables that the method expect in the PLC
                 */

                if (vars != "undefined")
                {
                    this.busyRpc = true;
                    int index = 0;
                    dynamic jsonVars = JsonConvert.DeserializeObject<Dictionary<string, int>>(vars);
                    methodVars = new object[jsonVars.Count];

                    foreach (string keyVar in jsonVars.Keys)
                    {
                        methodVars[index] = jsonVars[keyVar];
                        index++;
                    }
                }

                //var response = await _client.InvokeRpcMethodAsync(symbolPath, methodName, methodVars, _cancel);
                this.busyRpc = false;
                return "";
            }
            finally
            {
                semaphore.Release();
            }            
        }

        public async Task<object> GetWinchConfigs()
        {
            return await ReadSymbolValue("{\"symbol\":\"ConfigPersistent.Config.eWinch\"}");
        }

        public async Task<object> GetUserWinchConfigs()
        {
            return await ReadSymbolValue("{\"symbol\":\"configpersistent.Config.userWinch\"}");
        }

        public async Task<object> GetSpoolConfigs()
        {
            return await ReadSymbolValue("{\"symbol\":\"configpersistent.Config.spool\"}");
        }

        public async Task<object> GetControlUnitConfigs()
        {
            return await ReadSymbolValue("{\"symbol\":\"configpersistent.Config.ctrlBaseUnit\"}");
        }

        public async Task<object> GetSystemData()
        {
            //return await ReadSymbolValue("[{\"symbol\":\"configpersistent.Config\"}, {\"symbol\":\"opValuesPersistent.opvalues\"}]", 300000);
            return await ReadSymbolValue("{\"symbol\":\"configpersistent.Config\"}", 100000);
        }
        public async Task<object> GetOperationalValues()
        {
            return await ReadSymbolValue("{\"symbol\":\"opValuesPersistent.opvalues\"}", 100000);
        }

        public async Task<object> GetDriveConfig()
        {
            return await ReadSymbolValue("{\"symbol\":\"configpersistent.Config.Drive\"}");
        }
        public async Task<object> GetDriveInverters(int index)
        {
            return await ReadSymbolValue("{\"symbol\":\"ObjDriveTrain.driveInfos[" + index.ToString() + "].toHMI\"}");
        }
        public async Task<object> GetNumberOfInverters(int index)
        {
            return await ReadSymbolValue("{\"symbol\":\"ObjDriveTrain.driveInfos[" + index.ToString() + "].N\"}");
        }

        public async Task<object> GetSystemUnits()
        {
            return await ReadSymbolValue("{\"symbol\":\"obj.P6.Config.unitsInUse\"}");
        }

        public void Dispose()
        {
            //_client.UnsubscribeAsync(_mqttSubscriptionTopic).ConfigureAwait(false);
            _client.DisconnectAsync();
            _client.Dispose();
        }
    }
}
