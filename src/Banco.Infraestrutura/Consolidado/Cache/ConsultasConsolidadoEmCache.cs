using System.Globalization;
using System.Text.Json;
using Banco.Aplicacao.Consolidado.Portas;
using Banco.Infraestrutura.Consolidado.Persistencia;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Banco.Infraestrutura.Consolidado.Cache;

/// <summary>
/// Cache-aside no Redis sobre <see cref="ConsultasConsolidado"/> (design D9). A chave leva a versão da conta
/// (<c>versao:{conta}</c>, incrementada pelo Worker a cada projeção), então dado novo muda a chave e o que estava em
/// cache simplesmente deixa de ser lido — sem <c>SCAN</c>/<c>DEL</c>. TTL de 10 min quando a consulta só envolve dias
/// passados e de 5 s quando envolve o dia de hoje (UTC). Redis fora ⇒ <see cref="CircuitoCacheConsolidado"/> devolve
/// nada e a resposta vem do Postgres.
/// </summary>
public sealed partial class ConsultasConsolidadoEmCache(
    ConsultasConsolidado banco,
    IConnectionMultiplexer redis,
    CircuitoCacheConsolidado circuito,
    TimeProvider relogio,
    ILogger<ConsultasConsolidadoEmCache> logger) : IConsultasConsolidado
{
    public static readonly TimeSpan TtlDiasPassados = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TtlComHoje = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public Task<ConsolidadoPeriodo> ConsultarPeriodoAsync(string contaId, DateOnly de, DateOnly ate, CancellationToken cancelamento) =>
        ObterOuConsultarAsync(
            contaId,
            versao => $"consolidado:{contaId}:{versao}:{Data(de)}:{Data(ate)}",
            ultimoDia: ate,
            () => banco.ConsultarPeriodoAsync(contaId, de, ate, cancelamento),
            cancelamento);

    public Task<SaldoEmInstante> ConsultarSaldoAsync(string contaId, DateTimeOffset em, CancellationToken cancelamento)
    {
        var emUtc = em.ToUniversalTime();
        return ObterOuConsultarAsync(
            contaId,
            versao => $"saldo:{contaId}:{versao}:{emUtc.ToString("O", CultureInfo.InvariantCulture)}",
            ultimoDia: DateOnly.FromDateTime(emUtc.UtcDateTime),
            () => banco.ConsultarSaldoAsync(contaId, emUtc, cancelamento),
            cancelamento);
    }

    private async Task<T> ObterOuConsultarAsync<T>(
        string contaId,
        Func<long, string> montarChave,
        DateOnly ultimoDia,
        Func<Task<T>> consultarBanco,
        CancellationToken cancelamento)
        where T : class
    {
        var bancoRedis = redis.GetDatabase();
        var emCache = await circuito.TentarAsync(
            async _ =>
            {
                var versao = await bancoRedis.StringGetAsync(ChavesCacheConsolidado.Versao(contaId));
                var chave = montarChave(versao.TryParse(out long numero) ? numero : 0);
                return new ValorEmCache(chave, await bancoRedis.StringGetAsync(chave));
            },
            cancelamento);

        if (emCache is { Valor.HasValue: true })
        {
            LogAcerto(logger, emCache.Chave);
            return JsonSerializer.Deserialize<T>((string)emCache.Valor!, _json)!;
        }

        var resultado = await consultarBanco();
        if (emCache is not null)
        {
            var hoje = DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime);
            var ttl = ultimoDia < hoje ? TtlDiasPassados : TtlComHoje;
            var json = JsonSerializer.Serialize(resultado, _json);
            await circuito.TentarAsync(
                async _ => (object)await bancoRedis.StringSetAsync(emCache.Chave, json, ttl),
                cancelamento);
        }

        return resultado;
    }

    private static string Data(DateOnly dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record ValorEmCache(string Chave, RedisValue Valor);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consulta servida do cache: {Chave}")]
    private static partial void LogAcerto(ILogger logger, string chave);
}
