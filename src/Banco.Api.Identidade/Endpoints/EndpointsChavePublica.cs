using Banco.Infraestrutura.Compartilhado.Autenticacao;
using Banco.Infraestrutura.Identidade.Tokens;
using Microsoft.IdentityModel.Tokens;

namespace Banco.Api.Identidade.Endpoints;

/// <summary>
/// Publicação da chave de validação (spec access-control, design D10): documento de descoberta mínimo e JWKS
/// com a chave pública (sem a parte privada), ambos anônimos. As demais Apis apontam o <c>MetadataAddress</c>
/// do JwtBearer para <c>/.well-known/openid-configuration</c> e validam os tokens sem segredo compartilhado.
/// </summary>
internal static class EndpointsChavePublica
{
    public const string RotaDescoberta = "/.well-known/openid-configuration";
    public const string RotaJwks = "/.well-known/jwks.json";

    public static IEndpointRouteBuilder MapearEndpointsChavePublica(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(RotaDescoberta, (HttpRequest requisicao, IConfiguration configuracao) => TypedResults.Ok(new Dictionary<string, object>
        {
            ["issuer"] = configuracao[$"{ExtensoesAutenticacao.SecaoConfiguracao}:Emissor"] ?? ExtensoesAutenticacao.EmissorPadrao,
            // Absoluto a partir do host pelo qual a descoberta foi pedida (ex.: http://identidade-api:8080 na rede do compose).
            ["jwks_uri"] = $"{requisicao.Scheme}://{requisicao.Host}{requisicao.PathBase}{RotaJwks}",
            ["id_token_signing_alg_values_supported"] = new[] { SecurityAlgorithms.RsaSha256 },
        })).AllowAnonymous();

        endpoints.MapGet(RotaJwks, (ProvedorChavesRsa chaves) =>
        {
            var jwk = chaves.ChavePublicaJwk();
            return TypedResults.Ok(new
            {
                keys = new[] { new { kty = jwk.Kty, use = jwk.Use, alg = jwk.Alg, kid = jwk.Kid, n = jwk.N, e = jwk.E } },
            });
        }).AllowAnonymous();

        return endpoints;
    }
}
