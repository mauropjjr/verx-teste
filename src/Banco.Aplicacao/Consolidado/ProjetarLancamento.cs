using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Aplicacao.Consolidado.Portas;
using Banco.Dominio.Consolidado;

namespace Banco.Aplicacao.Consolidado;

/// <summary>
/// Caso de uso "Projetar lançamento" (spec balance-consolidation, design D7): aplica um
/// <see cref="LancamentoRegistradoV1"/> ao consolidado exatamente uma vez. Numa única transação: registra a mensagem
/// no inbox (já registrada ⇒ duplicata, nada muda), soma os deltas do dia, guarda o movimento e avança a marca de
/// atualização. Como só há somas comutativas e um máximo, a ordem de chegada não importa. Depois de confirmar,
/// sinaliza a nova versão da conta para o cache (D9).
/// </summary>
public sealed class ProjetarLancamento(
    IUnidadeDeTrabalhoConsolidado unidadeDeTrabalho,
    IRegistroMensagensProcessadas mensagensProcessadas,
    IRepositorioResumosDiarios resumos,
    ISinalizadorVersaoConta sinalizadorVersao,
    TimeProvider relogio)
{
    /// <exception cref="EventoInvalidoException">O evento não respeita o contrato (não adianta tentar de novo).</exception>
    public async Task<ResultadoProjecao> ExecutarAsync(LancamentoRegistradoV1 evento, CancellationToken cancelamento)
    {
        var movimento = ParaMovimento(evento);

        var resultado = ResultadoProjecao.Duplicado;
        await using (var transacao = await unidadeDeTrabalho.IniciarAsync(cancelamento))
        {
            if (await mensagensProcessadas.RegistrarAsync(movimento.Id, cancelamento))
            {
                await resumos.SomarAsync(ResumoDiario.DoMovimento(movimento), cancelamento);
                await resumos.RegistrarMovimentoAsync(movimento, cancelamento);
                await resumos.AvancarMarcaAsync(movimento.ContaId, movimento.OcorridoEm, relogio.GetUtcNow(), cancelamento);
                await transacao.ConfirmarAsync(cancelamento);
                resultado = ResultadoProjecao.Projetado;
            }
        }

        // Também na duplicata: se a entrega anterior caiu entre o COMMIT e o cache, a versão ainda sobe.
        await sinalizadorVersao.IncrementarAsync(movimento.ContaId, cancelamento);
        return resultado;
    }

    private static Movimento ParaMovimento(LancamentoRegistradoV1? evento)
    {
        if (evento is null)
        {
            throw new EventoInvalidoException("Evento ausente.");
        }

        var natureza = evento.Tipo switch
        {
            TipoLancamentoV1.Credito => NaturezaMovimento.Credito,
            TipoLancamentoV1.Debito => NaturezaMovimento.Debito,
            _ => throw new EventoInvalidoException($"Tipo de lançamento desconhecido: {evento.Tipo}."),
        };

        try
        {
            return Movimento.Criar(evento.IdLancamento, evento.ContaId, natureza, evento.Valor, evento.OcorridoEm);
        }
        catch (ArgumentException erro)
        {
            throw new EventoInvalidoException($"Evento {evento.IdLancamento} inválido: {erro.Message}", erro);
        }
    }
}

public enum ResultadoProjecao
{
    Projetado,

    /// <summary>A mensagem já tinha sido processada; nada mudou.</summary>
    Duplicado,
}

/// <summary>Evento que viola o contrato <see cref="LancamentoRegistradoV1"/> — falha não transitória.</summary>
public sealed class EventoInvalidoException : Exception
{
    public EventoInvalidoException()
    {
    }

    public EventoInvalidoException(string message)
        : base(message)
    {
    }

    public EventoInvalidoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
