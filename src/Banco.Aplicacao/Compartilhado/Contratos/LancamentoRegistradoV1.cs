using System.Text.Json;
using System.Text.Json.Serialization;

namespace Banco.Aplicacao.Compartilhado.Contratos;

/// <summary>
/// Evento de integração publicado pelo serviço de Lançamentos (via outbox) e consumido pelo Consolidado.
/// Contrato versionado: mudanças incompatíveis geram um <c>V2</c> com outro <see cref="NomeEvento"/>, nunca
/// alteram este tipo. Não carrega dados pessoais (sem titular nem descrição).
/// </summary>
/// <param name="IdLancamento">Id do lançamento (também usado como <c>message_id</c> para idempotência).</param>
/// <param name="ContaId">Conta movimentada.</param>
/// <param name="Tipo">Crédito ou débito.</param>
/// <param name="Valor">Valor sempre positivo, com até 2 casas decimais.</param>
/// <param name="OcorridoEm">Instante de ocorrência atribuído pelo servidor.</param>
/// <param name="EstornoDe">Id do lançamento estornado, quando este é um estorno.</param>
public sealed record LancamentoRegistradoV1(
    Guid IdLancamento,
    string ContaId,
    TipoLancamentoV1 Tipo,
    decimal Valor,
    DateTimeOffset OcorridoEm,
    Guid? EstornoDe)
{
    /// <summary>Nome do evento — tipo no outbox e routing key no RabbitMQ (design D6).</summary>
    public const string NomeEvento = "lancamento.registrado.v1";

    /// <summary>Opções de serialização do contrato (camelCase, enums textuais).</summary>
    public static JsonSerializerOptions OpcoesJson { get; } = CriarOpcoesJson();

    private static JsonSerializerOptions CriarOpcoesJson()
    {
        var opcoes = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opcoes.MakeReadOnly(populateMissingResolver: true);
        return opcoes;
    }
}

/// <summary>Tipo do lançamento no contrato (serializado como <c>CREDITO</c>/<c>DEBITO</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TipoLancamentoV1>))]
public enum TipoLancamentoV1
{
    [JsonStringEnumMemberName("CREDITO")]
    Credito = 1,

    [JsonStringEnumMemberName("DEBITO")]
    Debito = 2,
}
