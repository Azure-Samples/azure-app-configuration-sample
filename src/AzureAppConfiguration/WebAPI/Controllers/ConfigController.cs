using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using Shared.Settings;
using System.Runtime;
using System.Text;
using System.Text.Json;
using WebApi.Controllers;
using WebAPI.Models.Settings;

namespace WebAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    [FeatureGate("ShowDebugView")]
    public class ConfigController : ControllerBase
    {

        private readonly ILogger<ConfigController> _logger;
        private readonly IConfigurationRoot _root;

        public ConfigController(ILogger<ConfigController> logger, IConfigurationRoot root)
        {
            _logger = logger;
            _root = root;
        }

        /// <summary>
        /// Shows the entire configuration. Enable featureflag to enable the endpoint.
        /// </summary>
        [HttpGet(Name = "GetConfigDump")]
        public string GetConfig()
        {
            return _root.GetDebugView();
        }

    }
}

