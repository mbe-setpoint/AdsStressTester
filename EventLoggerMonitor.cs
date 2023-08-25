using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TcEventLoggerAdsProxyLib;

namespace AdsStressTester
{
    internal class EventLoggerMonitor
    {
        private readonly ILogger _logger;
        private readonly TcEventLogger _eventLogger;
        private readonly TwinCatServiceADS _twinCatService;
        private readonly IConfiguration _config;
        private readonly string? _netId;

        public EventLoggerMonitor(ILogger logger, IConfiguration config, TwinCatServiceADS twinCatService)
        {
            _eventLogger = new TcEventLogger();
            _twinCatService = twinCatService;
            _logger = logger;
            _config = config;
            _netId = _config.GetValue<string>("TwinCatNetId");
            _eventLogger.MessageSent += OnMessageSent;
            _eventLogger.AlarmRaised += OnAlarmRaised;
            _eventLogger.AlarmCleared += OnAlarmCleared;
            _eventLogger.AlarmConfirmed += OnAlarmConfirmed;            
        }

        public void ConnectLogger()
        {
            _logger.LogInformation("Logger is connecting to " + _netId);
            try
            {
                _eventLogger.Connect(_netId);                
                _logger.LogInformation("Logger successfully connected to " + _netId);
            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot connect to event logger at server startup: " + ex);
            }
        }

        private void OnAlarmConfirmed(TcAlarm alarm, bool bRemove)
        {
            _logger.LogInformation($"Alarm Confirmed: {alarm.EventId}");
        }

        private void OnAlarmCleared(TcAlarm alarm, bool bRemove)
        {
            _logger.LogInformation($"Alarm Cleared: {alarm.EventId}");
        }

        private void OnAlarmRaised(TcAlarm alarm)
        {
            _logger.LogInformation($"Alarm Raised: {alarm.EventId}");
        }

        private void OnMessageSent(TcMessage message)
        {
            _logger.LogInformation($"Message received: {message.EventId}");
        }
    }
}
