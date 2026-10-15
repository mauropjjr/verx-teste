namespace Banco.Infraestrutura.Compartilhado.Autenticacao;

/// <summary>Nomes das policies de autorização (design D10).</summary>
public static class PoliticasAutorizacao
{
    /// <summary>Consultas: <c>consulta</c>, <c>operador</c> ou <c>admin</c>.</summary>
    public const string PodeConsultar = nameof(PodeConsultar);

    /// <summary>Registrar/estornar lançamentos: <c>operador</c> ou <c>admin</c>.</summary>
    public const string PodeOperar = nameof(PodeOperar);

    /// <summary>Administração (ex.: reprocessar DLQ): só <c>admin</c>.</summary>
    public const string PodeAdministrar = nameof(PodeAdministrar);
}
