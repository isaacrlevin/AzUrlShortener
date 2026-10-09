using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;
using ShortenerTools.Admin;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(options => options.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-ShortenerAdmin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.AccessDeniedPath = "/unauthorized";
    })
    .AddOpenIdConnect(options =>
    {
        var tenantId = RequiredGuidSetting("Entra:TenantId");
        var administratorObjectId = RequiredGuidSetting("Entra:AdministratorObjectId");
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.ClientId = RequiredGuidSetting("Entra:ClientId");
        options.ClientSecret = RequiredSetting("Entra:ClientSecret");
        options.ResponseType = "code";
        options.UsePkce = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
        options.Events.OnTokenValidated = context =>
        {
            if (!AdministratorAccess.IsAllowed(context.Principal, tenantId, administratorObjectId))
            {
                context.Fail("This account is not the configured administrator.");
            }
            else if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
            }
            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("EntraAuthentication").LogWarning(context.Failure, "Administrator sign-in failed.");
            context.HandleResponse();
            context.Response.Redirect("/unauthorized");
            return Task.CompletedTask;
        };
    });
builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
    .Validate(_ => !string.IsNullOrWhiteSpace(builder.Configuration["AdminApiKey"]),
        "AdminApiKey must be configured.")
    .ValidateOnStart();
builder.Services.AddAuthorization();
builder.Services.AddHttpClient("management", client =>
{
    client.BaseAddress = new Uri("https+http://management-api");
    client.DefaultRequestHeaders.Add("X-Admin-Api-Key", RequiredSetting("AdminApiKey"));
});
#pragma warning disable EXTEXP0001 // Replace inherited resilience only for slow AI requests.
builder.Services.AddHttpClient("description", client =>
{
    client.BaseAddress = new Uri("https+http://management-api");
    client.DefaultRequestHeaders.Add("X-Admin-Api-Key", RequiredSetting("AdminApiKey"));
    client.Timeout = TimeSpan.FromSeconds(130);
})
.RemoveAllResilienceHandlers()
.AddStandardResilienceHandler(options =>
{
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(240);
    options.Retry.DisableForUnsafeHttpMethods();
});
#pragma warning restore EXTEXP0001
builder.Services.AddScoped(services =>
    services.GetRequiredService<IHttpClientFactory>().CreateClient("management"));

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/auth/login", () => Results.Challenge(
    new AuthenticationProperties { RedirectUri = "/" },
    [OpenIdConnectDefaults.AuthenticationScheme]));
app.MapGet("/error", () => Results.Problem("The administrator application could not complete this request."));
app.MapPost("/auth/logout", async (HttpContext context,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
}).RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();
app.Run();

string RequiredSetting(string name) =>
    !string.IsNullOrWhiteSpace(builder.Configuration[name])
        ? builder.Configuration[name]!
        : throw new InvalidOperationException($"The secret or setting '{name}' must be configured.");

string RequiredGuidSetting(string name) =>
    Guid.TryParse(RequiredSetting(name), out var value) && value != Guid.Empty
        ? value.ToString()
        : throw new InvalidOperationException($"The setting '{name}' must be a non-empty GUID.");
