using Azure.Identity;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Shared.Middelware.ApplicationInsights.TelemetryInitializers;
using Shared.Middelware.ApplicationInsights.TelemetryProcessors;
using Shared.Services;
using Shared.Settings;
using System.Reflection;
using WebAPI.Models.Settings;

var builder = WebApplication.CreateBuilder(args);
string env = String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) ? "Development" : Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown";

var appAssembly = Assembly.Load(new AssemblyName(builder.Environment.ApplicationName));
builder.Configuration.AddUserSecrets(appAssembly, optional: true);

RuntimeSettings runtime = RuntimeSettings.Create(env);
runtime.UAMI = builder.Configuration.GetSection("UserAssignedManagedIdentityClientId").Value ?? "Unknown";
builder.Services.AddSingleton<RuntimeSettings>(runtime);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts => {
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    opts.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

builder.Configuration.AddAzureAppConfiguration(opts =>
{
    switch (runtime.CurrentEnvironment)
    {
        case RuntimeSettings.Environment.Development:
            var tid = builder.Configuration.GetSection("AppConfig:ServicePrincipal:TenantId").Value ?? "Unknown";
            var cid = builder.Configuration.GetSection("AppConfig:ServicePrincipal:ClientId").Value ?? "Unknown";
            var sec = builder.Configuration.GetSection("AppConfig:ServicePrincipal:Secret").Value ?? "Unknown";
            var connectionStringAppConfiguration = builder.Configuration.GetConnectionString("AppConfig");
            opts.Connect(connectionStringAppConfiguration).ConfigureKeyVault(kv =>
            {
                kv.SetCredential(new ClientSecretCredential(tid, cid, sec));
                kv.SetSecretRefreshInterval("API:Settings:Secrets", TimeSpan.FromHours(24));
            });
            
            break;
        case RuntimeSettings.Environment.Production:
            string c = builder.Configuration.GetSection("AppConfiguration").Value ?? throw new Exception("AppConfiguration not set");

            opts.Connect(new Uri(c), new ManagedIdentityCredential(runtime.UAMI)).ConfigureKeyVault(kv =>
            {
                kv.SetCredential(new ManagedIdentityCredential(runtime.UAMI));
                kv.SetSecretRefreshInterval("API:Settings:Secrets", TimeSpan.FromHours(24));
            });
            break;
        case RuntimeSettings.Environment.Test or RuntimeSettings.Environment.PPE:
            throw new ArgumentException("Confiuration needed");
        default:
            throw new ArgumentException("Environment not set");
    }
    opts
              .Select("Global:*")
              .Select("API:*")
              .Select($"API:{env}")
              .ConfigureRefresh(refresh =>
              {
                  refresh
                    .Register("Global", refreshAll: true)
                    .Register("API:Settings", refreshAll: true)
                    .Register("API:Settings", env, refreshAll: true)
                    .Register("API:Settings:Secrets:SecretString", refreshAll: true)
                    .SetCacheExpiration(TimeSpan.FromDays(1));
              })
              .UseFeatureFlags(featureFlagOptions =>
              {
                  featureFlagOptions.CacheExpirationInterval = TimeSpan.FromDays(1);
                  featureFlagOptions.Select(KeyFilter.Any, LabelFilter.Null).Select(KeyFilter.Any, env);
              });
    //builder.Services.AddSingleton<IConfigurationRefresher>(opts.GetRefresher());
}, optional: true);

builder.Services
       .AddAzureAppConfiguration()
       .AddHostedService<ConfigurationChangeSubscriberService>()
       .Configure<ChangeSubscriptionSettings>(builder.Configuration.GetSection("ChangeSubscription"))
       .Configure<ChangeSubscriptionSettings>(builder.Configuration.GetSection("ConnectionStrings"))
       .AddFeatureManagement().AddFeatureFilter<PercentageFilter>().AddFeatureFilter<TimeWindowFilter>();

builder.Services.AddDistributedMemoryCache();

//Add the settings to the configuration.
builder.Logging.AddConfiguration(builder.Configuration.GetSection("API:Settings:Logging"));

//first configure the global, then overwrite with specific values from app configuration
builder.Services.Configure<DemoSettings>(builder.Configuration.GetSection("Global:DemoSettings")); //global settings
builder.Services.Configure<DemoSettings>(builder.Configuration.GetSection("API:Settings:DemoSettings")); //override global settings for this service

builder.Services.Configure<SecretDemoSettings>(builder.Configuration.GetSection("API:Settings:Secrets"));

builder.Services.AddSingleton<IConfigurationRoot>((IConfigurationRoot)builder.Configuration);

builder.Services.AddApplicationInsightsTelemetry(opts =>
{
    opts.ConnectionString = builder.Configuration.GetSection("ApplicationInsights:ConnectionString").Value;
    opts.EnableDependencyTrackingTelemetryModule = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:EnableDependencyTrackingTelemetryModule").Value ?? "false");
    opts.EnablePerformanceCounterCollectionModule = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:EnablePerformanceCounterCollectionModule").Value ?? "false");
    opts.EnableAdaptiveSampling = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:EnableAdaptiveSampling").Value ?? "false");
    opts.EnableHeartbeat = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:EnableHeartbeat").Value ?? "false");
    opts.EnableAppServicesHeartbeatTelemetryModule = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:EnableAppServicesHeartbeatTelemetryModule").Value ?? "false");
    opts.EnableRequestTrackingTelemetryModule = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:EnableRequestTrackingTelemetryModule").Value ?? "true");
    opts.DeveloperMode = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:DeveloperMode").Value ?? "false");
});

builder.Services.AddSingleton<ITelemetryInitializer, DimensionTagsTelemetryInitializer>();
builder.Services.AddSingleton<ITelemetryInitializer, MetricTagsTelemetryInitializer>();

bool onlyLogFailedDependencies = bool.Parse(builder.Configuration.GetSection("ApplicationInsights:EnableDependencyTrackingTelemetryModule:OnlyLogFailed").Value ?? "false");
if (onlyLogFailedDependencies)
    builder.Services.AddApplicationInsightsTelemetryProcessor<SuccessfulDependencyFilter>();

var app = builder.Build();

app.UseAzureAppConfiguration();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();
app.MapControllers();

app.Run();

