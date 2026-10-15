namespace Banco.Dominio.Lancamentos;

/// <summary>Natureza do lançamento (design D3): o valor é sempre positivo e o tipo diz se entra ou sai.</summary>
public enum TipoLancamento
{
    Credito,
    Debito,
}
