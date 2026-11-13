using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CaixaWeb;

sealed record RetornoRegistro(Guid? Criado, string? Erro, Entrada? Rascunho);

sealed class EstadoCaixa
{
    public ConsultaCaixa? Historico { get; set; }
    public ConsultaCaixa? Consolidado { get; set; }

    private RetornoRegistro? retorno;
    public void RegistrarRetorno(RetornoRegistro valor) => Interlocked.Exchange(ref retorno, valor);
    public RetornoRegistro? ConsumirRetorno() => Interlocked.Exchange(ref retorno, null);
}

sealed class SessaoCaixa(IHttpContextAccessor accessor, Tickets tickets)
{
    public async Task<EstadoCaixa?> Obter()
    {
        var auth = await accessor.HttpContext!.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return auth.Properties?.Items.TryGetValue(Tickets.ChaveSessao, out var id) == true && id is not null
            ? tickets.ObterEstado(id) : null;
    }
}

sealed class Tickets : ITicketStore
{
    public const string ChaveSessao = "caixa.ticket";
    private sealed record Sessao(AuthenticationTicket Ticket, EstadoCaixa Estado);
    private readonly ConcurrentDictionary<string, Sessao> tickets = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        foreach (var (id, value) in tickets)
            if (value.Ticket.Properties.ExpiresUtc is { } expiry && expiry < DateTimeOffset.UtcNow)
                tickets.TryRemove(id, out _);
        var key = Guid.NewGuid().ToString("N");
        ticket.Properties.Items[ChaveSessao] = key;
        tickets[key] = new(ticket, new());
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ticket.Properties.Items[ChaveSessao] = key;
        if (tickets.TryGetValue(key, out var sessao))
            tickets.TryUpdate(key, sessao with { Ticket = ticket }, sessao);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(tickets.GetValueOrDefault(key)?.Ticket);
    public EstadoCaixa? ObterEstado(string key) => tickets.GetValueOrDefault(key)?.Estado;

    public Task RemoveAsync(string key)
    {
        tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
