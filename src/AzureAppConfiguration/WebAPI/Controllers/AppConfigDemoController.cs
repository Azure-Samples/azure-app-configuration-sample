using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Shared.Settings;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WebAPI.Models.Settings;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AppConfigDemoController : ControllerBase
    {
        public record AppConfigDemoResponse(int Counter, int SomeInteger, string SomeString, bool IsDoMagicFeatureEnabled, string? SomeLocalString, string? SomeSecretString, string MachineName);
        private readonly ILogger<AppConfigDemoController> _logger;
        private readonly IDistributedCache _cache;
        private const string key = "Counter";
        private readonly IOptionsMonitor<DemoSettings> _settings;
        private readonly IFeatureManager _featureManager;
        private readonly IOptionsMonitor<SecretDemoSettings> _secretSettings;

        public AppConfigDemoController(ILogger<AppConfigDemoController> logger,
            IDistributedCache cache,
            IOptionsMonitor<DemoSettings> settings,
            IOptionsMonitor<SecretDemoSettings> secretSettings,
            IFeatureManager featureManager)
        {
            _logger = logger;
            _cache = cache;
            _settings = settings;
            _featureManager = featureManager;
            _secretSettings = secretSettings;
        }
        /// <summary>
        /// Get some test data, including data from app configuration
        /// </summary>
        [HttpGet(Name = "GetSomeData")]
        public async Task<AppConfigDemoResponse> Get()
        {

            _logger.LogTrace("Got call");
            _logger.LogDebug("Got call");
            _logger.LogInformation("Got call");
            
            try
            {
                var counterStr = _cache.GetString(key);
                if (int.TryParse(counterStr, out int counter)) counter++;
                else counter = 0;
                _cache.SetString(key, counter.ToString());
                
                bool magicFeature= await _featureManager.IsEnabledAsync("DoMagic");
                //m- will add custom metric with MetricTagsTelemetryInitializer
                Activity.Current?.AddTag("m-someInteger", _settings.CurrentValue.SomeInteger.ToString());
                //m- will add custom dimension with DimensionTagsTelemetryInitializer
                Activity.Current?.AddTag("d-someString", _settings.CurrentValue.SomeString);
                return new AppConfigDemoResponse(Counter: counter, SomeInteger : _settings.CurrentValue.SomeInteger, SomeString : _settings.CurrentValue.SomeString??"Empty", IsDoMagicFeatureEnabled : magicFeature, SomeLocalString: _settings.CurrentValue.SomeGlobalString, SomeSecretString: _secretSettings.CurrentValue.SecretString, MachineName: Environment.MachineName);
            }
            catch (Exception rce)
            {
                _logger.LogError(rce,rce.Message);
                throw;
            }
        }


    }
}
