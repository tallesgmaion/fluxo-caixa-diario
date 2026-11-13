using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Caixa;

public record NovoLancamento(DateOnly DataCaixa, string Tipo, string Valor, string Moeda, string? Descricao);
public record Lancamento(Guid Id, Guid ContaId, DateOnly DataCaixa, string Tipo, string Valor, string Moeda, string? Descricao, DateTimeOffset CriadoEmUtc);
public record Evento(Guid EventId, string EventType, int SchemaVersion, DateTimeOffset OccurredAtUtc, Movimento Data);
public record Movimento(Guid LancamentoId, Guid ContaId, DateOnly DataCaixa, string Tipo, string Valor, string Moeda);
public record Dia(DateOnly DataCaixa, string Creditos, string Debitos, string MovimentoLiquido, DateTimeOffset? AtualizadoEmUtc);
public record Resumo(Guid ContaId, string Moeda, IReadOnlyList<Dia> Dias);
public record Pagina(IReadOnlyList<Lancamento> Itens, string? ProximoCursor, string? CursorAnterior = null);

public static class CaixaRules
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public static readonly ActivitySource Traces = new("Caixa");
    public static readonly Meter Metrics = new("Caixa");
    public static readonly Histogram<double> Atraso = Metrics.CreateHistogram<double>("caixa.consolidacao.atraso", "s");
    public static readonly Histogram<double> IdadeOutbox = Metrics.CreateHistogram<double>("caixa.outbox.idade", "s");
    public static readonly Counter<long> Registros = Metrics.CreateCounter<long>("caixa.lancamentos.registros");
    public static readonly Counter<double> ValorConsolidado = Metrics.CreateCounter<double>("caixa.consolidacao.valor", "BRL");
    public static readonly Counter<long> Entregas = Metrics.CreateCounter<long>("caixa.consolidacao.entregas");
    public static DateOnly Hoje(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Fuso).DateTime);
    public static string Dinheiro(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);
    public static Dia Totais(DateOnly dia, decimal creditos, decimal debitos, DateTimeOffset? atualizado) =>
        new(dia, Dinheiro(creditos), Dinheiro(debitos), Dinheiro(creditos - debitos), atualizado);

    public static bool ValorValido(string? valor, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrEmpty(valor) || valor.Length > 20 ||
            !decimal.TryParse(valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            return false;
        return amount > 0 && amount <= 99999999999999999.99m && valor == Dinheiro(amount);
    }

    public static bool Valido(NovoLancamento n, DateOnly hoje) =>
        n.DataCaixa != default && n.DataCaixa <= hoje && n.Tipo is "credito" or "debito" &&
        n.Moeda == "BRL" && ValorValido(n.Valor, out _) && n.Descricao is not { Length: > 200 };

    public static bool Valido(Evento e) =>
        e.EventId != Guid.Empty && e.EventType == "lancamento.registrado" && e.SchemaVersion == 1 &&
        e.OccurredAtUtc != default && e.Data is { } m && m.LancamentoId != Guid.Empty &&
        m.ContaId != Guid.Empty && m.DataCaixa != default && m.Tipo is "credito" or "debito" &&
        m.Moeda == "BRL" && ValorValido(m.Valor, out _);

    public static NovoLancamento Normalizar(NovoLancamento n) =>
        n with { Descricao = string.IsNullOrWhiteSpace(n.Descricao) ? null : n.Descricao.Trim() };

    public static byte[] Hash(NovoLancamento n) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(n, Json));
}
