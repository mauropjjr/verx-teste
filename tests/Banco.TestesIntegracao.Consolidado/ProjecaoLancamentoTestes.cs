using Banco.Aplicacao.Compartilhado.Contratos;
using Banco.Infraestrutura.Consolidado.Mensageria;
using Banco.TestesIntegracao.Consolidado.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.TestesIntegracao.Consolidado;

// Spec balance-consolidation: "Consolidar lançamentos por dia" e "Processamento idempotente de eventos"
// (evento duplicado, eventos fora de ordem); design D7 (inbox + projeção + marca na mesma transação, UPSERT).
[Collection(ColecaoConsolidado.Nome)]
public sealed class ProjecaoLancamentoTestes(InfraConsolidado infra)
{
    private static readonly DateTimeOffset _diaD = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Processar_lancamentos_atualiza_resumo_do_dia_lancamentos_projetados_e_marca_de_atualizacao()
    {
        await using var host = await HostConsolidado.CriarAsync(infra);
        var conta = HostConsolidado.NovaConta();
        var credito = Evento(conta, TipoLancamentoV1.Credito, 100.00m, _diaD);
        var debito = Evento(conta, TipoLancamentoV1.Debito, 30.00m, _diaD.AddHours(5));

        Assert.Equal(ResultadoProcessamento.Confirmar, await ProcessarAsync(host, credito));
        Assert.Equal(ResultadoProcessamento.Confirmar, await ProcessarAsync(host, debito));

        var resumo = await host.NoBancoAsync(c => c.ResumosDiarios.AsNoTracking().SingleAsync(r => r.ContaId == conta));
        Assert.Equal((DateOnly.FromDateTime(_diaD.UtcDateTime), 100.00m, 30.00m), (resumo.Dia, resumo.Creditos, resumo.Debitos));
        var projetados = await host.NoBancoAsync(c => c.LancamentosProjetados.AsNoTracking().Where(l => l.ContaId == conta).OrderBy(l => l.OcorridoEm).ToListAsync());
        Assert.Equal([(credito.IdLancamento, 100.00m), (debito.IdLancamento, 30.00m)], projetados.Select(p => (p.Id, p.Valor)));
        var marca = await host.NoBancoAsync(c => c.MarcasAtualizacao.AsNoTracking().SingleAsync(m => m.ContaId == conta));
        Assert.Equal(debito.OcorridoEm, marca.UltimoOcorridoEm);
    }

    [Fact]
    public async Task Evento_duplicado_e_confirmado_sem_alterar_o_consolidado()
    {
        await using var host = await HostConsolidado.CriarAsync(infra);
        var conta = HostConsolidado.NovaConta();
        var evento = Evento(conta, TipoLancamentoV1.Credito, 42.50m, _diaD);

        Assert.Equal(ResultadoProcessamento.Confirmar, await ProcessarAsync(host, evento));
        Assert.Equal(ResultadoProcessamento.Confirmar, await ProcessarAsync(host, evento));

        var resumo = await host.NoBancoAsync(c => c.ResumosDiarios.AsNoTracking().SingleAsync(r => r.ContaId == conta));
        Assert.Equal((42.50m, 0m), (resumo.Creditos, resumo.Debitos));
        Assert.Equal(1, await host.NoBancoAsync(c => c.LancamentosProjetados.CountAsync(l => l.ContaId == conta)));
        Assert.Equal(1, await host.NoBancoAsync(c => c.MensagensProcessadas.CountAsync(m => m.IdMensagem == evento.IdLancamento)));
    }

    [Fact]
    public async Task Eventos_fora_de_ordem_produzem_o_mesmo_consolidado_da_ordem_natural()
    {
        await using var host = await HostConsolidado.CriarAsync(infra);
        var contaNatural = HostConsolidado.NovaConta();
        var contaInvertida = HostConsolidado.NovaConta();

        LancamentoRegistradoV1[] Eventos(string conta) =>
        [
            Evento(conta, TipoLancamentoV1.Credito, 100.00m, _diaD),
            Evento(conta, TipoLancamentoV1.Debito, 100.00m, _diaD.AddDays(1)),
        ];

        foreach (var evento in Eventos(contaNatural))
        {
            await ProcessarAsync(host, evento);
        }

        foreach (var evento in Eventos(contaInvertida).Reverse())
        {
            await ProcessarAsync(host, evento);
        }

        async Task<(List<(DateOnly Dia, decimal Creditos, decimal Debitos)> Resumos, DateTimeOffset UltimoOcorridoEm)> EstadoAsync(string conta) => (
            (await host.NoBancoAsync(c => c.ResumosDiarios.AsNoTracking().Where(r => r.ContaId == conta).OrderBy(r => r.Dia).ToListAsync()))
                .Select(r => (r.Dia, r.Creditos, r.Debitos)).ToList(),
            (await host.NoBancoAsync(c => c.MarcasAtualizacao.AsNoTracking().SingleAsync(m => m.ContaId == conta))).UltimoOcorridoEm);

        var natural = await EstadoAsync(contaNatural);
        var invertida = await EstadoAsync(contaInvertida);
        Assert.Equal(natural.Resumos, invertida.Resumos);
        Assert.Equal(_diaD.AddDays(1), invertida.UltimoOcorridoEm);
        Assert.Equal(natural.UltimoOcorridoEm, invertida.UltimoOcorridoEm);
    }

    [Fact]
    public async Task Com_o_Redis_fora_a_projecao_e_gravada_e_a_mensagem_confirmada()
    {
        // Porta 1: nada escuta; o incremento da versão falha e só é registrado em log (design D9).
        await using var host = await HostConsolidado.CriarAsync(infra, configuracaoExtra: new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "127.0.0.1:1",
        });
        var conta = HostConsolidado.NovaConta();

        Assert.Equal(ResultadoProcessamento.Confirmar, await ProcessarAsync(host, Evento(conta, TipoLancamentoV1.Credito, 9.90m, _diaD)));

        Assert.Equal(9.90m, (await host.NoBancoAsync(c => c.ResumosDiarios.AsNoTracking().SingleAsync(r => r.ContaId == conta))).Creditos);
        Assert.Contains(host.Logs.Eventos, e => e.Level == Serilog.Events.LogEventLevel.Warning && e.Properties.ContainsKey("ContaId")
            && e.MessageTemplate.Text.Contains("Redis", StringComparison.Ordinal));
    }

    private static async Task<ResultadoProcessamento> ProcessarAsync(HostConsolidado host, LancamentoRegistradoV1 evento) =>
        await host.Servicos.GetRequiredService<ProcessadorMensagemLancamento>()
            .ProcessarAsync(HostConsolidado.Serializar(evento), CancellationToken.None);

    private static LancamentoRegistradoV1 Evento(string conta, TipoLancamentoV1 tipo, decimal valor, DateTimeOffset ocorridoEm) =>
        new(Guid.CreateVersion7(ocorridoEm), conta, tipo, valor, ocorridoEm, EstornoDe: null);
}
