namespace Banco.Infraestrutura.Compartilhado.Mensageria;

/// <summary>Conexão com o RabbitMQ (seção <c>RabbitMq</c>; no compose, <c>RabbitMq__Host</c> etc.).</summary>
public sealed class OpcoesRabbitMq
{
    public const string Secao = "RabbitMq";

    public string Host { get; set; } = "localhost";

    public int Porta { get; set; } = 5672;

    public string Usuario { get; set; } = "guest";

    public string Senha { get; set; } = "guest";

    /// <summary>Nome da conexão exibido no management (identifica o serviço).</summary>
    public string NomeCliente { get; set; } = "banco";
}
