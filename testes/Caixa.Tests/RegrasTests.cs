using Caixa;
using Xunit;

public sealed class RegrasTests
{
    [Theory]
    [InlineData("0.00", false)]
    [InlineData("-10.00", false)]
    [InlineData("10.2", false)]
    [InlineData("10,25", false)]
    [InlineData("100000000000000000.00", false)]
    [InlineData("99999999999999999.99", true)]
    [InlineData("10.25", true)]
    public void ValorMonetario(string text, bool valid) =>
        Assert.Equal(valid, CaixaRules.ValorValido(text, out _));

    [Fact]
    public void UsaDiaCivilDeSaoPaulo()
    {
        var clock = new Relogio(new(2026, 11, 13, 1, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 12), CaixaRules.Hoje(clock));
    }

    [Fact]
    public void CalculaLiquidoNegativo()
    {
        var resumo = CaixaRules.Totais(new(2026, 11, 13), 20m, 50m, null);
        Assert.Equal("-30.00", resumo.MovimentoLiquido);
    }

    [Fact]
    public void NormalizaReenvio()
    {
        var a = new NovoLancamento(new(2026, 11, 13), "credito", "50.00", "BRL", "  Venda ");
        var b = a with { Descricao = "Venda" };
        Assert.Equal(CaixaRules.Hash(CaixaRules.Normalizar(a)), CaixaRules.Hash(CaixaRules.Normalizar(b)));
    }

    private sealed class Relogio(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
