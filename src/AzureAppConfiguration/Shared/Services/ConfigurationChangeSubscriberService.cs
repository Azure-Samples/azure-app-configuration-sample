using Azure.Identity;
using Azure.Messaging.EventGrid;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Shared.Settings;
using System.Reactive.Linq;
using System.Text.Json;

namespace Shared.Services
{
    public class ConfigurationChangeSubscriberService : IHostedService
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IConfigurationRefresher? _refresher;
        private readonly ChangeSubscriptionSettings _changeSubscriptionSettings;
        private readonly ILogger<ConfigurationChangeSubscriberService> _logger;
        private readonly RuntimeSettings _environment;
        private readonly IConfigurationRoot _configurationRoot;
        private readonly IFeatureManager _featureManager;

        public ConfigurationChangeSubscriberService(
            IHostApplicationLifetime hostApplicationLifetime, IConfigurationRefresherProvider refreshProvider, IOptions<ChangeSubscriptionSettings> ChangeSubscriptionSettings, ILogger<ConfigurationChangeSubscriberService> logger, RuntimeSettings env, IConfigurationRoot configuration, IFeatureManager featureManager)
        {
            _hostApplicationLifetime = hostApplicationLifetime;
            _refresher = refreshProvider.Refreshers.FirstOrDefault(); ; 
            _changeSubscriptionSettings = ChangeSubscriptionSettings.Value;
            _logger = logger;
            _environment = env;
            _configurationRoot = configuration;
            _featureManager = featureManager;
        }
        /*
         * Needed for creating a subscription
         */
        private ServiceBusAdministrationClient GetServiceBusAdminClient()
        {
            ServiceBusAdministrationClient client;
            switch (_environment.CurrentEnvironment)
            {
                case RuntimeSettings.Environment.Development:
                    client = new ServiceBusAdministrationClient(_changeSubscriptionSettings.ServiceBusConnectionString);
                    break;
                case RuntimeSettings.Environment.Production:
                    client = new ServiceBusAdministrationClient(_changeSubscriptionSettings.ServiceBusNamespace, new ManagedIdentityCredential(_environment.UAMI));
                    break;
                default:
                    throw new ArgumentException("Environment not configured");
            }
            return client;
        }
        private ServiceBusClient GetServiceBusClient()
        {
            var clientOptions = new ServiceBusClientOptions() { TransportType = ServiceBusTransportType.AmqpWebSockets };
            ServiceBusClient client;

            switch (_environment.CurrentEnvironment)
            {
                case RuntimeSettings.Environment.Development:
                    client = new ServiceBusClient(_changeSubscriptionSettings.ServiceBusConnectionString, clientOptions);
                    break;
                case RuntimeSettings.Environment.Production:
                    client = new ServiceBusClient(_changeSubscriptionSettings.ServiceBusNamespace, new ManagedIdentityCredential(_environment.UAMI), clientOptions);
                    break;
                default:
                    throw new ArgumentException("Environment not configured");
            }
            return client;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _hostApplicationLifetime.ApplicationStarted.Register(OnStarted);
            _hostApplicationLifetime.ApplicationStopping.Register(OnStopping);
            _hostApplicationLifetime.ApplicationStopped.Register(OnStopped);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        private void OnStarted()
        {
            ConfigurationChangeSubscribe().GetAwaiter();
            _logger.LogInformation("Start");
        }

        private void OnStopping()
        {
            ConfigurationChangeUnSubscribe().GetAwaiter();
            _logger.LogInformation("Stopping");
        }
        private void OnStopped()
        {
            _logger.LogInformation("Stopped");
        }

        private async Task ConfigurationChangeSubscribe()
        {
            try
            {
                var client = GetServiceBusAdminClient();
                if (!client.SubscriptionExistsAsync(_changeSubscriptionSettings.ServiceBusTopic, _changeSubscriptionSettings.ServiceBusSubscriptionPrefix).Result)
                {
                    var so = new CreateSubscriptionOptions(_changeSubscriptionSettings.ServiceBusTopic, _changeSubscriptionSettings.ServiceBusSubscriptionPrefix);
                    so.AutoDeleteOnIdle = TimeSpan.FromHours(_changeSubscriptionSettings.AutoDeleteOnIdleInHours);
                    await client.CreateSubscriptionAsync(so);
                }

                var servicebusClient = GetServiceBusClient();
                var processor = servicebusClient.CreateProcessor(_changeSubscriptionSettings.ServiceBusTopic, _changeSubscriptionSettings.ServiceBusSubscriptionPrefix, new ServiceBusProcessorOptions() { });

                processor.ProcessMessageAsync += MessageHandler;
                processor.ProcessErrorAsync += ErrorHandler;

                await processor.StartProcessingAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("already exists"))
            {
                _logger.LogTrace(ex, ex.Message);
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }
        }
        private async Task ConfigurationChangeUnSubscribe()
        {
            var client = GetServiceBusAdminClient();
            if (client.SubscriptionExistsAsync(_changeSubscriptionSettings.ServiceBusTopic, _changeSubscriptionSettings.ServiceBusSubscriptionPrefix).Result)
            {
                await client.DeleteSubscriptionAsync(_changeSubscriptionSettings.ServiceBusTopic, _changeSubscriptionSettings.ServiceBusSubscriptionPrefix);
            }
        }
        private record EventData(string ObjectType, string VaultName, string ObjectName);
        private async Task MessageHandler(ProcessMessageEventArgs args)
        {
            try
            {
                string body = args.Message.Body.ToString();
                // Build EventGridEvent from notification message
                 EventGridEvent eventGridEvent = EventGridEvent.Parse(BinaryData.FromBytes(args.Message.Body));
                _logger.LogTrace($"Received: {eventGridEvent.Data}");

                // Create PushNotification from eventGridEvent. pushNotification will be null, if its a secret!
                eventGridEvent.TryCreatePushNotification(out PushNotification pushNotification);

                var d = System.Text.Json.JsonSerializer.Deserialize<EventData>(eventGridEvent.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!string.IsNullOrEmpty(d?.ObjectName) && (d.ObjectType.ToLower() == "secret" || d.ObjectType.ToLower() == "certificate"))
                {
                    if (await _featureManager.IsEnabledAsync("AutoUpdateLatestVersionSecrets"))
                    {
                        if (eventGridEvent.EventType == "Microsoft.KeyVault.SecretNewVersionCreated")
                        {
                            //seems a bit brute force. But currently it seems to be the only solution to reload secrets.
                            _logger.LogTrace($"Refreshing all, triggered by secret: " + eventGridEvent.Subject);
                            _configurationRoot.Reload();
                        }
                    }
                    else
                        _logger.LogTrace($"Not processing: {eventGridEvent.EventType}");
                    await args.CompleteMessageAsync(args.Message);
                }
                else if (pushNotification != null)
                {
                    // Prompt Configuration Refresh based on the PushNotification
                    _refresher?.ProcessPushNotification(pushNotification, TimeSpan.FromSeconds(_changeSubscriptionSettings.MaxDelayBeforeCacheIsMarkedDirtyInSeconds ?? 30));
                    await args.CompleteMessageAsync(args.Message);
                }
                else
                    throw new Exception("Unknown message");
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }
        }

        private async Task ErrorHandler(ProcessErrorEventArgs args)
        {
            if (args.Exception != null && (args.Exception.Message.Contains("The messaging entity") && args.Exception.Message.Contains("not be found")))
            {
                await ConfigurationChangeSubscribe();
                _logger.LogInformation("Topic subscription restablished: " + args.Exception.Message);
                await Task.CompletedTask;
            }
            else
            {
                _logger.LogError(args.Exception!.Message, args.Exception);
                await Task.CompletedTask;
            }
        }
    }
}
