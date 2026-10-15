using System.Diagnostics;

namespace Banco.TestesSistema.Infra;

/// <summary>
/// Para e sobe serviços do compose em execução (<c>docker compose -p &lt;projeto&gt; stop|start</c>). Pelo nome do
/// projeto, sem precisar do arquivo: funciona de qualquer diretório.
/// </summary>
internal static class ComposeDocker
{
    public static Task PararAsync(params string[] servicos) => ExecutarAsync("stop", servicos);

    public static Task IniciarAsync(params string[] servicos) => ExecutarAsync("start", servicos);

    private static async Task ExecutarAsync(string comando, string[] servicos)
    {
        var inicio = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argumento in new[] { "compose", "-p", AmbienteSistema.ProjetoCompose, comando }.Concat(servicos))
        {
            inicio.ArgumentList.Add(argumento);
        }

        using var processo = Process.Start(inicio) ?? throw new InvalidOperationException("Não foi possível executar o docker.");
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erros = processo.StandardError.ReadToEndAsync();
        using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await processo.WaitForExitAsync(limite.Token);
        if (processo.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"docker compose {comando} {string.Join(' ', servicos)} saiu com {processo.ExitCode}: {await saida}{await erros}");
        }
    }
}
