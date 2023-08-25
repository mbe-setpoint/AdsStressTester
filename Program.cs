using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Diagnostics;
using TwinCAT;

namespace AdsStressTester
{
    internal class Options
    {
        [Option('n', "number_of_runs", Required = false, HelpText = "Number of times to get symbols from PLC. Set to -1 to run until stopped")]
        public int? NumberOfRuns { get; set; }

        [Option('d', "delay", Required = false, HelpText = "Delay in miliseconds between fetches from PLC")]
        public int? Delay { get; set; }
    }

    internal class Program
    {
        static async Task<int> Main(string[] args)
        {            
            ILogger<TwinCatServiceADS> twinCatServiceADSLogger;
            ILogger<TwinCatServiceMQTT> twinCatServiceMQTTLogger;
            ILogger<TwinCatSymbolMapper> twinCatSymbolMapperLogger;
            ILogger<Program> mainLogger;

            int numberOfRuns = 10;
            int milisecondsDelay = 500;

            // Setup loggers
            IConfiguration config = new ConfigurationBuilder()
               .AddJsonFile("adsStressTester.json", optional: false, reloadOnChange: true).Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();

            using (ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddSerilog()))
            {
                twinCatServiceADSLogger = factory.CreateLogger<TwinCatServiceADS>();
                twinCatServiceMQTTLogger = factory.CreateLogger<TwinCatServiceMQTT>();
                mainLogger = factory.CreateLogger<Program>();
                twinCatSymbolMapperLogger = factory.CreateLogger<TwinCatSymbolMapper>();
            }

            // Setup services
            TwinCatServiceADS twinCatServiceADS = new TwinCatServiceADS(config, twinCatServiceADSLogger);
            TwinCatServiceMQTT twinCatServiceMQTT = new TwinCatServiceMQTT(config, twinCatServiceMQTTLogger);
            TwinCatSymbolMapper symbolMapper = new TwinCatSymbolMapper(twinCatSymbolMapperLogger, twinCatServiceADS, symbolFilePath: "adsSymbols_bsdtest.json");

            Parser.Default.ParseArguments<Options>(args)
                  .WithParsed<Options>(o =>
                  {
                      if (o.NumberOfRuns != null)
                      {
                          numberOfRuns = o.NumberOfRuns.Value;

                      }
                      mainLogger.LogInformation($"Running {numberOfRuns} iterations");

                      if (o.Delay != null)
                      {
                          milisecondsDelay = o.Delay.Value;

                      }
                      mainLogger.LogInformation($"Using {milisecondsDelay} ms between iterations");
                  });

            var stresser = new Stresser(mainLogger, twinCatServiceADS, twinCatServiceMQTT, symbolMapper);
            var eventMonitor = new EventLoggerMonitor(mainLogger, config, twinCatServiceADS);
            eventMonitor.ConnectLogger();
            var stopwatch = Stopwatch.StartNew();
            var result = await stresser.DoStress(numberOfRuns: numberOfRuns, millisecondsDelay: milisecondsDelay, config: config, useMQTT: true);
            stopwatch.Stop();
            mainLogger.LogInformation($"Time to run {numberOfRuns} iterations was: {stopwatch.ElapsedMilliseconds} ms");           
            return result;
        }
    }


    internal class Stresser
    {
        private readonly ILogger<Program> _logger;
        private readonly TwinCatServiceADS _twinCatServiceADS;
        private readonly TwinCatServiceMQTT _twinCatServiceMQTT;
        private readonly TwinCatSymbolMapper _twinCatSymbolMapper;

        public Stresser(ILogger<Program> logger, TwinCatServiceADS twinCatServiceADS, TwinCatServiceMQTT twinCatServiceMQTT, TwinCatSymbolMapper twinCatSymbolMapper)
        {
            _logger = logger;
            _twinCatServiceADS = twinCatServiceADS;
            _twinCatServiceMQTT = twinCatServiceMQTT;
            _twinCatSymbolMapper = twinCatSymbolMapper;
        }

        public async Task<int> DoStress(int numberOfRuns, int millisecondsDelay, IConfiguration config, bool useMQTT = true)
        {
            LoadGenerator loadGenerator = new LoadGenerator(_logger, _twinCatServiceADS, _twinCatServiceMQTT, _twinCatSymbolMapper, config);
            try
            {
                _logger.LogInformation("Running test");
                if (numberOfRuns < 0)
                {
                    _logger.LogInformation("Running until stopped. Press Ctrl+c to terminate");
                }
                _ = await loadGenerator.GetData(numberOfRuns, millisecondsDelay, useMQTT);
                _logger.LogInformation("Finished");
                return 0;
            }
            catch (Exception e)
            {
                _logger.LogError("Exception: " + e.Message);
                return -1;
            }
        }
    }
}

