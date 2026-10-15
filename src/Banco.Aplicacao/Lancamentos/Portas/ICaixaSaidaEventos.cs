using Banco.Aplicacao.Compartilhado.Contratos;

namespace Banco.Aplicacao.Lancamentos.Portas;

/// <summary>
/// Caixa de saída de eventos de integração (Transactional Outbox, design D5). <see cref="Adicionar"/> só acumula
/// o evento no escopo atual: ele é gravado pela <see cref="IUnidadeDeTrabalhoLancamentos"/> na mesma transação do
/// lançamento e publicado depois, de forma assíncrona. A implementação anexa o id de correlação da requisição
/// (spec platform-operations), que a Aplicacao não conhece.
/// </summary>
public interface ICaixaSaidaEventos
{
    void Adicionar(LancamentoRegistradoV1 evento);
}
