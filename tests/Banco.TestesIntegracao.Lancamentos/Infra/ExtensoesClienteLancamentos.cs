using System.Net.Http.Json;
using System.Text.Json;

namespace Banco.TestesIntegracao.Lancamentos.Infra;

/// <summary>Atalhos HTTP para as rotas de lançamentos.</summary>
internal static class ExtensoesClienteLancamentos
{
    public static Task<HttpResponseMessage> RegistrarAsync(this HttpClient cliente, string contaId, object corpo, string? chave) =>
        EnviarAsync(cliente, $"/api/v1/contas/{contaId}/lancamentos", JsonContent.Create(corpo), chave);

    public static Task<HttpResponseMessage> EstornarAsync(this HttpClient cliente, string contaId, Guid lancamentoId, string? chave) =>
        EnviarAsync(cliente, $"/api/v1/contas/{contaId}/lancamentos/{lancamentoId}/estorno", content: null, chave);

    public static async Task<JsonElement> LerJsonAsync(this HttpResponseMessage resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return documento.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string rota, HttpContent? content, string? chave)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, rota) { Content = content };
        if (chave is not null)
        {
            requisicao.Headers.Add("Idempotency-Key", chave);
        }

        return cliente.SendAsync(requisicao);
    }

    public static string NovaChave() => "chave-" + Guid.NewGuid().ToString("N");
}
