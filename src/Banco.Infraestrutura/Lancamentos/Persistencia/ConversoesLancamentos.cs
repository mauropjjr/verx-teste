using Banco.Dominio.Lancamentos;

namespace Banco.Infraestrutura.Lancamentos.Persistencia;

/// <summary>Representação textual dos enums no banco e na API (<c>CREDITO</c>/<c>DEBITO</c>, <c>ATIVA</c>/<c>INATIVA</c>).</summary>
public static class ConversoesLancamentos
{
    public const string Credito = "CREDITO";
    public const string Debito = "DEBITO";

    public static string ParaTexto(TipoLancamento tipo) => tipo switch
    {
        TipoLancamento.Credito => Credito,
        TipoLancamento.Debito => Debito,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null),
    };

    public static TipoLancamento ParaTipo(string texto) => texto switch
    {
        Credito => TipoLancamento.Credito,
        Debito => TipoLancamento.Debito,
        _ => throw new ArgumentOutOfRangeException(nameof(texto), texto, null),
    };

    public static string ParaTexto(SituacaoConta situacao) => situacao switch
    {
        SituacaoConta.Ativa => "ATIVA",
        SituacaoConta.Inativa => "INATIVA",
        _ => throw new ArgumentOutOfRangeException(nameof(situacao), situacao, null),
    };

    public static SituacaoConta ParaSituacao(string texto) => texto switch
    {
        "ATIVA" => SituacaoConta.Ativa,
        "INATIVA" => SituacaoConta.Inativa,
        _ => throw new ArgumentOutOfRangeException(nameof(texto), texto, null),
    };
}
