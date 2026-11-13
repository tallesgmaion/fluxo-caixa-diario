namespace CaixaWeb;

public sealed class ConsultaCaixa
{
    public DateOnly? Inicio { get; set; }
    public DateOnly? Fim { get; set; }
    public int? Limite { get; set; }
    public int Pagina { get; set; } = 1;
    public string? Direcao { get; set; }
    public string? CursorAnterior { get; set; }
    public string? ProximoCursor { get; set; }
}
