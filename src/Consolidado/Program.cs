using Caixa;

var builder = WebApplication.CreateBuilder(args);
const string audience = "consolidado-api";
builder.ConfigureApi(audience, "ResumoDiario");
var app = builder.Build();
app.UseApi("API de Consolidado");

app.MapGet("/v1/contas/{contaId:guid}/consolidado-diario",
    async (Guid contaId, DateOnly inicio, DateOnly fim, HttpContext ctx, Store store, CancellationToken ct) =>
    {
        if (!Hosting.Allowed(ctx.User, contaId, audience, "consolidado.consultar")) return Results.Forbid();
        if (inicio > fim || fim.DayNumber - inicio.DayNumber >= 31)
            return Hosting.Problem(ctx, 400, "periodo_invalido");
        return Results.Ok(await store.Summary(contaId, inicio, fim, ct));
    }).RequireAuthorization().Produces<Resumo>().ProducesProblem(400).ProducesProblem(503);

app.Run();
public partial class Program;
