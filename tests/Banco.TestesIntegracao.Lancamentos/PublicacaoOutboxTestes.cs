using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Infraestrutura.Compartilhado.Mensageria;
using Banco.TestesIntegracao.Lancamentos.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using static Banco.TestesIntegracao.Lancamentos.Infra.ExtensoesClienteLancamentos;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec ledger-entries, requisito "Publicação garantida de eventos" (cenário "Barramento indisponível no registro")
// e platform-operations "Rastreabilidade por correlação"; design D5 (PublicadorOutbox) e D6 (topologia).
[Collection(ColecaoLancamentos.Nome)]
public sealed class PublicacaoOutboxTestes(PostgresLancamentos postgres)
{
    private const string Usuario = "banco";
    private const string Senha = "banco_teste";

    [Fact]
    public async Task Com_o_broker_fora_o_registro_retorna_201_a_api_segue_pronta_a_mensagem_fica_pendente_e_e_publicada_com_message_id_e_correlacao_quando_o_broker_sobe()
    {
        // Porta fixa escolhida antes: a API já aponta para ela enquanto o container ainda não existe.
        var porta = PortaLivre();
        await using var rabbit = new RabbitMqBuilder("rabbitmq:4-management")
            .WithUsername(Usuario).WithPassword(Senha)
            .WithPortBinding(porta, RabbitMqBuilder.RabbitMqPort)
            .Build();
        using var fabrica = new FabricaApiLancamentos(postgres, builder =>
        {
            builder.UseSetting("RabbitMq:Porta", porta.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RabbitMq:Usuario", Usuario);
            builder.UseSetting("RabbitMq:Senha", Senha);
            builder.UseSetting("Outbox:IntervaloSondagem", "00:00:00.200");
            builder.UseSetting("Outbox:EsperaMaximaFalha", "00:00:01");
        });
        using var operador = fabrica.ClienteComPapel("operador");
        operador.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-broker-fora");
        var conta = await fabrica.CriarContaAsync();

        using var resposta = await operador.RegistrarAsync(conta, new { tipo = "CREDITO", valor = 12.34m, descricao = "Depósito" }, NovaChave());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var id = (await resposta.LerJsonAsync()).GetProperty("id").GetGuid();
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Null(await PublicadoEmAsync(fabrica, id));
        // O broker não entra no /health/ready: a Api segue pronta para registrar lançamentos.
        using var prontidao = await operador.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, prontidao.StatusCode);

        await rabbit.StartAsync();

        var limite = DateTime.UtcNow.AddSeconds(60);
        while (await PublicadoEmAsync(fabrica, id) is null && DateTime.UtcNow < limite)
        {
            await Task.Delay(250);
        }

        Assert.NotNull(await PublicadoEmAsync(fabrica, id));
        var fabricaConexao = new ConnectionFactory { Uri = new Uri(rabbit.GetConnectionString()) };
        await using var conexao = await fabricaConexao.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync();
        // A topologia D6 existe e redeclará-la com os mesmos argumentos é idempotente.
        await TopologiaMensageria.DeclararAsync(canal, CancellationToken.None);
        await canal.QueueDeclarePassiveAsync(TopologiaMensageria.FilaDlqLancamentoRegistrado);

        var mensagem = await BuscarNaFilaAsync(canal, id.ToString());
        Assert.Equal("lancamentos.eventos", mensagem.Exchange);
        Assert.Equal("lancamento.registrado.v1", mensagem.RoutingKey);
        Assert.Equal("application/json", mensagem.BasicProperties.ContentType);
        Assert.Equal(DeliveryModes.Persistent, mensagem.BasicProperties.DeliveryMode);
        Assert.Equal("corr-broker-fora", Encoding.UTF8.GetString((byte[])mensagem.BasicProperties.Headers!["x-correlation-id"]!));
        var evento = JsonSerializer.Deserialize<LancamentoRegistradoV1>(mensagem.Body.Span, LancamentoRegistradoV1.OpcoesJson)!;
        Assert.Equal((id, conta, 12.34m), (evento.IdLancamento, evento.ContaId, evento.Valor));
    }

    private static Task<DateTimeOffset?> PublicadoEmAsync(FabricaApiLancamentos fabrica, Guid id) =>
        fabrica.ExecutarNoBancoAsync(c => c.MensagensOutbox.AsNoTracking().Where(m => m.Id == id).Select(m => m.PublicadoEm).SingleAsync());

    /// <summary>A fila pode trazer eventos pendentes de outros testes (o Postgres é compartilhado): procura pelo message_id.</summary>
    private static async Task<BasicGetResult> BuscarNaFilaAsync(IChannel canal, string idMensagem)
    {
        while (await canal.BasicGetAsync(TopologiaMensageria.FilaLancamentoRegistrado, autoAck: true) is { } mensagem)
        {
            if (mensagem.BasicProperties.MessageId == idMensagem)
            {
                return mensagem;
            }
        }

        throw new Xunit.Sdk.XunitException($"Mensagem {idMensagem} não encontrada na fila.");
    }

    private static int PortaLivre()
    {
        using var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        return ((IPEndPoint)ouvinte.LocalEndpoint).Port;
    }
}
