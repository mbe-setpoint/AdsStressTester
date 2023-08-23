using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog.Core;
using System.Text;
using System.Threading.Channels;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.Ads.Native;
using System.Buffers.Binary;
using System.Diagnostics.Metrics;
using TwinCAT.TypeSystem;

namespace AdsStressTester
{
    public class TwinCatService
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

        private async Task<bool> Connect()
        {
            await semaphore.WaitAsync();

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
                    }

                    ResultReadDeviceState result = await _client.ReadStateAsync(_cancel);

                    if (result.Succeeded && result.State.AdsState == AdsState.Run)
                    {
                        _logger.LogInformation("TWINCAT State: " + result.State.AdsState);
                        _logger.LogInformation("Connected to TWINCAT on: " + _netId + " port: " + _port);
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

        public async Task<object> ReadSymbolValue(string symbolNames, int extraSize = 0)
        {
            await semaphore.WaitAsync();
            var symbolList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(symbolNames);

            using (AdsClient client = new AdsClient())
            {
                client.Connect(_netId, _port);
                ResultReadDeviceState result = await client.ReadStateAsync(_cancel);
                if (result.Succeeded && result.State.AdsState == AdsState.Run)
                {
                    try
                    {
                        if (symbolList != null)
                        {
                            for (int i = 0; i < symbolList.Count; i++)
                            {
                                var symbolName = symbolList[i]["symbol"];
                                ResultValue<IAdsSymbol> symbol = await client.ReadSymbolAsync(symbolName, _cancel);
                                if (symbol != null && symbol.Value != null)
                                {
                                    var value = await client.ReadValueAsync(symbol.Value as ISymbol, _cancel);
                                    if (value != null && value.Value != null)
                                    {
                                        symbolList[i]["symbol"] = symbolName + ", " + "value:" + value.Value.ToString();
                                    }
                                }
                            }

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
        }

        public async Task<object> WriteSymbolValue(JsonDto variable)
        {
            try
            {
                await semaphore.WaitAsync();

                using (AdsClient client = new AdsClient())
                {
                    client.Connect(_netId, _port);
                    ResultReadDeviceState result = await client.ReadStateAsync(_cancel);
                    if (result.Succeeded && result.State.AdsState == AdsState.Run)
                    {
                        ResultValue<IAdsSymbol> symbol = await client.ReadSymbolAsync(variable.Symbol, _cancel);
                        if (symbol != null && symbol.Value != null)
                        {
                            var value = await client.WriteValueAsync(symbol.Value as ISymbol, variable.Value, _cancel);
                            return value;
                        }
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
            //var result = await obj.InvokeRpcMethodAsync("M_Add", new object[] { methodVars["x"], methodVars["y"] }, _cancel);
        }

        //public async Task<object> GetProp(string propName)
        //{
        //    Tuple<ResultHandle, ResultValue<IAdsSymbol>> result = await CheckSymbol(propName);
        //    _resultHandle = result.Item1;
        //    _symbol = result.Item2;

        //    Type dType = null;

        //    switch (_symbol.Value.DataType.Name.ToLower())
        //    {
        //        case "bool":
        //            dType = typeof(bool);
        //            break;
        //        case "real":
        //            dType = typeof(float);
        //            break;
        //        case "lreal":
        //            dType = typeof(double);
        //            break;
        //        case "dint":
        //            dType = typeof(Int32);
        //            break;
        //        case "int":
        //            dType = typeof(Int16);
        //            break;
        //    }

        //    return await _client.ReadValueAsync(propName, dType, _cancel);
        //}

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
    }
}
