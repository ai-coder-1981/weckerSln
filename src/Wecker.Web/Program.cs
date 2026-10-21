using System.Globalization;
using MudBlazor.Services;
using Wecker.Web.Components;
using Wecker.Web.Services;

var culture = new CultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

var serviceUrl = builder.Configuration["ServiceUrl"] ?? "http://127.0.0.1:5001";
builder.Services.AddHttpClient<AlarmApiClient>(c =>
{
    c.BaseAddress = new Uri(serviceUrl);
    c.Timeout = Timeout.InfiniteTimeSpan; // kurze Timeouts setzt der AlarmApiClient pro Aufruf
});

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
