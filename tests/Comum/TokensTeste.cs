using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Banco.Testes.Comum;

/// <summary>
/// Emite JWTs RS256 de teste e configura o <c>JwtBearer</c> do host em teste para validá-los com uma chave
/// RSA local, sem buscar metadata na Identidade. Incluído por link nos projetos de teste que sobem uma Api
/// (<c>&lt;Compile Include="../Comum/TokensTeste.cs" Link="Comum/TokensTeste.cs" /&gt;</c>).
/// Uso com WebApplicationFactory: <c>builder.ConfigureTestServices(s =&gt; s.ValidarTokensComChaveDeTeste())</c>.
/// </summary>
internal static class TokensTeste
{
    public const string EmissorPadrao = "banco-identidade";
    public const string AudienciaPadrao = "banco-api";

    private static readonly RSA _rsa = RSA.Create(2048);

    /// <summary>Chave confiável pelo host em teste.</summary>
    public static RsaSecurityKey Chave { get; } = new(_rsa) { KeyId = "chave-teste" };

    /// <summary>Gera uma chave RSA nova, não confiável — para simular assinatura inválida.</summary>
    public static RsaSecurityKey NovaChaveNaoConfiavel() => new(RSA.Create(2048)) { KeyId = "chave-estranha" };

    public static string Emitir(
        string sub = "usuario-teste",
        IReadOnlyCollection<string>? papeis = null,
        string emissor = EmissorPadrao,
        string audiencia = AudienciaPadrao,
        DateTime? expiraEmUtc = null,
        SecurityKey? chaveAssinatura = null,
        string nome = "Usuário de Teste")
    {
        var expira = expiraEmUtc ?? DateTime.UtcNow.AddMinutes(30);
        var descritor = new SecurityTokenDescriptor
        {
            Issuer = emissor,
            Audience = audiencia,
            IssuedAt = expira.AddMinutes(-30),
            NotBefore = expira.AddMinutes(-30),
            Expires = expira,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = sub,
                ["name"] = nome,
                ["role"] = (papeis ?? []).ToArray(),
            },
            SigningCredentials = new SigningCredentials(chaveAssinatura ?? Chave, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descritor);
    }

    /// <summary>Faz o JwtBearer do host validar com <see cref="Chave"/> em vez do MetadataAddress da Identidade.</summary>
    public static IServiceCollection ValidarTokensComChaveDeTeste(this IServiceCollection servicos) =>
        servicos.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opcoes =>
        {
            opcoes.ConfigurationManager = null;
            opcoes.TokenValidationParameters.IssuerSigningKey = Chave;
        });
}
