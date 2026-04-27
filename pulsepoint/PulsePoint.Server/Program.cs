using PulsePoint;
using PulsePoint.Api;
using PulsePoint.Data;
using PulsePoint.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var dbPath = builder.Configuration["DbPath"] ?? "pulsepoint.db";
var db = new Database(dbPath);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(new AppState(db));
builder.Services.AddHostedService<ServiceMonitor>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

Endpoints.Map(app);

app.Run();
