using Caixa;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureCaixa("Outbox");
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
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), stop);
                state.Channel = channel;
                while (channel.IsOpen && !stop.IsCancellationRequested)
                {
                    var batch = await store.Pending(stop);
                    foreach (var pending in batch)
                        await Publish(channel, pending, stop);
                    if (batch.Count == 0) await Task.Delay(350, stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogWarning(e, "Publicador aguardando dependências");
            }
            finally { state.Channel = null; }
            await Task.Delay(TimeSpan.FromSeconds(2), stop);
        }
    }

    private async Task Publish(IChannel channel, Pendente item, CancellationToken stop)
    {
        ActivityContext.TryParse(item.TraceParent, item.TraceState, true, out var parent);
        using var activity = CaixaRules.Traces.StartActivity("lancamento.publicar", ActivityKind.Producer, parent);
        var props = new BasicProperties { ContentType = "application/json", MessageId = item.Id.ToString(), Persistent = true };
        var trace = activity?.Id ?? item.TraceParent;
        var traceState = activity?.TraceStateString ?? item.TraceState;
        if (trace is not null) props.Headers = new Dictionary<string, object?> { ["traceparent"] = Encoding.UTF8.GetBytes(trace) };
        if (traceState is not null && props.Headers is not null) props.Headers["tracestate"] = Encoding.UTF8.GetBytes(traceState);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await channel.BasicPublishAsync(Broker.Exchange, Broker.Route, true, props, Encoding.UTF8.GetBytes(item.Envelope), timeout.Token);
        }
        catch (Exception e) when (!stop.IsCancellationRequested)
        {
            logger.LogWarning(e, "Publicação pendente {EventId}", item.Id);
            await store.Complete(item.Id, false, Math.Min(20, 1 << Math.Min(item.Tentativas, 5)) + Random.Shared.Next(1, 4), stop);
            return;
        }

        await store.Complete(item.Id, true, 0, stop);
        CaixaRules.IdadeOutbox.Record((DateTimeOffset.UtcNow - item.CriadoEmUtc).TotalSeconds);
        logger.LogInformation("Evento publicado {EventId}", item.Id);
    }
}
