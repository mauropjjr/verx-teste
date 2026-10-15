using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Banco.Dominio.Lancamentos;

namespace Banco.Aplicacao.Lancamentos;

/// <summary>
/// <c>hash_requisicao</c> (design D4): SHA-256, em hexadecimal minúsculo, do conteúdo normalizado da requisição
/// — campos já interpretados, então espaços, ordem das propriedades e zeros à direita do valor não contam.
/// </summary>
internal static class HashRequisicao
{
    public static string DoRegistro(TipoLancamento tipo, decimal valor, string descricao) =>
        Calcular(string.Create(CultureInfo.InvariantCulture, $"REGISTRO\n{tipo}\n{valor:0.00}\n{descricao.Trim()}"));

    public static string DoEstorno(Guid lancamentoId) =>
        Calcular(string.Create(CultureInfo.InvariantCulture, $"ESTORNO\n{lancamentoId:D}"));

    private static string Calcular(string normalizado) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizado)));
}
