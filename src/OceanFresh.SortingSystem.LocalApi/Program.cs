using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Infrastructure;
using OceanFresh.SortingSystem.Runtime;
using Microsoft.AspNetCore.Authentication;
using OceanFresh.SortingSystem.LocalApi;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5188");

builder.Services.AddOceanFreshApplication();
builder.Services.AddOceanFreshInfrastructure();
builder.Services.AddHostedService<RealtimeRuntimeWorker>();
builder.Services
    .AddAuthentication(LocalSessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, LocalSessionAuthenticationHandler>(
        LocalSessionAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
await app.Services.InitializeOceanFreshInfrastructureAsync();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    service = "OceanFresh.LocalApi",
    mode = "development-scaffold",
    timestamp = DateTimeOffset.UtcNow
}));

app.Run();
