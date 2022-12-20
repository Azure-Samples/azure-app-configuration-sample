using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Settings
{
    public class ChangeSubscriptionSettings
    {
        public string? ServiceBusConnectionString { get; set; }

        public string? ServiceBusTopic { get; set; }
        string? _serviceBusSubscription;
        public string? ServiceBusSubscriptionPrefix
        {
            get { return _serviceBusSubscription; }
            set { _serviceBusSubscription = $"{value}-{Environment.MachineName.ToString()}"; }
        }
        public int AutoDeleteOnIdleInHours { get; set; }
        private string? _serviceBusNamespace;
        public string? ServiceBusNamespace {
            get { return _serviceBusNamespace; }
            set {
                this._serviceBusNamespace = value?.Replace(@"https://", "").Replace(@":443/","");}
        }
        public int? MaxDelayBeforeCacheIsMarkedDirtyInSeconds { get; set; }
    }
}
