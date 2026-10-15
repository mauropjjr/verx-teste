using Serilog.Core;
using Serilog.Events;

namespace Banco.Infraestrutura.Compartilhado.Registro;

/// <summary>
/// Mascara, em qualquer nível de aninhamento (propriedades, objetos destruturados e dicionários), valores
/// cujo nome indica dado sensível — <c>Authorization</c>, senha, tokens e cookies (design D11).
/// </summary>
public sealed class FiltroDadosSensiveis : ILogEventEnricher
{
    public const string Mascara = "***";

    private static readonly ScalarValue _valorMascarado = new(Mascara);

    private static readonly HashSet<string> _nomesSensiveis = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "senha",
        "password",
        "token",
        "tokenAcesso",
        "access_token",
        "refresh_token",
        "Cookie",
        "Set-Cookie",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        foreach (var (nome, valor) in logEvent.Properties.ToArray())
        {
            var limpo = EhSensivel(nome) ? _valorMascarado : Limpar(valor);
            if (!ReferenceEquals(limpo, valor))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(nome, limpo));
            }
        }
    }

    private static bool EhSensivel(string nome) => _nomesSensiveis.Contains(nome);

    private static LogEventPropertyValue Limpar(LogEventPropertyValue valor) => valor switch
    {
        StructureValue estrutura => new StructureValue(
            estrutura.Properties.Select(p => new LogEventProperty(p.Name, EhSensivel(p.Name) ? _valorMascarado : Limpar(p.Value))),
            estrutura.TypeTag),
        DictionaryValue dicionario => new DictionaryValue(
            dicionario.Elements.Select(e => KeyValuePair.Create(
                e.Key,
                e.Key.Value is string chave && EhSensivel(chave) ? _valorMascarado : Limpar(e.Value)))),
        SequenceValue sequencia => new SequenceValue(sequencia.Elements.Select(Limpar)),
        _ => valor,
    };
}
