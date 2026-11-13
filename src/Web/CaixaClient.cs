using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using Caixa;
using Microsoft.AspNetCore.Authentication;
using Polly.Timeout;

namespace CaixaWeb;

public sealed class CaixaClient(IHttpClientFactory clients, IHttpContextAccessor accessor)
{
    public async Task<(T? Data, string? Error)> Send<T>(string service, string path, object? body = null, Guid? key = null)
    {
        var ctx = accessor.HttpContext!;
        var token = await ctx.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token) ||
            !Guid.TryParse(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.FirstOrDefault(c => c.Type == "conta_id")?.Value, out var conta))
            return (default, "A conta de caixa não está disponível nesta sessão.");

        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, $"/v1/contas/{conta}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (key is not null) request.Headers.Add("Idempotency-Key", key.ToString());
        if (body is not null) request.Content = JsonContent.Create(body, options: CaixaRules.Json);
        try
        {
            using var response = await clients.CreateClient(service).SendAsync(request, ctx.RequestAborted);
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<T>(CaixaRules.Json, ctx.RequestAborted), null);
            return (default, (int)response.StatusCode switch
            {
                400 => "Confira as datas e os dados informados.",
                401 => "A sessão expirou. Inicie uma nova sessão.",
                403 => "Seu perfil não tem acesso a esta operação.",
                409 => "Este envio já foi usado com outros dados. Abra um novo lançamento.",
                _ => "Serviço indisponível. Tente novamente."
            });
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            return (default, "Serviço indisponível. Tente novamente.");
        }
    }

    public static string Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public static string Real(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture).ToString("C", CaixaRules.PtBr);
    public static string Data(DateOnly value) => value.ToString("dd/MM/yyyy", CaixaRules.PtBr);
}
