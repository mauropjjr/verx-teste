using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Banco.Testes.Comum;

/// <summary>
/// Captura os eventos de log do Serilog (registrado no DI como <see cref="ILogEventSink"/>). Incluído por link nos
/// projetos de teste, como <c>TokensTeste.cs</c>.
/// </summary>
internal sealed class SinkMemoria : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _eventos = new();

    public IReadOnlyCollection<LogEvent> Eventos => _eventos.ToArray();

    public void Emit(LogEvent logEvent) => _eventos.Enqueue(logEvent);

    /// <summary>Todos os eventos renderizados em JSON compacto (o mesmo formato da saída do console).</summary>
    public string ComoJson()
    {
        var formatador = new RenderedCompactJsonFormatter();
        using var escritor = new StringWriter();
        foreach (var evento in _eventos)
        {
            formatador.Format(evento, escritor);
        }

        return escritor.ToString();
    }
}
