namespace Banco.Infraestrutura.Identidade.Tokens;

/// <summary>
/// Origem do par de chaves RSA que assina os tokens (seção <c>Identidade:Chaves</c>, design D10).
/// <see cref="ChavePrivadaPem"/> (ex.: variável <c>Identidade__Chaves__ChavePrivadaPem</c>) tem precedência;
/// sem ela, a chave é lida de <see cref="CaminhoArquivo"/> ou gerada e gravada lá na primeira subida (volume).
/// </summary>
public sealed class OpcoesChavesIdentidade
{
    public const string Secao = "Identidade:Chaves";

    /// <summary>
    /// Fora do repositório por padrão (perfil local do usuário), para a chave privada nunca cair num commit ao rodar
    /// com <c>dotnet run</c>. No container: <c>/app/chaves/identidade-rsa.pem</c> (volume).
    /// </summary>
    public static readonly string CaminhoArquivoPadrao = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "banco", "identidade-rsa.pem");

    public string? ChavePrivadaPem { get; set; }

    public string CaminhoArquivo { get; set; } = CaminhoArquivoPadrao;
}
