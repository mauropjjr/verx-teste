using Banco.Infraestrutura.Lancamentos.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Banco.Infraestrutura.Lancamentos.Inicializacao;

/// <summary>
/// Aplica as migrations e o seed na subida da Api (design D13), com novas tentativas (backoff exponencial até
/// 10 s) enquanto o Postgres ainda não aceita conexões. Ambiente de avaliação; em produção seria um
/// <i>migration job</i>.
/// </summary>
public static partial class InicializadorBancoLancamentos
{
    private static readonly TimeSpan _esperaMaxima = TimeSpan.FromSeconds(10);

    public static async Task InicializarBancoLancamentosAsync(
        this IServiceProvider servicos,
        int tentativas = 10,
        CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        var logger = servicos.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InicializadorBancoLancamentos).FullName!);
        var espera = TimeSpan.FromSeconds(1);

        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                await using var escopo = servicos.CreateAsyncScope();
                await escopo.ServiceProvider.GetRequiredService<LancamentosDbContext>().Database.MigrateAsync(cancelamento);
                await escopo.ServiceProvider.GetRequiredService<SemeadorLancamentos>().SemearAsync(cancelamento);
                LogInicializado(logger, tentativa);
                return;
            }
            catch (Exception erro) when (tentativa < tentativas && !cancelamento.IsCancellationRequested)
            {
                LogFalhaTentativa(logger, erro, tentativa, tentativas, espera);
                await Task.Delay(espera, cancelamento);
                espera = espera * 2 > _esperaMaxima ? _esperaMaxima : espera * 2;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Banco de lançamentos migrado e semeado (tentativa {Tentativa})")]
    private static partial void LogInicializado(ILogger logger, int tentativa);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao inicializar o banco de lançamentos (tentativa {Tentativa}/{Tentativas}); nova tentativa em {Espera}")]
    private static partial void LogFalhaTentativa(ILogger logger, Exception erro, int tentativa, int tentativas, TimeSpan espera);
}
