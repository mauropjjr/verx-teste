namespace Banco.Infraestrutura.Lancamentos.Outbox;

/// <summary>Ritmo do <see cref="PublicadorOutbox"/> (seção <c>Outbox</c>).</summary>
public sealed class OpcoesPublicadorOutbox
{
    public const string Secao = "Outbox";

    /// <summary>Espera entre varreduras quando não há (mais) pendentes.</summary>
    public TimeSpan IntervaloSondagem { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Mensagens travadas e publicadas por transação.</summary>
    public int TamanhoLote { get; set; } = 100;

    /// <summary>Teto do backoff exponencial (1 s, 2 s, 4 s...) após falha de conexão ou de publicação.</summary>
    public TimeSpan EsperaMaximaFalha { get; set; } = TimeSpan.FromSeconds(30);
}
