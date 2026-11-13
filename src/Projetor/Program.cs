using Caixa;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureCaixa("EventoAplicado");
builder.Services.AddSingleton<BrokerState>();
builder.Services.AddHealthChecks().AddCheck<BrokerState>("rabbitmq", tags: ["ready"]);
builder.Services.AddHostedService<Worker>();
var app = builder.Build();
app.UseCaixa();
app.Run();

sealed class Worker(Store store, IConfiguration config, BrokerState state, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var connection = await Broker.Connect(config, stop);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stop);
                await channel.BasicQosAsync(0, 16, false, stop);
                var consumer = new AsyncEventingBasicConsumer(channel);
                var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                consumer.ReceivedAsync += (_, delivery) => Consume(channel, delivery, stop);
                consumer.UnregisteredAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
                channel.ChannelShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
                channel.CallbackExceptionAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
                await channel.BasicConsumeAsync(Broker.Queue, false, consumer, stop);
                state.Channel = channel;
                await disconnected.Task.WaitAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning(e, "Consolidador aguardando dependências"); }
            finally { state.Channel = null; }
            await Task.Delay(TimeSpan.FromSeconds(2), stop);
        }
    }

    private async Task Consume(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var headers = delivery.BasicProperties.Headers;
        static string? Text(IDictionary<string, object?>? values, string key) =>
            values is not null && values.TryGetValue(key, out var value) && value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : null;
        ActivityContext.TryParse(Text(headers, "traceparent"), Text(headers, "tracestate"), true, out var parent);
        using var activity = CaixaRules.Traces.StartActivity("lancamento.consolidar", ActivityKind.Consumer, parent);
        Evento? item = null;
        try { item = JsonSerializer.Deserialize<Evento>(delivery.Body.Span, CaixaRules.Json); }
        catch (JsonException) { }
        if (item is null || !CaixaRules.Valido(item))
        {
            logger.LogWarning("Evento inválido {MessageId} enviado à fila de falhas", delivery.BasicProperties.MessageId);
            await channel.BasicRejectAsync(delivery.DeliveryTag, false, ct);
            CaixaRules.Entregas.Add(1, new KeyValuePair<string, object?>("resultado", "invalido"));
            return;
        }

        try
        {
            if (await store.Apply(item, ct))
            {
                CaixaRules.Atraso.Record(Math.Max(0, (DateTimeOffset.UtcNow - item.OccurredAtUtc).TotalSeconds));
                CaixaRules.ValorValido(item.Data.Valor, out var valor);
                CaixaRules.ValorConsolidado.Add((double)valor, new KeyValuePair<string, object?>("tipo", item.Data.Tipo));
                CaixaRules.Entregas.Add(1, new("resultado", "aplicado"), new("tipo", item.Data.Tipo));
            }
            else
                CaixaRules.Entregas.Add(1, new("resultado", "duplicado"), new("tipo", item.Data.Tipo));
        }
        catch (SqlException e)
        {
            logger.LogWarning(e, "Consolidação adiada {EventId}", item.EventId);
            await channel.BasicRejectAsync(delivery.DeliveryTag, true, ct);
            CaixaRules.Entregas.Add(1, new("resultado", "falha_sql"), new("tipo", item.Data.Tipo));
            return;
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
        logger.LogInformation("Entrega confirmada {EventId}", item.EventId);
    }
}
