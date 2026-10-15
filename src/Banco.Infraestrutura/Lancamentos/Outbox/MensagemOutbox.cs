namespace Banco.Infraestrutura.Lancamentos.Outbox;

/// <summary>
/// Linha de <c>mensagens_outbox</c> (design D5): evento de integração gravado na mesma transação do lançamento
/// e publicado depois ao RabbitMQ. <see cref="PublicadoEm"/> nulo = pendente.
/// </summary>
public sealed class MensagemOutbox
{
    public Guid Id { get; set; }

    /// <summary>Tipo do evento (ex.: <c>lancamento.registrado.v1</c>).</summary>
    public required string Tipo { get; set; }

    /// <summary>Evento serializado em JSON (<c>jsonb</c>).</summary>
    public required string Conteudo { get; set; }

    public string? IdCorrelacao { get; set; }

    public DateTimeOffset OcorridoEm { get; set; }

    public DateTimeOffset? PublicadoEm { get; set; }

    public int Tentativas { get; set; }
}
