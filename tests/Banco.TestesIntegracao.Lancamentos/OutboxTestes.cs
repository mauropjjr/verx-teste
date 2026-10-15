using System.Net;
using System.Text.Json;
using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.TestesIntegracao.Lancamentos.Infra;
using Microsoft.EntityFrameworkCore;
using static Banco.TestesIntegracao.Lancamentos.Infra.ExtensoesClienteLancamentos;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec ledger-entries, requisito "Publicação garantida de eventos" (design D5: outbox na mesma transação do
// lançamento) e platform-operations "Rastreabilidade por correlação" (id de correlação gravado com o evento).
[Collection(ColecaoLancamentos.Nome)]
public sealed class OutboxTestes : IDisposable
{
    private readonly FabricaApiLancamentos _fabrica;
    private readonly HttpClient _operador;

    public OutboxTestes(PostgresLancamentos postgres)
    {
        _fabrica = new FabricaApiLancamentos(postgres);
        _operador = _fabrica.ClienteComPapel("operador");
    }

    public void Dispose()
    {
        _operador.Dispose();
        _fabrica.Dispose();
    }

    [Fact]
    public async Task Registro_e_estorno_gravam_LancamentoRegistradoV1_no_outbox_com_a_correlacao_e_o_reenvio_nao_duplica_a_mensagem()
    {
        var conta = await _fabrica.CriarContaAsync();
        var chave = NovaChave();
        var corpo = new { tipo = "CREDITO", valor = 42.50m, descricao = "Depósito" };
        _operador.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-outbox-1");

        using var registro = await _operador.RegistrarAsync(conta, corpo, chave);
        using var reenvio = await _operador.RegistrarAsync(conta, corpo, chave);
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reenvio.StatusCode);
        var lancamento = await registro.LerJsonAsync();
        var idLancamento = lancamento.GetProperty("id").GetGuid();
        using var estorno = await _operador.EstornarAsync(conta, idLancamento, NovaChave());
        Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
        var idEstorno = (await estorno.LerJsonAsync()).GetProperty("id").GetGuid();

        var mensagens = await _fabrica.ExecutarNoBancoAsync(c => c.MensagensOutbox.AsNoTracking()
            .Where(m => m.Id == idLancamento || m.Id == idEstorno).OrderBy(m => m.OcorridoEm).ToListAsync());

        Assert.Equal(2, mensagens.Count);
        Assert.All(mensagens, m =>
        {
            Assert.Equal("lancamento.registrado.v1", m.Tipo);
            Assert.Equal("corr-outbox-1", m.IdCorrelacao);
            Assert.Equal(0, m.Tentativas);
        });
        var eventoRegistro = JsonSerializer.Deserialize<LancamentoRegistradoV1>(mensagens[0].Conteudo, LancamentoRegistradoV1.OpcoesJson);
        Assert.Equal(
            new LancamentoRegistradoV1(idLancamento, conta, TipoLancamentoV1.Credito, 42.50m, lancamento.GetProperty("ocorridoEm").GetDateTimeOffset(), null),
            eventoRegistro);
        var eventoEstorno = JsonSerializer.Deserialize<LancamentoRegistradoV1>(mensagens[1].Conteudo, LancamentoRegistradoV1.OpcoesJson);
        Assert.Equal((idEstorno, TipoLancamentoV1.Debito, 42.50m, (Guid?)idLancamento),
            (eventoEstorno!.IdLancamento, eventoEstorno.Tipo, eventoEstorno.Valor, eventoEstorno.EstornoDe));
    }

    [Fact]
    public async Task Falha_forcada_depois_dos_inserts_nao_deixa_nem_o_lancamento_nem_a_mensagem_de_outbox()
    {
        var conta = await _fabrica.CriarContaAsync();
        // Gatilho adiado para o COMMIT: dispara depois que lançamento e outbox já foram inseridos na transação.
        // DDL não aceita parâmetros; o id da conta é gerado pelo próprio teste.
        var gatilho = $"""
            CREATE OR REPLACE FUNCTION falhar_teste_outbox() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'falha forçada pelo teste'; END $$;
            CREATE CONSTRAINT TRIGGER falha_{conta.Replace("-", "_", StringComparison.Ordinal)}
                AFTER INSERT ON lancamentos DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW WHEN (NEW.conta_id = '{conta}') EXECUTE FUNCTION falhar_teste_outbox();
            """;
        await _fabrica.ExecutarNoBancoAsync(c => c.Database.ExecuteSqlRawAsync(gatilho));

        using var resposta = await _operador.RegistrarAsync(conta, new { tipo = "DEBITO", valor = 10m, descricao = "x" }, NovaChave());

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.Equal(0, await _fabrica.ExecutarNoBancoAsync(c => c.Lancamentos.CountAsync(l => l.ContaId == conta)));
        Assert.Equal(0, await _fabrica.ExecutarNoBancoAsync(c => c.MensagensOutbox.CountAsync(
            m => EF.Functions.JsonContains(m.Conteudo, $$"""{"contaId":"{{conta}}"}"""))));
    }
}
