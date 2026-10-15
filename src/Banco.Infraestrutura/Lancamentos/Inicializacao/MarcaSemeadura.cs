namespace Banco.Infraestrutura.Lancamentos.Inicializacao;

/// <summary>
/// Linha de <c>semeaduras</c>: uma etapa de seed concluída. Gravada na mesma transação do último lote, faz as
/// subidas seguintes pularem a etapa sem reconsultar os lançamentos um a um.
/// </summary>
public sealed class MarcaSemeadura
{
    public required string Nome { get; set; }

    public DateTimeOffset ConcluidaEm { get; set; }
}
