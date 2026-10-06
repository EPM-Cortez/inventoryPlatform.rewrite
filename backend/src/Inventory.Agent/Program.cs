using Inventory.Agent;
using Inventory.Agent.Services;
using Microsoft.Extensions.Options;

if (AgentBootstrapCommand.IsRequested(args))
{
    await AgentBootstrapCommand.ExecuteAsync(
        args,
        CancellationToken.None);
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(
    options => options.ServiceName = "InventoryEndpointAgent");
builder.Services.AddSingleton<
    IPostConfigureOptions<AgentOptions>,
    AgentRegistryOptionsPostConfigure>();
builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(
        options =>
        {
            options.Validate();
            return true;
        },
        "Endpoint-agent configuration is invalid.")
    .ValidateOnStart();

builder.Services
    .AddHttpClient<IAgentApiClient, AgentApiClient>()
    .ConfigurePrimaryHttpMessageHandler(
        () => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            ConnectTimeout = TimeSpan.FromSeconds(30),
        });
builder.Services.AddSingleton<IDeviceKeyService, DeviceKeyService>();
builder.Services.AddSingleton<
    IDeviceInventoryCollector,
    DeviceInventoryCollector>();
builder.Services.AddSingleton<
    IEnrollmentTokenStore,
    EnrollmentTokenStore>();
builder.Services.AddSingleton<IProtectedStateStore, ProtectedStateStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
