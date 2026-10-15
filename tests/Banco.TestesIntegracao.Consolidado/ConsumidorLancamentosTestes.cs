using System.Text;
using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Aplicacao.Consolidado.Portas;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Banco.Infraestrutura.Consolidado.Persistencia;
using Banco.TestesIntegracao.Consolidado.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using RabbitMQ.Client;
using Serilog.Events;
using StackExchange.Redis;

namespace Banco.TestesIntegracao.Consolidado;

// Spec balance-consolidation "Retry, DLQ e reprocessamento" (falha transitória, mensagem envenenada) e
// platform-operations "Rastreabilidade por correlação"; design D6 (manual ack), D8 (Polly + DLQ) e D9 (versão no Redis).
[Collection(ColecaoConsolidado.Nome)]
public sealed class ConsumidorLancamentosTestes(InfraConsolidado infra)
{
    private static readonly TimeSpan _limite = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Mensagem_valida_e_projetada_confirmada_incrementa_a_versao_da_conta_no_Redis_e_loga_a_correlacao()
    {
        await using var host = await HostConsolidado.CriarAsync(infra);
        await host.IniciarAsync();
        var evento = Evento(HostConsolidado.NovaConta());

        await PublicarAsync(HostConsolidado.Serializar(evento), evento.IdLancamento.ToString(), "corr-worker-1");

        await AguardarProjecaoAsync(host, evento.ContaId);
        await host.Host.StopAsync(); // sem ack, a mensagem voltaria para a fila ao fechar o canal
        Assert.False(await ExisteNaFilaAsync(TopologiaMensageria.FilaLancamentoRegistrado, evento.IdLancamento.ToString()));
        Assert.False(await ExisteNaFilaAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado, evento.IdLancamento.ToString()));

        await using var redis = await ConnectionMultiplexer.ConnectAsync(infra.StringConexaoRedis);
        Assert.Equal(1, (long)await redis.GetDatabase().StringGetAsync($"versao:{evento.ContaId}"));

        Assert.Contains(host.Logs.Eventos, e =>
            e.Properties.TryGetValue("CorrelationId", out var valor) && valor is ScalarValue { Value: "corr-worker-1" }
            && e.Properties.TryGetValue("IdLancamento", out var id) && id.ToString() == evento.IdLancamento.ToString());
    }

    [Fact]
    public async Task Mensagem_malformada_vai_para_a_DLQ_sem_bloquear_as_seguintes()
    {
        await using var host = await HostConsolidado.CriarAsync(infra);
        await host.IniciarAsync();
        var idMalformada = Guid.NewGuid().ToString();
        var seguinte = Evento(HostConsolidado.NovaConta());

        await PublicarAsync(Encoding.UTF8.GetBytes("{ isto não é um evento"), idMalformada, "corr-veneno");
        await PublicarAsync(HostConsolidado.Serializar(seguinte), seguinte.IdLancamento.ToString(), "corr-seguinte");

        await AguardarProjecaoAsync(host, seguinte.ContaId);
        await host.Host.StopAsync();
        Assert.True(await ExisteNaFilaAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado, idMalformada));
        Assert.False(await ExisteNaFilaAsync(TopologiaMensageria.FilaLancamentoRegistrado, idMalformada));
    }

    [Fact]
    public async Task Falha_transitoria_de_banco_e_retentada_e_a_mensagem_e_projetada_sem_ir_para_a_DLQ()
    {
        var falhas = new ContadorFalhas(2);
        await using var host = await HostConsolidado.CriarAsync(infra, servicos =>
        {
            // Decorator determinístico: as 2 primeiras gravações no inbox falham como o Npgsql falha com o banco fora.
            servicos.RemoveAll<IRegistroMensagensProcessadas>();
            servicos.AddScoped<IRegistroMensagensProcessadas>(sp => new InboxQueFalha(
                ActivatorUtilities.CreateInstance<RegistroMensagensProcessadas>(sp), falhas));
        });
        await host.IniciarAsync();
        var evento = Evento(HostConsolidado.NovaConta());

        await PublicarAsync(HostConsolidado.Serializar(evento), evento.IdLancamento.ToString(), "corr-transitoria");

        await AguardarProjecaoAsync(host, evento.ContaId);
        await host.Host.StopAsync();
        Assert.Equal(3, falhas.Chamadas); // 2 falhas + a tentativa que deu certo
        var resumo = await host.NoBancoAsync(c => c.ResumosDiarios.AsNoTracking().SingleAsync(r => r.ContaId == evento.ContaId));
        Assert.Equal(evento.Valor, resumo.Creditos);
        Assert.False(await ExisteNaFilaAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado, evento.IdLancamento.ToString()));
        Assert.False(await ExisteNaFilaAsync(TopologiaMensageria.FilaLancamentoRegistrado, evento.IdLancamento.ToString()));
    }

    private static LancamentoRegistradoV1 Evento(string conta) =>
        new(Guid.CreateVersion7(), conta, TipoLancamentoV1.Credito, 25.10m, DateTimeOffset.UtcNow, EstornoDe: null);

    private static async Task AguardarProjecaoAsync(HostConsolidado host, string conta)
    {
        var limite = DateTime.UtcNow + _limite;
        while (!await host.NoBancoAsync(c => c.ResumosDiarios.AnyAsync(r => r.ContaId == conta)))
        {
            Assert.True(DateTime.UtcNow < limite, $"A conta {conta} não foi projetada em {_limite}.");
            await Task.Delay(100);
        }
    }

    private async Task PublicarAsync(byte[] corpo, string idMensagem, string idCorrelacao)
    {
        await using var conexao = await new ConnectionFactory { Uri = new Uri(infra.StringConexaoRabbit) }.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync(new CreateChannelOptions(true, true));
        await TopologiaMensageria.DeclararAsync(canal, CancellationToken.None);
        var propriedades = new BasicProperties
        {
            MessageId = idMensagem,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?> { [TopologiaMensageria.CabecalhoCorrelacao] = idCorrelacao },
        };
        await canal.BasicPublishAsync(
            TopologiaMensageria.ExchangeLancamentos, TopologiaMensageria.RoutingKeyLancamentoRegistrado, mandatory: true, propriedades, corpo);
    }

    /// <summary>Procura pelo message_id sem consumir: as mensagens lidas voltam para a fila (nack com requeue).</summary>
    private async Task<bool> ExisteNaFilaAsync(string fila, string idMensagem)
    {
        await using var conexao = await new ConnectionFactory { Uri = new Uri(infra.StringConexaoRabbit) }.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync();
        var achou = false;
        // Sem ack, cada BasicGet traz a próxima mensagem pronta; termina quando a fila se esgota para este canal.
        while (await canal.BasicGetAsync(fila, autoAck: false) is { } mensagem)
        {
            achou |= mensagem.BasicProperties.MessageId == idMensagem;
        }

        return achou; // ao fechar o canal, o que não foi confirmado volta para a fila
    }

    /// <summary>Faz as <paramref name="falhas"/> primeiras chamadas falharem e conta todas.</summary>
    private sealed class ContadorFalhas(int falhas)
    {
        private int _chamadas;

        public int Chamadas => Volatile.Read(ref _chamadas);

        public bool DeveFalhar() => Interlocked.Increment(ref _chamadas) <= falhas;
    }

    private sealed class InboxQueFalha(IRegistroMensagensProcessadas interno, ContadorFalhas falhas) : IRegistroMensagensProcessadas
    {
        public Task<bool> RegistrarAsync(Guid idMensagem, CancellationToken cancelamento) => falhas.DeveFalhar()
            ? throw new NpgsqlException("Falha simulada: banco indisponível", new TimeoutException())
            : interno.RegistrarAsync(idMensagem, cancelamento);
    }
}
