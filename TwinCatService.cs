using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;

namespace AdsStressTester
{
    public class TwinCatService : IDisposable
    {
        private readonly ILogger<TwinCatService> _logger;
        private AdsClient _client;
        private readonly IConfiguration _config;
        private readonly CancellationToken _cancel;
        private string _netId;
        private int _port;
        private int _minBitSize = 1000; // 26429;
        private int _defaultBitValue = 30000;
        public bool busyRpc = false;
        public bool isBusy = false;
        private List<ResultHandle> _resultHandlesCache; // caching of resultHandles         
        private List<ResultValue<IAdsSymbol>> _symbolsCache; // caching of symbols
        private ResultSymbols _plcSymbols;
        private bool _plcReady = false;


        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public TwinCatService(IConfiguration config, ILogger<TwinCatService> logger)
        {
            _client = new AdsClient();
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            _cancel = cancelSource.Token;
            _config = config;
            _logger = logger;
            _netId = _config.GetValue<string>("TwinCatNetId");
            _port = _config.GetValue<Int16>("TwinCatPort");
            _symbolsCache = new List<ResultValue<IAdsSymbol>>();
            _resultHandlesCache = new List<ResultHandle>();
        }

        public bool IsConnected
        {
            get
            {
                if (_plcReady)
                {
                    return true;
                }
                return Connect();
            }
        }

        private bool Connect()
        {
            semaphore.Wait();

            try
            {
                try
                {
                    if (_client == null || _client.IsDisposed)
                    {
                        _client = new AdsClient();
                    }

                    if (_client.IsConnected == false)
                    {
                        _client.Connect(_netId, _port);

                        StateInfo result = _client.ReadState();
                        if (result.AdsState == AdsState.Run)
                        {
                            _logger.LogInformation("TWINCAT State: " + result.AdsState);
                            _logger.LogInformation("Connected to TWINCAT on: " + _netId + " port: " + _port);
                            _plcReady = true;
                            return _plcReady;
                        }
                        else
                        {
                            _logger.LogWarning($"Could not connect to ADS: {result.AdsState}");
                        }
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

        public async Task<object> ReadSymbolValue(string symbolNames, int extraSize = 0, bool useJsonDataInterface = false)
        {           
            if (IsConnected)
            {               
                try
                {
                    await semaphore.WaitAsync();
                    var symbolList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(symbolNames);
                    if (useJsonDataInterface)
                    {
                        // Access via JSON Data Interface https://infosys.beckhoff.com/english.php?content=../content/1033/tf6020_tc3_json_data_interface/10821785483.html&id=                    
                        byte[] writeData = new byte[symbolNames.Length + 1];
                        MemoryStream writeStream = new MemoryStream(writeData);
                        BinaryWriter writer = new BinaryWriter(writeStream);
                        writer.Write(Encoding.ASCII.GetBytes(symbolNames));

                        byte[] readData = new byte[_defaultBitValue + extraSize];

                        _client.ReadWrite(0xf070, 0, readData, writeData);

                        var responseString = Encoding.ASCII.GetString(readData);
                        return responseString;
                    }
                    else
                    {
                        if (symbolList != null)
                        {
                            for (int i = 0; i < symbolList.Count; i++)
                            {
                                var symbolName = symbolList[i]["symbol"];
                                ResultValue<IAdsSymbol>? symbol = _symbolsCache.Find(s => s.Value.InstancePath == symbolName);
                                if (symbol == null)
                                {
                                    ResultValue<IAdsSymbol> newSymbol = await _client.ReadSymbolAsync(symbolName, _cancel);
                                    if (newSymbol.Succeeded)
                                    {
                                        //Put symbol in cache
                                        _symbolsCache.Add(newSymbol);
                                        symbol = newSymbol;
                                    }
                                }

                                // Getvalue from PLC
                                var value = await _client.ReadValueAsync(symbol.Value as ISymbol, _cancel);
                                if (value.Succeeded)
                                {
                                    // Append the value to the symbol list
                                    symbolList[i]["symbol"] = symbolName + ", " + "value:" + value.Value.ToString();
                                }
                            }
                        }

                        // Convert all symbols and values to Json and return
                        string symbolsWithValues = JsonConvert.SerializeObject(symbolList, Formatting.None);
                        return symbolsWithValues;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            }
            return null;
        }


        public async Task<object> WriteSymbolValue(JsonDto variable)
        {
            try
            {
                await semaphore.WaitAsync();


                if (IsConnected)
                {                    
                    // Try to get symbol from cache
                    ResultValue<IAdsSymbol>? symbol = _symbolsCache.Find(s => s.Value.InstancePath == variable.Symbol);
                    if (symbol == null)
                    {
                        ResultValue<IAdsSymbol> newSymbol = await _client.ReadSymbolAsync(variable.Symbol, _cancel);
                        if (newSymbol != null)
                        {
                            //Put symbol in cache
                            _symbolsCache.Add(newSymbol);
                            symbol = newSymbol;
                        }
                    }

                    if (symbol.Value != null)
                    {
                        var value = await _client.WriteValueAsync(symbol.Value as ISymbol, variable.Value, _cancel);
                        return value;
                    }
                }
            }
            catch (Exception e)
            {
                _logger?.LogError(e.Message);
                return null;
            }
            finally
            {
                semaphore.Release();
            }

            return null;
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
            _client.Dispose();
        }
    }
}
