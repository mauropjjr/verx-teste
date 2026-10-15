using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>Tradução do lançamento de domínio para o contrato de integração publicado ao Consolidado.</summary>
internal static class EventosLancamento
{
    public static LancamentoRegistradoV1 Registrado(Lancamento lancamento) => new(
        lancamento.Id,
        lancamento.ContaId,
        lancamento.Tipo == TipoLancamento.Credito ? TipoLancamentoV1.Credito : TipoLancamentoV1.Debito,
        lancamento.Valor,
        lancamento.OcorridoEm,
        lancamento.EstornoDe);
}
