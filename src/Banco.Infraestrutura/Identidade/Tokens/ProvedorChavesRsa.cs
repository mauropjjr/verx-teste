using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Banco.Infraestrutura.Identidade.Tokens;

/// <summary>
/// Mantém o par de chaves RSA de assinatura (design D10): usa o PEM informado na configuração ou o arquivo
/// persistido (volume); se nenhum existir, gera uma chave de 2048 bits e a grava (PKCS#8, só o dono lê).
/// O <c>kid</c> é o thumbprint JWK (RFC 7638) da chave pública: estável entre reinícios e entre réplicas.
/// </summary>
public sealed class ProvedorChavesRsa : IDisposable
{
    private const int TamanhoChaveBits = 2048;

    private readonly RSA _rsa;

    public ProvedorChavesRsa(OpcoesChavesIdentidade opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        _rsa = RSA.Create();
        _rsa.ImportFromPem(ObterPem(opcoes));

        using var publica = RSA.Create(_rsa.ExportParameters(includePrivateParameters: false));
        var thumbprint = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(publica)).ComputeJwkThumbprint();
        Chave = new RsaSecurityKey(_rsa) { KeyId = Base64UrlEncoder.Encode(thumbprint) };
    }

    /// <summary>Chave privada de assinatura, com o <c>kid</c>.</summary>
    public RsaSecurityKey Chave { get; }

    /// <summary>Chave pública no formato JWK, para o JWKS.</summary>
    public JsonWebKey ChavePublicaJwk()
    {
        using var publica = RSA.Create(_rsa.ExportParameters(includePrivateParameters: false));
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(publica));
        jwk.Kid = Chave.KeyId;
        jwk.Use = JsonWebKeyUseNames.Sig;
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        return jwk;
    }

    public void Dispose() => _rsa.Dispose();

    private static string ObterPem(OpcoesChavesIdentidade opcoes)
    {
        if (!string.IsNullOrWhiteSpace(opcoes.ChavePrivadaPem))
        {
            return opcoes.ChavePrivadaPem;
        }

        var caminho = Path.GetFullPath(opcoes.CaminhoArquivo);
        if (File.Exists(caminho))
        {
            return File.ReadAllText(caminho);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        using var nova = RSA.Create(TamanhoChaveBits);
        var pem = nova.ExportPkcs8PrivateKeyPem();

        // Grava em arquivo temporário e move: outra instância subindo ao mesmo tempo nunca lê arquivo pela metade.
        var temporario = caminho + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporario, pem);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporario, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        try
        {
            File.Move(temporario, caminho, overwrite: false);
            return pem;
        }
        catch (IOException) when (File.Exists(caminho))
        {
            // Outra instância gravou primeiro: vale a dela.
            File.Delete(temporario);
            return File.ReadAllText(caminho);
        }
    }
}
