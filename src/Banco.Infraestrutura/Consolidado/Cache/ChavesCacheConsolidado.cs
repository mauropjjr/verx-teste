namespace Banco.Infraestrutura.Consolidado.Cache;

/// <summary>Chaves do Redis do consolidado (design D9).</summary>
public static class ChavesCacheConsolidado
{
    /// <summary>Contador incrementado a cada projeção da conta; entra nas chaves de cache, que assim se invalidam sozinhas.</summary>
    public static string Versao(string contaId) => $"versao:{contaId}";
}
