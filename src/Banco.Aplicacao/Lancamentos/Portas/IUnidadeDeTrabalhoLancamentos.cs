namespace Banco.Aplicacao.Lancamentos.Portas;

/// <summary>
/// Confirma, numa única transação, o que os repositórios do contexto acumularam (design D2.1) — é aqui que o
/// lançamento e a sua mensagem de outbox entram juntos (D5). As corridas que só o banco decide (UNIQUE da
/// chave de idempotência e do estorno, design D3/D4) voltam como <see cref="ResultadoConfirmacao"/>, não como
/// exceção; nesse caso nada foi gravado e as alterações pendentes são descartadas.
/// </summary>
public interface IUnidadeDeTrabalhoLancamentos
{
    Task<ResultadoConfirmacao> ConfirmarAsync(CancellationToken cancelamento);
}

public enum ResultadoConfirmacao
{
    Confirmado,

    /// <summary>Outra requisição gravou antes um lançamento com a mesma conta + <c>Idempotency-Key</c>.</summary>
    ChaveIdempotenciaDuplicada,

    /// <summary>Outra requisição estornou antes o mesmo lançamento.</summary>
    EstornoDuplicado,
}
