using System.Text.Json;
using Caixa;

var builder = WebApplication.CreateBuilder(args);
const string audience = "lancamentos-api";
builder.ConfigureApi(audience, "Lancamento");
var app = builder.Build();
app.UseApi("API de Lançamentos");
var routes = app.MapGroup("/v1/contas/{contaId:guid}/lancamentos").RequireAuthorization();

routes.MapPost("", async (Guid contaId, NovoLancamento novo,
    [Microsoft.AspNetCore.Mvc.FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    HttpContext ctx, Store store, TimeProvider clock, CancellationToken ct) =>
{
    if (!Hosting.Allowed(ctx.User, contaId, audience, "lancamentos.registrar")) return Results.Forbid();
    if (!Guid.TryParse(idempotencyKey, out var key) || key == Guid.Empty ||
        !CaixaRules.Valido(novo, CaixaRules.Hoje(clock)))
        return Hosting.Problem(ctx, 400, "lancamento_invalido");
    var result = await store.Register(contaId, key, novo, clock, ct);
    var resultado = result.Resultado switch
    {
        ResultadoRegistro.Criado => "criado",
        ResultadoRegistro.Repetido => "repetido",
        ResultadoRegistro.Conflito => "conflito",
        _ => throw new InvalidOperationException("Resultado do registro desconhecido.")
    };
    CaixaRules.Registros.Add(1, new("resultado", resultado), new("tipo", novo.Tipo));
    return result.Resultado == ResultadoRegistro.Conflito ? Hosting.Problem(ctx, 409, "chave_reutilizada") :
        Results.Created($"/v1/contas/{contaId}/lancamentos/{result.Lancamento.Id}", result.Lancamento);
}).Produces<Lancamento>(201).ProducesProblem(400).ProducesProblem(409).ProducesProblem(503);

routes.MapGet("/{id:guid}", async (Guid contaId, Guid id, HttpContext ctx, Store store, CancellationToken ct) =>
{
    if (!Hosting.Allowed(ctx.User, contaId, audience, "lancamentos.consultar")) return Results.Forbid();
    var item = await store.Find(contaId, id, ct);
    return item is null ? Hosting.Problem(ctx, 404, "lancamento_ausente") : Results.Ok(item);
}).Produces<Lancamento>().ProducesProblem(404).ProducesProblem(503);

routes.MapGet("", async (Guid contaId, DateOnly inicio, DateOnly fim, int? limite, string? cursor, HttpContext ctx, Store store, CancellationToken ct) =>
{
    if (!Hosting.Allowed(ctx.User, contaId, audience, "lancamentos.consultar")) return Results.Forbid();
    if (inicio > fim || limite is < 1 or > 100) return Hosting.Problem(ctx, 400, "filtro_invalido");
    Cursor? previous = null;
    if (cursor is not null)
    {
        if (cursor.Length > 1024) return Hosting.Problem(ctx, 400, "cursor_invalido");
        try { previous = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor), CaixaRules.Json); }
        catch (Exception e) when (e is FormatException or JsonException) { }
        if (previous is null || previous.Conta != contaId || previous.Inicio != inicio || previous.Fim != fim ||
            previous.Dia < inicio || previous.Dia > fim || previous.Id == Guid.Empty)
            return Hosting.Problem(ctx, 400, "cursor_invalido");
    }
    return Results.Ok(await store.History(contaId, inicio, fim, limite ?? 20, previous, ct));
}).Produces<Pagina>().ProducesProblem(400).ProducesProblem(503);

app.Run();
public partial class Program;
