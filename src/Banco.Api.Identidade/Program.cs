using Banco.Api.Identidade.Endpoints;
using Banco.Infraestrutura.Compartilhado;
using Banco.Infraestrutura.Compartilhado.Saude;
using Banco.Infraestrutura.Identidade;
using Banco.Infraestrutura.Identidade.Inicializacao;

// Healthcheck do container: a imagem aspnet não tem curl/wget, então o próprio binário faz o GET em /health/live.
if (await VerificacaoSaudeLinhaComando.ExecutarSeSolicitadaAsync(args) is { } codigoSaida)
{
    return codigoSaida;
}

var builder = WebApplication.CreateBuilder(args);
builder.AdicionarPadroesBanco();
builder.Services.AdicionarInfraestruturaIdentidade(builder.Configuration);

var app = builder.Build();
app.UsarPadroesBanco();
app.MapearEndpointsAutenticacao();
app.MapearEndpointsChavePublica();

await app.Services.InicializarBancoIdentidadeAsync();
await app.RunAsync();
return 0;
