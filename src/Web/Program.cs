using Caixa;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Http.Resilience;
using CaixaWeb;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureCaixa();
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CaixaClient>();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<Tickets>();
builder.Services.AddScoped<SessaoCaixa>();
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<Tickets>((o, tickets) =>
    {
        o.SessionStore = tickets;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(25);
        o.SlidingExpiration = false;
    });

builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
}).AddCookie().AddOpenIdConnect(o =>
{
    o.Authority = builder.Configuration["Identity:Authority"];
    o.MetadataAddress = builder.Configuration["Identity:Metadata"];
    o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    o.ClientId = "caixa-web";
    o.ClientSecret = builder.Configuration["Identity:ClientSecret"];
    o.ResponseType = "code";
    o.UsePkce = true;
    o.SaveTokens = true;
    o.MapInboundClaims = false;
    o.TokenValidationParameters.ValidIssuer = o.Authority;
    o.TokenValidationParameters.NameClaimType = "preferred_username";
    if (builder.Environment.IsDevelopment())
    {
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.NonceCookie.SameSite = SameSiteMode.Lax;
        o.ResponseMode = "query";
    }
});

foreach (var name in new[] { "Lancamentos", "Consolidado" })
{
    builder.Services.AddHttpClient(name, c => c.BaseAddress = new Uri(builder.Configuration[$"Api:{name}"]!))
        .AddStandardResilienceHandler(o =>
        {
            o.Retry.DisableForUnsafeHttpMethods();
            o.Retry.MaxRetryAttempts = 1;
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(4);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(9);
        });
}

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseCaixa();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapPost("/sair", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(ctx)) return Results.BadRequest();
    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
}).RequireAuthorization();
app.MapRazorComponents<App>();
app.MapPost("/lancamentos/consultar", Formularios.ConsultarHistorico).RequireAuthorization();
app.MapPost("/consolidado/consultar", Formularios.ConsultarConsolidado).RequireAuthorization();
app.MapPost("/lancamentos/registrar", Formularios.Registrar).RequireAuthorization();
app.MapFallback("/{**rota}", () => new RazorComponentResult<App>(new { PaginaInexistente = true })
{
    StatusCode = StatusCodes.Status404NotFound
}).WithMetadata(new HttpMethodMetadata(["GET", "HEAD"]));
app.Run();
