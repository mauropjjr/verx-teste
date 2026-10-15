using Banco.Api.Lancamentos.Endpoints;
using Banco.Infraestrutura.Compartilhado;
using Banco.Infraestrutura.Compartilhado.Saude;
using Banco.Infraestrutura.Lancamentos;
using Banco.Infraestrutura.Lancamentos.Inicializacao;

// Healthcheck do container: a imagem aspnet não tem curl/wget, então o próprio binário faz o GET em /health/live.
if (await VerificacaoSaudeLinhaComando.ExecutarSeSolicitadaAsync(args) is { } codigoSaida)
{
    return codigoSaida;
}

var builder = WebApplication.CreateBuilder(args);
builder.AdicionarPadroesBanco();
builder.Services.AdicionarInfraestruturaLancamentos(builder.Configuration);
builder.Services.AdicionarPublicadorOutboxLancamentos(builder.Configuration);
builder.Services.AdicionarValidacaoLancamentos();

var app = builder.Build();
app.UsarPadroesBanco();
app.MapearEndpointsContas();
app.MapearEndpointsLancamentos();

await app.Services.InicializarBancoLancamentosAsync();
await app.RunAsync();
return 0;
