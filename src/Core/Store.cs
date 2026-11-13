using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Caixa;

public enum ResultadoRegistro { Criado, Repetido, Conflito }
public record Registro(Lancamento Lancamento, ResultadoRegistro Resultado);
public record Pendente(Guid Id, string Envelope, string? TraceParent, string? TraceState, DateTimeOffset CriadoEmUtc, int Tentativas);
public record Cursor(Guid Conta, DateOnly Inicio, DateOnly Fim, DateOnly Dia, Guid Id, bool Anterior = false);

public sealed class Store(IConfiguration config)
{
    private readonly string connection = config.GetConnectionString("Caixa")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:Caixa.");

    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var sql = new SqlConnection(connection);
        try { await sql.OpenAsync(ct); return sql; }
        catch { await sql.DisposeAsync(); throw; }
    }

    private static SqlCommand Command(SqlConnection sql, string statement, params (string Name, object? Value)[] args)
    {
        var cmd = sql.CreateCommand();
        cmd.CommandText = statement;
        cmd.CommandTimeout = 5;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private const string Columns = "Id, ContaId, DataCaixa, Tipo, Valor, Descricao, CriadoEmUtc";
    private static Lancamento Read(SqlDataReader r) =>
        new(r.GetGuid(0), r.GetGuid(1), DateOnly.FromDateTime(r.GetDateTime(2)), r.GetString(3),
            CaixaRules.Dinheiro(r.GetDecimal(4)), "BRL", r.IsDBNull(5) ? null : r.GetString(5),
            r.GetDateTimeOffset(6));

    public async Task<bool> Ready(string table, CancellationToken ct)
    {
        await using var sql = await Open(ct);
        await using var cmd = Command(sql, $"SELECT TOP (1) 1 FROM dbo.{table}");
        await cmd.ExecuteScalarAsync(ct);
        return true;
    }

    public async Task<Registro> Register(Guid conta, Guid chave, NovoLancamento novo, TimeProvider clock, CancellationToken ct)
    {
        novo = CaixaRules.Normalizar(novo);
        var hash = CaixaRules.Hash(novo);
        var lancamento = new Lancamento(Guid.NewGuid(), conta, novo.DataCaixa, novo.Tipo, novo.Valor,
            "BRL", novo.Descricao, clock.GetUtcNow());
        var evt = new Evento(Guid.NewGuid(), "lancamento.registrado", 1, lancamento.CriadoEmUtc,
            new(lancamento.Id, conta, novo.DataCaixa, novo.Tipo, novo.Valor, "BRL"));
        CaixaRules.ValorValido(novo.Valor, out var amount);
        await using var sql = await Open(ct);
        await using var cmd = Command(sql, """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            INSERT dbo.Lancamento(Id,ContaId,DataCaixa,Tipo,Valor,Descricao,CriadoEmUtc,Chave,Hash)
              VALUES(@id,@conta,@dia,@tipo,@valor,@descricao,@agora,@chave,@hash);
            INSERT dbo.Outbox(EventoId,LancamentoId,Envelope,TraceParent,TraceState,CriadoEmUtc,ProximaTentativa)
              VALUES(@evento,@id,@envelope,@parent,@state,@agora,@agora);
            COMMIT;
            """, ("@id", lancamento.Id), ("@conta", conta), ("@dia", novo.DataCaixa), ("@tipo", novo.Tipo),
            ("@valor", amount), ("@descricao", novo.Descricao), ("@agora", lancamento.CriadoEmUtc),
            ("@chave", chave), ("@hash", hash), ("@evento", evt.EventId),
            ("@envelope", JsonSerializer.Serialize(evt, CaixaRules.Json)), ("@parent", Activity.Current?.Id),
            ("@state", Activity.Current?.TraceStateString));
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return new(lancamento, ResultadoRegistro.Criado);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            await using var query = Command(sql, $"SELECT {Columns}, Hash FROM dbo.Lancamento WHERE ContaId=@conta AND Chave=@chave",
                ("@conta", conta), ("@chave", chave));
            await using var r = await query.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw;
            return new(Read(r), hash.SequenceEqual(r.GetFieldValue<byte[]>(7))
                ? ResultadoRegistro.Repetido : ResultadoRegistro.Conflito);
        }
    }

    public async Task<Lancamento?> Find(Guid conta, Guid id, CancellationToken ct)
    {
        await using var sql = await Open(ct);
        await using var cmd = Command(sql, $"SELECT {Columns} FROM dbo.Lancamento WHERE ContaId=@conta AND Id=@id", ("@conta", conta), ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Read(r) : null;
    }

    public async Task<Pagina> History(Guid conta, DateOnly inicio, DateOnly fim, int limite, Cursor? cursor, CancellationToken ct)
    {
        await using var sql = await Open(ct);
        var anterior = cursor?.Anterior == true;
        var comparison = anterior ? ">" : "<";
        var order = anterior ? "ASC" : "DESC";
        var predicate = cursor is null ? "" :
            $" AND (DataCaixa {comparison} @dia OR (DataCaixa = @dia AND Id {comparison} @id))";
        await using var cmd = Command(sql, $"""
            SELECT TOP (@limite) {Columns} FROM dbo.Lancamento
            WHERE ContaId=@conta AND DataCaixa BETWEEN @inicio AND @fim{predicate}
            ORDER BY DataCaixa {order}, Id {order}
            """, ("@limite", limite + 1), ("@conta", conta), ("@inicio", inicio), ("@fim", fim),
            ("@dia", cursor?.Dia), ("@id", cursor?.Id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Lancamento>();
        while (await r.ReadAsync(ct)) list.Add(Read(r));
        var more = list.Count > limite;
        if (more) list.RemoveAt(limite);
        if (anterior) list.Reverse();
        if (list.Count == 0) return new(list, null);

        string Encode(Lancamento item, bool backward) =>
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                new Cursor(conta, inicio, fim, item.DataCaixa, item.Id, backward), CaixaRules.Json));

        var hasPrevious = anterior ? more : cursor is not null;
        var hasNext = anterior || more;
        return new(list, hasNext ? Encode(list[^1], false) : null, hasPrevious ? Encode(list[0], true) : null);
    }

    public async Task<IReadOnlyList<Pendente>> Pending(CancellationToken ct)
    {
        await using var sql = await Open(ct);
        await using var cmd = Command(sql, """
            SELECT TOP (50) EventoId,Envelope,TraceParent,TraceState,CriadoEmUtc,Tentativas
            FROM dbo.Outbox WHERE PublicadoEmUtc IS NULL AND ProximaTentativa <= SYSUTCDATETIME()
            ORDER BY ProximaTentativa, EventoId
            """);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Pendente>();
        while (await r.ReadAsync(ct))
            list.Add(new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetDateTimeOffset(4), r.GetInt32(5)));
        return list;
    }

    public async Task Complete(Guid id, bool published, int delay, CancellationToken ct)
    {
        await using var sql = await Open(ct);
        await using var cmd = Command(sql, published
            ? "UPDATE dbo.Outbox SET PublicadoEmUtc=SYSUTCDATETIME() WHERE EventoId=@id AND PublicadoEmUtc IS NULL"
            : "UPDATE dbo.Outbox SET Tentativas=Tentativas+1, ProximaTentativa=DATEADD(second,@delay,SYSUTCDATETIME()) WHERE EventoId=@id AND PublicadoEmUtc IS NULL",
            ("@id", id), ("@delay", delay));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> Apply(Evento e, CancellationToken ct)
    {
        CaixaRules.ValorValido(e.Data.Valor, out var valor);
        await using var sql = await Open(ct);
        await using var cmd = Command(sql, """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.EventoAplicado WITH (UPDLOCK,HOLDLOCK) WHERE EventoId=@evento)
            BEGIN
                COMMIT;
                SELECT CAST(0 AS bit);
                RETURN;
            END;
            DECLARE @agora datetimeoffset = SYSUTCDATETIME();
            INSERT dbo.EventoAplicado(EventoId,ContaId,DataCaixa,AplicadoEmUtc) VALUES(@evento,@conta,@dia,@agora);
            UPDATE dbo.ResumoDiario WITH (UPDLOCK,HOLDLOCK)
                SET Creditos=Creditos+@credito, Debitos=Debitos+@debito, Quantidade=Quantidade+1, AtualizadoEmUtc=@agora
                WHERE ContaId=@conta AND DataCaixa=@dia;
            IF @@ROWCOUNT=0
                INSERT dbo.ResumoDiario(ContaId,DataCaixa,Creditos,Debitos,Quantidade,AtualizadoEmUtc)
                  VALUES(@conta,@dia,@credito,@debito,1,@agora);
            COMMIT;
            SELECT CAST(1 AS bit);
            """, ("@evento", e.EventId), ("@conta", e.Data.ContaId), ("@dia", e.Data.DataCaixa),
            ("@credito", e.Data.Tipo == "credito" ? valor : 0m), ("@debito", e.Data.Tipo == "debito" ? valor : 0m));
        return (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
    }

    public async Task<Resumo> Summary(Guid conta, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var sql = await Open(ct);
        await using var cmd = Command(sql, """
            SELECT DataCaixa,Creditos,Debitos,AtualizadoEmUtc FROM dbo.ResumoDiario
            WHERE ContaId=@conta AND DataCaixa BETWEEN @inicio AND @fim ORDER BY DataCaixa
            """, ("@conta", conta), ("@inicio", inicio), ("@fim", fim));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new Dictionary<DateOnly, Dia>();
        while (await r.ReadAsync(ct))
        {
            var day = DateOnly.FromDateTime(r.GetDateTime(0));
            rows[day] = CaixaRules.Totais(day, r.GetDecimal(1), r.GetDecimal(2), r.GetDateTimeOffset(3));
        }
        var days = Enumerable.Range(inicio.DayNumber, fim.DayNumber - inicio.DayNumber + 1)
            .Select(DateOnly.FromDayNumber).Select(day => rows.GetValueOrDefault(day) ?? CaixaRules.Totais(day, 0, 0, null));
        return new(conta, "BRL", days.ToArray());
    }
}
