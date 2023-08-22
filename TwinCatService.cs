using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;

namespace AdsStressTester
{
    public class TwinCatService
    {
        private readonly ILogger<TwinCatService> _logger;        
        private readonly AdsClient _client;
        private readonly IConfiguration _config;
        private readonly CancellationToken _cancel;
        private string _netId;
        private int _port;
        private int _minBitSize = 1000; // 26429;
        private int _defaultBitValue = 30000;
        public bool isConnected = false;
        public bool busyRpc = false;

        public bool isBusy = false;

        private ResultHandle _resultHandle;
        private List<ResultHandle> _resultHandles; // caching of resultHandles 
        private ResultValue<IAdsSymbol> _symbol;
        private List<ResultValue<IAdsSymbol>> _symbols; // caching of symbols 

        private byte[] _writeData;
        private byte[] _readData;
        private MemoryStream _writeStream;
        private BinaryWriter _writer;

        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);


        public TwinCatService(IConfiguration config, ILogger<TwinCatService> logger)
        {
            _client = new AdsClient();
            _cancel = CancellationToken.None;
            _config = config;            
            _logger = logger;
            _netId = _config.GetValue<string>("TwinCatNetId");
            _port = _config.GetValue<Int16>("TwinCatPort");

            _symbols = new List<ResultValue<IAdsSymbol>>();
            _resultHandles = new List<ResultHandle>();
        }

        public async Task<bool> Connect()
        {
            await semaphore.WaitAsync();

            try
            {
                try
                {
                    _client.Connect(_netId, _port);
                    ResultReadDeviceState result = await _client.ReadStateAsync(_cancel);

                    if (result.Succeeded && result.State.AdsState == AdsState.Run)                    
                    {
                        _logger.LogInformation("TWINCAT State: " + result.State.AdsState);
                        _logger.LogInformation("Connected to TWINCAT on: " + _netId + " port: " + _port);

                        isConnected = true;
                        return result.Succeeded;                        
                    }
                    else
                    {
                        _logger.LogWarning($"Could not connect to ADS: {result.ErrorCode}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    _client.Disconnect();                    
                }
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public void Disconnect()
        {
            _logger.LogInformation("Terminated connection with TWINCAT");
            isConnected = false;
            _client.Dispose();
        }

        private async Task<Tuple<ResultHandle, ResultValue<IAdsSymbol>>> CheckSymbol(string symbolName)
        {
            Tuple<ResultHandle, ResultValue<IAdsSymbol>> tuple;

            ResultValue<IAdsSymbol> symbol = _symbols.Find(s => s.Value.InstancePath == symbolName);
            
            if (symbol != null)
            {
                // How do i get result handle by symbol  so i dont need to create a new resulthandle???
                //ResultHandle ResultHandle = _resultHandles.Find(resultHandle => res) 

                _resultHandle = await _client.CreateVariableHandleAsync(symbolName, _cancel);

                tuple = new Tuple<ResultHandle, ResultValue<IAdsSymbol>>(_resultHandle, symbol); 
            } else
            {
                _symbol = await _client.ReadSymbolAsync(symbolName, _cancel);
                _symbols.Add(_symbol);
                _resultHandle = await _client.CreateVariableHandleAsync(symbolName, _cancel);
                _resultHandles.Add(_resultHandle);

                tuple = new Tuple<ResultHandle, ResultValue<IAdsSymbol>>(_resultHandle, _symbol);
            }

            return tuple;
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

                var response = await _client.InvokeRpcMethodAsync(symbolPath, methodName, methodVars, _cancel);
                this.busyRpc = false;
                return response;
            }
            finally
            {
                semaphore.Release();
            }
            //var result = await obj.InvokeRpcMethodAsync("M_Add", new object[] { methodVars["x"], methodVars["y"] }, _cancel);
        }


        //public async Task<object> InvokeRpcMethod(string symbolPath, string methodName, string vars)
        //{
        //    dynamic methodVars = new object[] { };

        //    /*
        //     * variables should come as a stringify object from the client in the form of {variableName: value, variableName2: value2}
        //     * where variableNames are the exact input variables that the method expect in the PLC
        //     */

        //    if (vars != "undefined")
        //    {
        //        this.busyRpc = true;
        //        int index = 0;
        //        dynamic jsonVars = JsonConvert.DeserializeObject<Dictionary<string, int>>(vars);
        //        methodVars = new object[jsonVars.Count];

        //        foreach (string keyVar in jsonVars.Keys)
        //        {
        //            methodVars[index] = jsonVars[keyVar];
        //            index++;
        //        }
        //    }

        //    var response = await _client.InvokeRpcMethodAsync(symbolPath, methodName, methodVars, _cancel);
        //    this.busyRpc = false;
        //    return response;
        //    //var result = await obj.InvokeRpcMethodAsync("M_Add", new object[] { methodVars["x"], methodVars["y"] }, _cancel);
        //}

        public async Task<object> GetProp(string propName)
        {
            Tuple<ResultHandle, ResultValue<IAdsSymbol>> result = await CheckSymbol(propName);
            _resultHandle = result.Item1;
            _symbol = result.Item2;

            Type dType = null;

            switch (_symbol.Value.DataType.Name.ToLower())
            {
                case "bool":
                    dType = typeof(bool);
                    break;
                case "real":
                    dType = typeof(float);
                    break;
                case "lreal":
                    dType = typeof(double);
                    break;
                case "dint":
                    dType = typeof(Int32);
                    break;
                case "int":
                    dType = typeof(Int16);
                    break;
            }

            return await _client.ReadValueAsync(propName, dType, _cancel);
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

        public async Task<object> ReadSymbolValue(string symbolName, int extraSize = 0)
        {
            await semaphore.WaitAsync();

            try
            {
                  /*This method can be used both to read and write to a json
                    * The variable should be in the form of = "{\"symbol\":\"obj.aStruct\"}" without a new value for reading
                    * and in the form of symbolName = "{\"symbol\":\"obj.aStruct\", "value": xxx}" in order to rewrite a new value
                */
                this.isBusy = true;
                _writeData = new byte[symbolName.Length + 1];
                _writeStream = new MemoryStream(_writeData);
                _writer = new BinaryWriter(_writeStream);
                _writer.Write(Encoding.ASCII.GetBytes(symbolName));

                _readData = new byte[_defaultBitValue + extraSize];
                await _client.ReadWriteAsync(0xf070, 0, _readData, _writeData, _cancel);
                return Encoding.ASCII.GetString(_readData).TrimEnd('\u0000');
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

                _writeData = new byte[obj.Length + 1];
                _writeStream = new MemoryStream(_writeData);
                _writer = new BinaryWriter(_writeStream);
                _writer.Write(Encoding.ASCII.GetBytes(obj));

                _readData = new byte[_defaultBitValue];

                var result = await _client.ReadWriteAsync(0xf070, 0, _readData, _writeData, _cancel);
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Write failed: ", result.ErrorCode);
                }
                return Encoding.ASCII.GetString(_readData).TrimEnd('\u0000');

            }
            finally
            {
                semaphore.Release();
            }
}

        public async Task<object> WriteMultipleValues(JsonDto[] variable)
        {
            string obj = "[{\"symbol\":\"" + variable[0].Symbol.ToString() + "\"," + "\"value\":" + variable[0].Value.ToString() + "}," +
                "{\"symbol\":\"" + variable[1].Symbol.ToString() + "\"," + "\"value\":" + variable[1].Value.ToString() + "}" +
                "]";

            _writeData = new byte[obj.Length + 1];
            _writeStream = new MemoryStream(_writeData);
            _writer = new BinaryWriter(_writeStream);
            _writer.Write(Encoding.ASCII.GetBytes(obj));

            _readData = new byte[_defaultBitValue];

            await _client.ReadWriteAsync(0xf070, 0, _readData, _writeData, _cancel);
            return Encoding.ASCII.GetString(_readData).TrimEnd('\u0000');
        }
    }
}
