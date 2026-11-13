using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Caixa;

public sealed class BrokerState : IHealthCheck
{
    public IChannel? Channel { get; set; }
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Channel?.IsOpen == true ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Canal indisponível"));
}

public static class Broker
{
    public const string Exchange = "caixa.lancamentos";
    public const string Route = "lancamento.registrado.v1";
    public const string Queue = "caixa.consolidado.v1";

    public static Task<IConnection> Connect(IConfiguration cfg, CancellationToken ct) =>
        new ConnectionFactory
        {
            HostName = cfg["Rabbit:Host"] ?? "rabbitmq",
            VirtualHost = "fluxo-caixa",
            UserName = cfg["Rabbit:User"] ?? throw new InvalidOperationException("Configure Rabbit:User."),
            Password = cfg["Rabbit:Password"] ?? throw new InvalidOperationException("Configure Rabbit:Password."),
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(10)
        }.CreateConnectionAsync(ct);
}
