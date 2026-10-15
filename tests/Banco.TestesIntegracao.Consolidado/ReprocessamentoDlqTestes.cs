using System.Net;
using System.Text;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Banco.TestesIntegracao.Consolidado.Infra;
using RabbitMQ.Client;
using static Banco.TestesIntegracao.Consolidado.Infra.FabricaApiConsolidado;

namespace Banco.TestesIntegracao.Consolidado;

// Spec balance-consolidation "Reprocessar DLQ" e access-control "Operador tenta reprocessar DLQ"; design D8.
[Collection(ColecaoConsolidado.Nome)]
public sealed class ReprocessamentoDlqTestes(InfraConsolidado infra)
{
    [Fact]
    public async Task Admin_move_ate_N_mensagens_da_DLQ_para_a_fila_principal_preservando_id_e_correlacao_e_operador_recebe_403()
    {
        await using var conexao = await new ConnectionFactory { Uri = new Uri(infra.StringConexaoRabbit) }.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync(new CreateChannelOptions(true, true));
        await TopologiaMensageria.DeclararAsync(canal, CancellationToken.None);
        // A coleção roda em sequência: começa com as filas vazias (outros testes deixam mensagens na DLQ).
        await canal.QueuePurgeAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado);
        await canal.QueuePurgeAsync(TopologiaMensageria.FilaLancamentoRegistrado);
        var ids = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        for (var i = 0; i < ids.Length; i++)
        {
            await MandarParaDlqAsync(canal, ids[i], naDlq: (uint)i + 1);
        }

        await using var api = new FabricaApiConsolidado(infra);
        const string rota = "/api/v1/admin/dlq/reprocessar";

        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClienteComPapel("operador").PostAsync(rota, content: null)).StatusCode);
        var admin = api.ClienteComPapel("admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{rota}?maximo=0", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{rota}?maximo=1001", content: null)).StatusCode);
        Assert.Equal(3u, await canal.MessageCountAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado));

        var primeira = await admin.PostAsync($"{rota}?maximo=2", content: null);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(2, (await LerJsonAsync(primeira)).GetProperty("reenviadas").GetInt32());
        Assert.Equal(1u, await canal.MessageCountAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado));

        var segunda = await admin.PostAsync(rota, content: null); // padrão: até 100
        Assert.Equal(1, (await LerJsonAsync(segunda)).GetProperty("reenviadas").GetInt32());
        Assert.Equal(0u, await canal.MessageCountAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado));

        // De volta à fila principal (pela exchange e routing key originais), com message_id, correlação e persistência.
        var devolvidas = new List<IReadOnlyBasicProperties>();
        while (await canal.BasicGetAsync(TopologiaMensageria.FilaLancamentoRegistrado, autoAck: true) is { } mensagem)
        {
            Assert.Equal(TopologiaMensageria.ExchangeLancamentos, mensagem.Exchange);
            Assert.Equal(TopologiaMensageria.RoutingKeyLancamentoRegistrado, mensagem.RoutingKey);
            Assert.Equal($"corpo-{mensagem.BasicProperties.MessageId}", Encoding.UTF8.GetString(mensagem.Body.Span));
            devolvidas.Add(mensagem.BasicProperties);
        }

        Assert.Equal(ids.Order(), devolvidas.Select(p => p.MessageId).Order());
        Assert.All(devolvidas, p =>
        {
            Assert.Equal(DeliveryModes.Persistent, p.DeliveryMode);
            Assert.Equal($"corr-{p.MessageId}", Encoding.UTF8.GetString((byte[])p.Headers![TopologiaMensageria.CabecalhoCorrelacao]!));
        });
    }

    /// <summary>Publica na exchange principal e rejeita sem requeue: o broker a leva para a DLQ, como faz o Worker.</summary>
    private static async Task MandarParaDlqAsync(IChannel canal, string id, uint naDlq)
    {
        var propriedades = new BasicProperties
        {
            MessageId = id,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?> { [TopologiaMensageria.CabecalhoCorrelacao] = $"corr-{id}" },
        };
        await canal.BasicPublishAsync(
            TopologiaMensageria.ExchangeLancamentos,
            TopologiaMensageria.RoutingKeyLancamentoRegistrado,
            mandatory: true,
            propriedades,
            Encoding.UTF8.GetBytes($"corpo-{id}"));
        var mensagem = await canal.BasicGetAsync(TopologiaMensageria.FilaLancamentoRegistrado, autoAck: false);
        Assert.NotNull(mensagem);
        await canal.BasicNackAsync(mensagem.DeliveryTag, multiple: false, requeue: false);

        var limite = DateTime.UtcNow.AddSeconds(10);
        while (await canal.MessageCountAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado) < naDlq)
        {
            Assert.True(DateTime.UtcNow < limite, "A mensagem não chegou à DLQ.");
            await Task.Delay(50);
        }
    }
}
