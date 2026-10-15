namespace Banco.Dominio.Lancamentos;

/// <summary>Só contas ativas recebem lançamentos (spec ledger-entries "Registrar lançamento").</summary>
public enum SituacaoConta
{
    Ativa,
    Inativa,
}
