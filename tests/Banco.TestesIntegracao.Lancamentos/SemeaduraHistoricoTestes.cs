using Banco.Infraestrutura.Lancamentos.Inicializacao;
using Banco.TestesIntegracao.Lancamentos.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Banco.TestesIntegracao.Lancamentos;

// Spec platform-operations "Subida com um único comando" (dados de demonstração carregados automaticamente) e
// cenário "Reexecução preserva dados" (a carga não é duplicada); design D13: o histórico passa pelo caminho de
// domínio e gera o evento de outbox de cada lançamento, então o consolidado é populado pelo fluxo de produção.
[Collection(ColecaoLancamentos.Nome)]
public sealed class SemeaduraHistoricoTestes(PostgresLancamentos postgres)
{
    private const int DiasNoAno = 365;

    [Fact]
    public async Task Semeadura_executada_duas_vezes_cria_as_contas_e_um_ano_de_lancamentos_uma_unica_vez_com_um_evento_por_lancamento()
    {
        // Database próprio: o histórico das contas demo não se mistura com os dados dos demais testes da coleção.
        var stringConexao = new NpgsqlConnectionStringBuilder(postgres.StringConexao) { Database = "lancamentos_semeadura" }.ConnectionString;
        using var fabrica = new FabricaApiLancamentos(postgres, builder =>
        {
            builder.UseSetting("ConnectionStrings:Lancamentos", stringConexao);
            builder.UseSetting("Semeadura:HistoricoHabilitado", "true");
        });
        var agora = DateTimeOffset.UtcNow;

        // Primeira execução: na subida da Api, como no compose.
        var primeira = await ContarAsync(fabrica);

        // Segunda execução: o mesmo semeador de novo (como numa nova subida do ambiente).
        await using (var escopo = fabrica.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<SemeadorLancamentos>().SemearAsync(CancellationToken.None);
        }

        var segunda = await ContarAsync(fabrica);

        Assert.Equal(primeira, segunda);
        Assert.Equal(5, segunda.Contas);
        // 2 a 6 lançamentos por dia em cada uma das 5 contas, mais alguns estornos.
        Assert.InRange(segunda.Lancamentos, 5 * 2 * DiasNoAno, 5 * 7 * DiasNoAno);
        Assert.InRange(segunda.Estornos, 1, segunda.Lancamentos / 10);
        Assert.True(segunda.MenorQuantidadePorConta >= 2 * DiasNoAno, $"{segunda.MenorQuantidadePorConta} lançamentos na conta com menos");
        // Retroativos, distribuídos no último ano e nunca no futuro.
        Assert.InRange(segunda.Primeiro, agora.AddDays(-DiasNoAno - 1), agora.AddDays(-DiasNoAno + 7));
        Assert.InRange(segunda.Ultimo, agora.AddDays(-7), agora);
        Assert.True(segunda.DiasComMovimento >= DiasNoAno - 1, $"{segunda.DiasComMovimento} dias com movimento");
        // Um evento de outbox por lançamento, com o mesmo id (o message_id publicado ao consolidado).
        Assert.Equal(segunda.Lancamentos, segunda.MensagensOutbox);
        Assert.Equal(0, segunda.LancamentosSemEvento);
    }

    private static Task<Contagem> ContarAsync(FabricaApiLancamentos fabrica) =>
        fabrica.ExecutarNoBancoAsync(async c =>
        {
            var lancamentos = c.Lancamentos.AsNoTracking();
            return new Contagem(
                await c.Contas.CountAsync(),
                await lancamentos.CountAsync(),
                await lancamentos.CountAsync(l => l.EstornoDe != null),
                await lancamentos.GroupBy(l => l.ContaId).Select(g => g.Count()).MinAsync(),
                await lancamentos.MinAsync(l => l.OcorridoEm),
                await lancamentos.MaxAsync(l => l.OcorridoEm),
                await c.Database.SqlQueryRaw<int>(
                    "SELECT count(DISTINCT (ocorrido_em AT TIME ZONE 'UTC')::date)::int AS \"Value\" FROM lancamentos").SingleAsync(),
                await c.MensagensOutbox.CountAsync(),
                await lancamentos.CountAsync(l => !c.MensagensOutbox.Any(m => m.Id == l.Id)));
        });

    private sealed record Contagem(
        int Contas,
        int Lancamentos,
        int Estornos,
        int MenorQuantidadePorConta,
        DateTimeOffset Primeiro,
        DateTimeOffset Ultimo,
        int DiasComMovimento,
        int MensagensOutbox,
        int LancamentosSemEvento);
}
