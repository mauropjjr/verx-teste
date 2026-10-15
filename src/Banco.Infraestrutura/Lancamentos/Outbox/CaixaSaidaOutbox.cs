using System.Text.Json;
using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Aplicacao.Lancamentos.Portas;
using Banco.Infraestrutura.Compartilhado.Correlacao;
using Banco.Infraestrutura.Lancamentos.Persistencia;
using Microsoft.AspNetCore.Http;

namespace Banco.Infraestrutura.Lancamentos.Outbox;

/// <summary>
/// Adiciona o evento ao <see cref="LancamentosDbContext"/> como <see cref="MensagemOutbox"/> pendente; quem grava é
/// a unidade de trabalho, na mesma transação do lançamento (design D5). O id da mensagem é o id do lançamento:
/// cada lançamento gera exatamente um evento, a PK impede duplicar e o mesmo valor vira o <c>message_id</c> no
/// RabbitMQ (D6), pelo qual o consumidor deduplica. A correlação vem da requisição HTTP atual, quando houver
/// (fora de requisição — ex.: seed — fica nula).
/// </summary>
public sealed class CaixaSaidaOutbox(LancamentosDbContext contexto, IHttpContextAccessor acessorHttp) : ICaixaSaidaEventos
{
    public void Adicionar(LancamentoRegistradoV1 evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        contexto.MensagensOutbox.Add(new MensagemOutbox
        {
            Id = evento.IdLancamento,
            Tipo = LancamentoRegistradoV1.NomeEvento,
            Conteudo = JsonSerializer.Serialize(evento, LancamentoRegistradoV1.OpcoesJson),
            IdCorrelacao = acessorHttp.HttpContext?.ObterIdCorrelacao(),
            OcorridoEm = evento.OcorridoEm,
        });
    }
}
