using SysDash.NetCore.Backend;
using SysDash.NetCore.Backend.Endpoints;

var builder = WebApplication.CreateBuilder(args);
var configuredUrls = builder.Configuration["urls"];
if (string.IsNullOrWhiteSpace(configuredUrls))
{
	// Default to LAN-accessible binding for dashboard and mobile clients.
	builder.WebHost.UseUrls("http://0.0.0.0:5000");
}

builder.Services.ConfigureHttpJsonOptions(opts =>
{
	opts.SerializerOptions.PropertyNamingPolicy = null;
});
builder.Services.AddRazorPages();
builder.Services.AddSingleton<HttpClient>(_ =>
{
	var unraidHandler = new HttpClientHandler
	{
		ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
	};
	return new HttpClient(unraidHandler)
	{
		Timeout = TimeSpan.FromSeconds(30),
	};
});
builder.Services.AddSingleton<IAppState>(sp =>
	new AppState(
		sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
		sp.GetRequiredService<IConfiguration>(),
		sp.GetRequiredService<HttpClient>(),
		sp.GetRequiredService<ILogger<AppState>>()));
builder.Services.AddHostedService<AppStateHostedService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapRazorPages();
app.MapSysDashApi();

app.Run();

