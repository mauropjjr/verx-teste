namespace Banco.Infraestrutura.Consolidado.Mensageria;

/// <summary>Consumo e retentativas da projeção (seção <c>Consumidor</c>; design D6/D8).</summary>
public sealed class OpcoesConsumidorConsolidado
{
    public const string Secao = "Consumidor";

    /// <summary>Mensagens entregues e ainda não confirmadas por consumidor (QoS do RabbitMQ).</summary>
    public ushort Prefetch { get; set; } = 20;

    /// <summary>Novas tentativas após a primeira, só para falhas transitórias; esgotadas, a mensagem vai para a DLQ.</summary>
    public int NovasTentativas { get; set; } = 3;

    /// <summary>Espera antes da 1ª nova tentativa; dobra a cada uma (com jitter): ~1 s, 2 s, 4 s.</summary>
    public TimeSpan EsperaBaseRetentativa { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>De quanto em quanto tempo o consumidor confere se o canal continua aberto.</summary>
    public TimeSpan IntervaloVerificacaoCanal { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Teto do backoff para reabrir conexão/canal com o broker.</summary>
    public TimeSpan EsperaMaximaReconexao { get; set; } = TimeSpan.FromSeconds(30);
}
