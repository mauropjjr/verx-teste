namespace Banco.Aplicacao.Identidade.Portas;

/// <summary>Confirma, numa única transação, as alterações feitas pelos repositórios do contexto (design D2.1).</summary>
public interface IUnidadeDeTrabalhoIdentidade
{
    Task ConfirmarAsync(CancellationToken cancelamento);
}
