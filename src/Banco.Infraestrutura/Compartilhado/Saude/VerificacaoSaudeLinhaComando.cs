namespace Banco.Infraestrutura.Compartilhado.Saude;

/// <summary>
/// Healthcheck de container sem curl/wget na imagem (as imagens <c>aspnet</c> não os trazem): o próprio binário,
/// chamado com <c>--verificar-saude</c>, faz <c>GET /health/live</c> na porta HTTP local e sai com 0 (saudável) ou
/// 1. No compose: <c>test: ["CMD", "dotnet", "Banco.Api.X.dll", "--verificar-saude"]</c>. Hosts sem HTTP (o Worker)
/// usam <see cref="ExecutarBatimentoSeSolicitado"/>, que confere o arquivo do <see cref="PublicadorBatimentoSaude"/>.
/// </summary>
public static class VerificacaoSaudeLinhaComando
{
    public const string Argumento = "--verificar-saude";

    /// <summary>Executa a verificação se o argumento foi passado; senão devolve <c>null</c> e o host sobe normalmente.</summary>
    public static async Task<int?> ExecutarSeSolicitadaAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!args.Contains(Argumento, StringComparer.Ordinal))
        {
            return null;
        }

        var portas = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        var porta = string.IsNullOrWhiteSpace(portas) ? "8080" : portas.Split([';', ','])[0].Trim();
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var resposta = await cliente.GetAsync(new Uri($"http://127.0.0.1:{porta}{ExtensoesVerificacoesSaude.RotaVivo}"));
            return resposta.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception erro) when (erro is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>
    /// Variante para host sem HTTP: com o argumento, sai com 0 se o batimento existe e tem no máximo
    /// <see cref="OpcoesBatimentoSaude.IdadeMaxima"/>, senão 1; sem o argumento devolve <c>null</c>.
    /// </summary>
    public static int? ExecutarBatimentoSeSolicitado(string[] args, OpcoesBatimentoSaude? opcoes = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!args.Contains(Argumento, StringComparer.Ordinal))
        {
            return null;
        }

        opcoes ??= new OpcoesBatimentoSaude();
        var arquivo = new FileInfo(opcoes.Arquivo);
        return arquivo.Exists && DateTime.UtcNow - arquivo.LastWriteTimeUtc <= opcoes.IdadeMaxima ? 0 : 1;
    }
}
