using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Banco.Infraestrutura.Compartilhado.Saude;

/// <summary>Batimento de saúde de um host sem HTTP (o Worker): onde gravar, de quanto em quanto tempo e até que idade vale.</summary>
public sealed class OpcoesBatimentoSaude
{
    public string Arquivo { get; set; } = Path.Combine(Path.GetTempPath(), "banco-batimento-saude");

    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Batimento mais velho que isto ⇒ não saudável (o host travou ou uma dependência caiu).</summary>
    public TimeSpan IdadeMaxima { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Healthcheck de container para host sem porta HTTP (design D14): a cada <see cref="OpcoesBatimentoSaude.Intervalo"/>
/// executa as mesmas verificações registradas que o <c>/health/ready</c> das Apis e, se todas passam, regrava o arquivo
/// de batimento. <see cref="VerificacaoSaudeLinhaComando.ExecutarBatimentoSeSolicitado"/> (o <c>--verificar-saude</c>)
/// só confere a idade do arquivo — sem abrir porta só para isso e sem depender de curl na imagem.
/// </summary>
public sealed partial class PublicadorBatimentoSaude(
    HealthCheckService verificacoes,
    OpcoesBatimentoSaude opcoes,
    ILogger<PublicadorBatimentoSaude> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(opcoes.Intervalo);
        do
        {
            var relatorio = await verificacoes.CheckHealthAsync(stoppingToken);
            if (relatorio.Status == HealthStatus.Healthy)
            {
                try
                {
                    await File.WriteAllTextAsync(opcoes.Arquivo, DateTimeOffset.UtcNow.ToString("O"), stoppingToken);
                }
                catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
                {
                    LogFalhaGravacao(logger, erro, opcoes.Arquivo);
                }
            }
            else
            {
                var falhas = string.Join(", ", relatorio.Entries.Where(e => e.Value.Status != HealthStatus.Healthy).Select(e => e.Key));
                LogNaoSaudavel(logger, falhas);
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verificação de saúde falhou ({Componentes}); batimento não renovado")]
    private static partial void LogNaoSaudavel(ILogger logger, string componentes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Não foi possível gravar o batimento de saúde em {Arquivo}")]
    private static partial void LogFalhaGravacao(ILogger logger, Exception erro, string arquivo);
}

public static class ExtensoesBatimentoSaude
{
    /// <summary>Registra os health checks e o <see cref="PublicadorBatimentoSaude"/>; o host adiciona as dependências.</summary>
    public static IHealthChecksBuilder AdicionarBatimentoSaude(this IServiceCollection servicos, OpcoesBatimentoSaude? opcoes = null)
    {
        servicos.AddSingleton(opcoes ?? new OpcoesBatimentoSaude());
        servicos.AddHostedService<PublicadorBatimentoSaude>();
        return servicos.AddHealthChecks();
    }
}
