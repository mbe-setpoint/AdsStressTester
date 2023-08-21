using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace AdsStressTester
{
    public enum SymbolCheckReturnCodes
    {
        OK,
        UNKNOWN_SYMBOL,
        EMPTY_RESULT,
        ADS_CONNECT_ERROR,
        FATAL
    }

    public class TwinCatSymbolMapper
    {
        private readonly ILogger<TwinCatSymbolMapper> _logger;        
        private readonly TwinCatService _twinCatService;
        private Dictionary<string, Dictionary<string, List<string>>> _symbolsDict;
        private readonly string _symbolFilePath;

        public TwinCatSymbolMapper(ILogger<TwinCatSymbolMapper> logger, TwinCatService twinCatService, string symbolFilePath = "adsSymbols.json")
        {
            _logger = logger;            
            _twinCatService = twinCatService;            
            _symbolFilePath = symbolFilePath;
            getSymbolsFromFile();
        }

        public Dictionary<string, Dictionary<string, List<string>>> SymbolsDict { 
            get { return _symbolsDict; }
        }

        public string GetSymbolsStringForHub(string hubName, int index = 0)
        {
            if (_symbolsDict.ContainsKey(hubName))
            {
                var symbolString = ComposeSymbolString(_symbolsDict[hubName]["Symbols"]);
                return symbolString.Replace("@index@", $"{index}"); // Replaces the string @index@ with the provided index. If @index@ is not found the string is returned unchanged
            }
            else
            {
                _logger.LogWarning($"ADS Symbol file does not contain a section for {hubName}");
                return string.Empty;
            }
        }

        public List<string> GetRpcMethodsInHubs()
        {
            List<string> methods = new List<string>();
            foreach (var hubName in _symbolsDict.Keys)
            {
                var rpcMethodsInHub = _symbolsDict[hubName]["RPC-methods"];
                methods.AddRange(rpcMethodsInHub);
            }             

            return methods;
            
            //else
            //{
            //    _logger.LogWarning($"ADS Symbol file does not contain a section for {hubName}");                
            //}
        }

        private void getSymbolsFromFile()
        {
            _logger.LogInformation($"Getting ADS-symbols from file: {_symbolFilePath}");
            var json = File.ReadAllText(_symbolFilePath);
            _symbolsDict = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<string>>>>(json);
        }

        private string ComposeSymbolString(List<string> symbolList)
        {
            StringBuilder symbolString = new StringBuilder();
            symbolString.Append("[");
            foreach (var symbol in symbolList)
            {
                symbolString.Append("{\"symbol\":\"" + symbol + "\"},");
            }
            symbolString.Remove(symbolString.Length - 1, 1);
            symbolString.Append("]");
            var finalString = symbolString.ToString();            
            return finalString;
        }

        public string PadSingleSymbolName(string symbolName)
        {
            StringBuilder symbolString = new StringBuilder();
            symbolString.Append("[");
            symbolString.Append("{\"symbol\":\"" + symbolName + "\"}");
            symbolString.Append("]");
            var finalString = symbolString.ToString();
            return finalString;
        }

        public async Task<Tuple<SymbolCheckReturnCodes, string>> CheckSymbolString(string symbolString)
        {
            var connected = await _twinCatService.Connect();
            if (connected)
            {
                dynamic data;
                data = await _twinCatService.ReadSymbolValue(symbolString);
                if (data != null)
                {
                    if (data != string.Empty)
                    {
                        return ExtractReturnValue(data);
                    }
                    else
                    {
                        return Tuple.Create(SymbolCheckReturnCodes.EMPTY_RESULT, "Empty result string");
                    }                    
                }
                
                return Tuple.Create(SymbolCheckReturnCodes.FATAL, "Catastrooof");
            }
            else
            {
                _logger.LogError("Could not connect to ADS");
                return Tuple.Create(SymbolCheckReturnCodes.ADS_CONNECT_ERROR, "Error connecting to ADS");
            }
        }

        private Tuple<SymbolCheckReturnCodes, string> ExtractReturnValue(string symbolResponse)
        {
            var returnDict = JsonConvert.DeserializeObject<List<Dictionary<string, dynamic>>>(symbolResponse)[0];
            if (returnDict.ContainsKey("error")) //Symbol not found: [{"symbol":"gvlAnchorPO.winch.EXnable","error":{"gvlAnchorPO.winch.EXnable":"unknown"}}]
            {                
                return Tuple.Create(SymbolCheckReturnCodes.UNKNOWN_SYMBOL, returnDict["error"].ToString());
            }
            else if (returnDict.ContainsKey("value")) //Symbol Ok: [{"symbol":"gvlAnchorPO.winch.Enable","value":true}]
            {
                return Tuple.Create(SymbolCheckReturnCodes.OK, returnDict["value"].ToString());
            }
            
            return null;
        }        
    }
}
