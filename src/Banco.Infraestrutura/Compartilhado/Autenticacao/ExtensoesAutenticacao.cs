using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Banco.Infraestrutura.Compartilhado.Autenticacao;

/// <summary>
/// Validação dos JWT RS256 emitidos pela Identidade (design D10): chaves obtidas pelo documento de
/// descoberta (<c>MetadataAddress</c>, cacheado e renovado pelo JwtBearer), validando emissor, audiência,
/// assinatura e expiração. Toda rota exige usuário autenticado por padrão (fallback policy); rotas públicas
/// (health, login, JWKS) usam <c>AllowAnonymous</c>.
/// Configuração (seção <c>Autenticacao</c>): <c>EnderecoMetadados</c>, <c>Emissor</c>, <c>Audiencia</c>,
/// <c>ExigirHttpsMetadados</c>.
/// </summary>
public static class ExtensoesAutenticacao
{
    public const string SecaoConfiguracao = "Autenticacao";
    public const string EnderecoMetadadosPadrao = "http://identidade-api:8080/.well-known/openid-configuration";
    public const string EmissorPadrao = "banco-identidade";
    public const string AudienciaPadrao = "banco-api";

    public static IServiceCollection AdicionarAutenticacaoBanco(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        var secao = configuracao.GetSection(SecaoConfiguracao);

        servicos.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opcoes =>
            {
                opcoes.MetadataAddress = secao["EnderecoMetadados"] ?? EnderecoMetadadosPadrao;
                // Na rede interna do compose a Identidade fala HTTP; em produção, ativar.
                opcoes.RequireHttpsMetadata = secao.GetValue("ExigirHttpsMetadados", false);
                opcoes.MapInboundClaims = false;
                opcoes.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = secao["Emissor"] ?? EmissorPadrao,
                    ValidateAudience = true,
                    ValidAudience = secao["Audiencia"] ?? AudienciaPadrao,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        servicos.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(PoliticasAutorizacao.PodeConsultar, p => p.RequireRole(Papeis.Consulta, Papeis.Operador, Papeis.Admin))
            .AddPolicy(PoliticasAutorizacao.PodeOperar, p => p.RequireRole(Papeis.Operador, Papeis.Admin))
            .AddPolicy(PoliticasAutorizacao.PodeAdministrar, p => p.RequireRole(Papeis.Admin));

        return servicos;
    }
}
