namespace Banco.Infraestrutura.Lancamentos.Inicializacao;

/// <summary>Seed de demonstração (seção <c>Semeadura</c>, design D13).</summary>
public sealed class OpcoesSemeadura
{
    public const string Secao = "Semeadura";

    /// <summary>
    /// Gera ~1 ano de lançamentos retroativos nas contas demo. Desligado por padrão (testes e execução local);
    /// o compose liga (<c>Semeadura__HistoricoHabilitado=true</c>).
    /// </summary>
    public bool HistoricoHabilitado { get; set; }
}
