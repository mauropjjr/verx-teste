namespace Banco.Aplicacao.Consolidado.Portas;

/// <summary>Fila de mensagens mortas (DLQ) da projeção do consolidado (design D8).</summary>
public interface IFilaMensagensMortas
{
    /// <summary>
    /// Devolve até <paramref name="maximo"/> mensagens da DLQ ao fluxo principal, preservando o conteúdo e os metadados;
    /// cada uma só sai da DLQ depois de confirmada pelo broker na fila principal.
    /// </summary>
    /// <returns>Quantas foram reenviadas.</returns>
    Task<int> ReenviarAsync(int maximo, CancellationToken cancelamento);
}
