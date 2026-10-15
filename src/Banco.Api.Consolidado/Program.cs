using Banco.Api.Consolidado.Endpoints;
using Banco.Infraestrutura.Compartilhado;
using Banco.Infraestrutura.Compartilhado.Saude;
using Banco.Infraestrutura.Consolidado;
using Banco.Infraestrutura.Consolidado.Inicializacao;

// Healthcheck do container: a imagem aspnet não tem curl/wget, então o próprio binário faz o GET em /health/live.
if (await VerificacaoSaudeLinhaComando.ExecutarSeSolicitadaAsync(args) is { } codigoSaida)
{
    return codigoSaida;
}

var builder = WebApplication.CreateBuilder(args);
builder.AdicionarPadroesBanco();
builder.Services.AdicionarInfraestruturaConsolidado(builder.Configuration);
builder.Services.AdicionarConsultasConsolidado();
builder.Services.AdicionarReprocessamentoDlqConsolidado(builder.Configuration);
builder.Services.AdicionarValidacaoConsolidado();

var app = builder.Build();
app.UsarPadroesBanco();
app.MapearEndpointsConsolidado();
app.MapearEndpointsAdministracao();

await app.Services.InicializarBancoConsolidadoAsync();
await app.RunAsync();
return 0;
