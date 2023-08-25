using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AdsStressTester
{
    internal class LoadGenerator
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _config;
        private readonly TwinCatServiceADS _twinCatServiceADS;
        private readonly TwinCatServiceMQTT _twinCatServiceMQTT;
        private readonly TwinCatSymbolMapper _symbolMapper;

        public LoadGenerator(ILogger logger, TwinCatServiceADS twinCatServiceADS, TwinCatServiceMQTT twinCatServiceMQTT, TwinCatSymbolMapper symbolMapper, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            _twinCatServiceADS = twinCatServiceADS;
            _twinCatServiceMQTT = twinCatServiceMQTT;
            _symbolMapper = symbolMapper;
        }

        public async Task<bool> GetData(int numberOfRuns, int millisecondsDelay, bool useMQTT = true)
        {
            var hubs = _config.GetSection("hubs").Get<List<string>>();
            var variables = _config.GetSection("adsWriteVariables").Get<List<Dictionary<string, dynamic>>>();
            var jsonDataInterface = new JsonDataInterface();

            using (_twinCatServiceADS)
            {
                if (hubs != null && variables != null)
                {
                    dynamic _data;
                    int i = 0;

                    while (numberOfRuns < 0 || i < numberOfRuns)
                    {
                        foreach (string hub in hubs)
                        {
                            var symbolString = _symbolMapper.GetSymbolsStringForHub(hub);

                            if (useMQTT)
                            {
                                _data = await _twinCatServiceMQTT.ReadSymbolValue(symbolString);                                
                            }
                            else
                            {
                                _data = await _twinCatServiceADS.ReadSymbolValue(symbolString);
                            }

                            if (!string.IsNullOrEmpty(_data))
                            {
                                List<object> result = JsonConvert.DeserializeObject<List<object>>(_data);
                                _logger.LogInformation($"Run # {i + 1} - Got symbols from {hub} - {result.Count} symbols");
                                foreach (var item in result)
                                {
                                    if (item.ToString().Contains("unknown"))
                                    {
                                        _logger.LogError(item.ToString());
                                    }
                                    _logger.LogDebug($"{item}");
                                }
                            }
                            else
                            {
                                _logger.LogError("Data is null");
                            }

                            await Task.Delay(millisecondsDelay);
                        }

                        if (i % 4 == 0)
                        {
                            // WriteValue(variables, useMQTT);
                        }

                        if (i % 7 == 0)
                        {
                            // CallRpcMethod(useMQTT);
                        }

                        i++;
                    }
                    return true;
                }
                else
                {
                    _logger.LogWarning("Could not connect to ADS or missing hubs and/or variables in adsSymbols file");
                    return false;
                }
            }
        }

        private async void CallRpcMethod(bool useMQTT)
        {            
            var methods = _symbolMapper.GetRpcMethodsInHubs();
            if (methods.Count > 0)
            {
                Random rnd = new Random();
                var index = rnd.Next(methods.Count - 1);
                var symbolMethodVars = methods[index].Split(';');
                var symbol = symbolMethodVars[0];
                var method = symbolMethodVars[1];
                var vars = symbolMethodVars[2];

                _logger.LogInformation($"Calling RPC Method {symbol} - {method}");
                TwinCAT.Ads.ResultRpcMethod result;
                if (useMQTT)
                {
                    result = (TwinCAT.Ads.ResultRpcMethod)await _twinCatServiceMQTT.InvokeRpcMethod(symbol, method, vars);
                }
                else
                {
                    result = (TwinCAT.Ads.ResultRpcMethod)await _twinCatServiceADS.InvokeRpcMethod(symbol, method, vars);
                }
                
                if (result.Failed)
                {
                    _logger.LogError($"Call to {symbol} - {method} failed. Result: {result.ErrorCode}");
                }
            }
        }

        private async void WriteValue(List<Dictionary<string, dynamic>> variables, bool useMQTT)
        {
            Random rnd = new Random();
            var variableIndex = rnd.Next(variables.Count - 1);
            string symbol = variables[variableIndex].Keys.First();
            var value = variables[variableIndex].Values.First();
            JsonDto variable = new JsonDto { Symbol = symbol, Value = value };
            _logger.LogDebug($"Writing symbol: {symbol} with value: {value}");
            object result;
            if (useMQTT)
            {
                result = await _twinCatServiceMQTT.WriteSymbolValue(variable);
            }
            else
            {
                result = await _twinCatServiceADS.WriteSymbolValue(variable);
            }
             
            if (result != null && result.ToString().Contains("error"))
            {
                _logger.LogError($"Failed to write to: {symbol}");
            }
        }
    }
}
