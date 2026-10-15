using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// Carga de lançamentos retroativos para o seed de demonstração (design D13). Usa o mesmo caminho de domínio do
/// registro e do estorno — <see cref="Lancamento.Registrar"/>/<see cref="Lancamento.Estornar"/>, o mesmo hash de
/// idempotência e o mesmo evento na caixa de saída (outbox, D5) —, de modo que o consolidado é populado pelo fluxo
/// de produção. Diferenças em relação a <see cref="RegistrarLancamento"/>: o instante vem do item (passado) e a
/// confirmação é feita em lotes, não por lançamento. Não é exposto pela Api: a data de um lançamento real
/// continua sendo sempre a do servidor.
/// </summary>
public sealed class RegistrarHistoricoLancamentos(
    IRepositorioContas contas,
    IRepositorioLancamentos lancamentos,
    ICaixaSaidaEventos caixaSaida,
    IUnidadeDeTrabalhoLancamentos unidadeDeTrabalho)
{
    /// <summary>Registra os itens na ordem dada, confirmando a cada <paramref name="lancamentosPorLote"/>; devolve quantos gravou.</summary>
    public async Task<int> ExecutarAsync(IEnumerable<LancamentoHistorico> itens, int lancamentosPorLote, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(itens);
        ArgumentOutOfRangeException.ThrowIfLessThan(lancamentosPorLote, 1);
        var contasValidadas = new HashSet<string>(StringComparer.Ordinal);
        var registrados = new Dictionary<string, Lancamento>(StringComparer.Ordinal);
        var pendentes = 0;

        foreach (var item in itens)
        {
            if (contasValidadas.Add(item.ContaId) && await contas.ObterAsync(item.ContaId, cancelamento) is not { EstaAtiva: true })
            {
                throw new InvalidOperationException($"Conta {item.ContaId} inexistente ou inativa para o histórico.");
            }

            var lancamento = item.ChaveOriginal is null
                ? Lancamento.Registrar(
                    item.ContaId, item.Tipo, item.Valor, item.Descricao, item.OcorridoEm, item.ChaveIdempotencia,
                    HashRequisicao.DoRegistro(item.Tipo, item.Valor, item.Descricao))
                : Estornar(registrados[item.ChaveOriginal], item);

            lancamentos.Adicionar(lancamento);
            caixaSaida.Adicionar(EventosLancamento.Registrado(lancamento));
            registrados[item.ChaveIdempotencia] = lancamento;

            if (++pendentes == lancamentosPorLote)
            {
                await ConfirmarAsync(cancelamento);
                pendentes = 0;
            }
        }

        await ConfirmarAsync(cancelamento);
        return registrados.Count;
    }

    private static Lancamento Estornar(Lancamento original, LancamentoHistorico item) =>
        original.Estornar(item.OcorridoEm, item.ChaveIdempotencia, HashRequisicao.DoEstorno(original.Id));

    private async Task ConfirmarAsync(CancellationToken cancelamento)
    {
        var resultado = await unidadeDeTrabalho.ConfirmarAsync(cancelamento);
        if (resultado != ResultadoConfirmacao.Confirmado)
        {
            throw new InvalidOperationException($"Falha ao gravar o histórico de lançamentos: {resultado}.");
        }
    }
}

/// <summary>
/// Um lançamento do histórico: registro (<see cref="ChaveOriginal"/> nula) ou estorno do lançamento registrado
/// antes, no mesmo histórico, com a chave <see cref="ChaveOriginal"/> (aí tipo, valor e descrição são ignorados).
/// </summary>
public sealed record LancamentoHistorico(
    string ContaId,
    string ChaveIdempotencia,
    DateTimeOffset OcorridoEm,
    TipoLancamento Tipo,
    decimal Valor,
    string Descricao,
    string? ChaveOriginal = null);
