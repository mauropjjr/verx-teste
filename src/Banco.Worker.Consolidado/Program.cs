using Banco.Infraestrutura.Compartilhado.Registro;
using Banco.Infraestrutura.Compartilhado.Saude;
using Banco.Infraestrutura.Consolidado;
using Banco.Infraestrutura.Consolidado.Inicializacao;
using Microsoft.Extensions.Hosting;

// Healthcheck do container: o Worker não tem HTTP; o próprio binário confere a idade do batimento de saúde,
// renovado enquanto Postgres e o consumidor RabbitMQ estão de pé (PublicadorBatimentoSaude).
if (VerificacaoSaudeLinhaComando.ExecutarBatimentoSeSolicitado(args) is { } codigoSaida)
{
    return codigoSaida;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AdicionarRegistroBanco();
builder.Services.AdicionarBatimentoSaude();
builder.Services.AdicionarInfraestruturaConsolidado(builder.Configuration);
builder.Services.AdicionarConsumidorConsolidado(builder.Configuration);

var host = builder.Build();
await host.Services.InicializarBancoConsolidadoAsync();
await host.RunAsync();
return 0;
