using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Microsoft.OpenApi;

namespace Caixa;

public static class Hosting
{
    public static void ConfigureCaixa(this WebApplicationBuilder builder, string? table = null)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProblemDetails();
        var checks = builder.Services.AddHealthChecks();
        if (table is not null)
        {
            builder.Services.AddSingleton<Store>();
            builder.Services.AddSingleton(new SqlProbeTarget(table));
            checks.AddCheck<SqlProbe>("sql", tags: ["ready"], timeout: TimeSpan.FromSeconds(6));
        }

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            var resource = ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName);
            builder.Logging.AddOpenTelemetry(o =>
            {
                o.SetResourceBuilder(resource);
                o.IncludeFormattedMessage = true;
                o.AddOtlpExporter();
            });
            builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService(builder.Environment.ApplicationName))
                .WithTracing(t => t.AddSource("Caixa").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter())
                .WithMetrics(m => m.AddMeter("Caixa").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter());
        }
    }

    public static void ConfigureApi(this WebApplicationBuilder builder, string audience, string table)
    {
        builder.ConfigureCaixa(table);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = builder.Configuration["Identity:Authority"];
            if (builder.Configuration["Identity:Metadata"] is { } metadata) o.MetadataAddress = metadata;
            o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            o.Audience = audience;
            o.MapInboundClaims = false;
            o.TokenValidationParameters.ValidIssuer = o.Authority;
        });
        builder.Services.AddAuthorization();
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            };
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
            return Task.CompletedTask;
        }));
    }

    public static void UseCaixa(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(async ctx =>
        {
            var error = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            await (error is SqlException
                ? Problem(ctx, 503, "persistencia_indisponivel")
                : Problem(ctx, 500, "erro_interno")).ExecuteAsync(ctx);
        }));
        app.UseStatusCodePages(ctx => Problem(ctx.HttpContext, ctx.HttpContext.Response.StatusCode,
            ctx.HttpContext.Response.StatusCode switch
            {
                400 => "requisicao_invalida", 401 => "autenticacao_necessaria", 403 => "acesso_negado", _ => "erro_http"
            }).ExecuteAsync(ctx.HttpContext));
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = x => x.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = x => x.Tags.Contains("ready") });
    }

    public static void UseApi(this WebApplication app, string title)
    {
        app.UseCaixa();
        app.UseAuthentication();
        app.UseAuthorization();
        if (app.Environment.IsDevelopment())
        {
            app.UseStaticFiles();
            app.MapOpenApi();
            app.MapScalarApiReference(o =>
            {
                o.WithTitle(title).WithFavicon("/carrefour-banco-icon.png");
                o.HeaderContent = """
                    <div style="display:flex;align-items:center;flex-wrap:wrap;gap:.5rem 1rem;background:#fff;color:#00529c;border-bottom:3px solid #61b2e5;padding:.75rem 1rem">
                      <img src="/carrefour-banco.png" alt="Carrefour Banco" width="140" style="height:auto">
                      <span>Fluxo de Caixa Diário · Demonstração técnica independente</span>
                    </div>
                    """;
            });
        }
    }

    public static bool Allowed(ClaimsPrincipal principal, Guid conta, string audience, string role)
    {
        if (!Guid.TryParse(principal.FindFirstValue("conta_id"), out var allowed) || allowed != conta)
            return false;
        var raw = principal.FindFirstValue("resource_access");
        if (string.IsNullOrEmpty(raw)) return false;
        using var json = JsonDocument.Parse(raw);
        return json.RootElement.TryGetProperty(audience, out var client) &&
            client.TryGetProperty("roles", out var roles) &&
            roles.EnumerateArray().Any(x => x.GetString() == role);
    }

    public static IResult Problem(HttpContext ctx, int status, string code) =>
        Results.Problem(statusCode: status, title: code.Replace('_', ' '),
            extensions: new Dictionary<string, object?> { ["codigo"] = code, ["traceId"] = ctx.TraceIdentifier });
}

public record SqlProbeTarget(string Table);

public sealed class SqlProbe(Store store, SqlProbeTarget target) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        await store.Ready(target.Table, cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
