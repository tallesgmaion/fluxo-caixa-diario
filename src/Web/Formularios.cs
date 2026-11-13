using System.Globalization;
using Caixa;
using Microsoft.AspNetCore.Antiforgery;

namespace CaixaWeb;

sealed record Entrada(DateOnly? Data, string Tipo, string? Valor, string? Descricao, Guid Chave);

static class Formularios
{
    public static Task<IResult> ConsultarHistorico(HttpContext ctx, IAntiforgery antiforgery, SessaoCaixa sessao) =>
        Consultar(ctx, antiforgery, sessao, historico: true);

    public static Task<IResult> ConsultarConsolidado(HttpContext ctx, IAntiforgery antiforgery, SessaoCaixa sessao) =>
        Consultar(ctx, antiforgery, sessao, historico: false);

    private static async Task<IResult> Consultar(HttpContext ctx, IAntiforgery antiforgery, SessaoCaixa sessao, bool historico)
    {
        if (!await antiforgery.IsRequestValidAsync(ctx)) return Results.BadRequest();
        if (await sessao.Obter() is not { } estado) return Results.Challenge();

        var dados = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var anterior = dados["Consulta.CursorAnterior"].ToString();
        var proximo = dados["Consulta.ProximoCursor"].ToString();
        var consulta = new ConsultaCaixa
        {
            Inicio = Data(dados["Consulta.Inicio"]),
            Fim = Data(dados["Consulta.Fim"]),
            Limite = int.TryParse(dados["Consulta.Limite"], out var limite) ? limite : null,
            Pagina = int.TryParse(dados["Consulta.Pagina"], out var pagina) ? pagina : 1,
            Direcao = dados["Consulta.Direcao"].ToString() switch { "anterior" => "anterior", "proxima" => "proxima", _ => null },
            CursorAnterior = anterior.Length > 1024 ? anterior[..1025] : anterior,
            ProximoCursor = proximo.Length > 1024 ? proximo[..1025] : proximo
        };
        if (historico)
        {
            estado.Historico = consulta;
            return VerOutraPagina(ctx, "/lancamentos#historico");
        }

        estado.Consolidado = consulta;
        return VerOutraPagina(ctx, "/consolidado#resultado");
    }

    public static async Task<IResult> Registrar(HttpContext ctx, IAntiforgery antiforgery, SessaoCaixa sessao,
        CaixaClient api, TimeProvider clock)
    {
        if (!await antiforgery.IsRequestValidAsync(ctx)) return Results.BadRequest();
        if (await sessao.Obter() is not { } estado) return Results.Challenge();

        var dados = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var entrada = new Entrada(Data(dados["Formulario.Data"]), dados["Formulario.Tipo"].ToString(),
            TextoLimitado(dados["Formulario.Valor"], 25), TextoLimitado(dados["Formulario.Descricao"], 200),
            Guid.TryParse(dados["Formulario.Chave"], out var chave) ? chave : Guid.Empty);
        var valor = entrada.Valor?.Trim().Replace(',', '.');
        if (entrada.Data is not { } data || entrada.Chave == Guid.Empty ||
            !CaixaRules.ValorValido(valor, out _) || dados["Formulario.Descricao"].ToString().Length > 200 ||
            !CaixaRules.Valido(new(data, entrada.Tipo, valor!, "BRL", entrada.Descricao), CaixaRules.Hoje(clock)))
        {
            estado.RegistrarRetorno(new(null, "Confira a data, o tipo, a descrição e o valor positivo com duas casas decimais.",
                entrada with { Chave = entrada.Chave == Guid.Empty ? Guid.NewGuid() : entrada.Chave }));
        }
        else
        {
            var (item, erro) = await api.Send<Lancamento>("Lancamentos", "lancamentos",
                new NovoLancamento(data, entrada.Tipo, valor!, "BRL", entrada.Descricao), entrada.Chave);
            estado.RegistrarRetorno(new(item?.Id, erro, item is null ? entrada : null));
        }

        return VerOutraPagina(ctx, "/lancamentos");
    }

    private static DateOnly? Data(string? valor) =>
        DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data : null;

    private static string? TextoLimitado(string? valor, int limite) =>
        valor?.Length > limite ? string.Empty : valor;

    private static IResult VerOutraPagina(HttpContext ctx, string destino)
    {
        ctx.Response.Headers.Location = destino;
        return Results.StatusCode(StatusCodes.Status303SeeOther);
    }
}
