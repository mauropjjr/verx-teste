using System.Globalization;
using Banco.Aplicacao.Consolidado;
using Banco.Dominio.Consolidado;
using FluentValidation;
using FluentValidation.Results;

namespace Banco.Api.Consolidado.Endpoints;

/// <summary>
/// Validação de formato de rota e query (spec access-control "Validação de entrada", design D11) antes de chamar o
/// caso de uso. As chaves dos erros são os nomes que o cliente enviou (<c>contaId</c>, <c>de</c>, <c>ate</c>, <c>em</c>).
/// </summary>
internal static class ValidacaoEntrada
{
    private const int TamanhoMaximoInstante = 40;

    public static IServiceCollection AdicionarValidacaoConsolidado(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<IValidator<EntradaPeriodo>, ValidadorEntradaPeriodo>()
            .AddSingleton<IValidator<EntradaSaldo>, ValidadorEntradaSaldo>();

    /// <summary>Datas da query em <c>yyyy-MM-dd</c>.</summary>
    public static bool TentarLerData(string? texto, out DateOnly data) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out data);

    /// <summary>Instante ISO-8601; sem fuso, é tratado como UTC.</summary>
    public static bool TentarLerInstante(string? texto, out DateTimeOffset instante)
    {
        instante = default;
        return texto is { Length: > 0 and <= TamanhoMaximoInstante }
            && DateTimeOffset.TryParse(
                texto,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out instante);
    }

    public static IRuleBuilderOptions<T, string> ContaIdValido<T>(this IRuleBuilder<T, string> regra) =>
        regra.Matches("^[A-Za-z0-9-]{1,20}$")
            .WithMessage($"Identificador de conta inválido (letras, dígitos e hífen, até {Movimento.TamanhoMaximoContaId} caracteres).")
            .OverridePropertyName("contaId");

    public static IResult ProblemaValidacao(this ValidationResult resultado) =>
        TypedResults.ValidationProblem(resultado.ToDictionary());
}

/// <summary>Rota e query do <c>GET .../consolidado-diario?de&amp;ate</c>.</summary>
internal sealed record EntradaPeriodo(string ContaId, string? De, string? Ate);

internal sealed class ValidadorEntradaPeriodo : AbstractValidator<EntradaPeriodo>
{
    public ValidadorEntradaPeriodo()
    {
        RuleFor(e => e.ContaId).ContaIdValido();
        RuleFor(e => e.De)
            .Must(d => ValidacaoEntrada.TentarLerData(d, out _))
            .WithMessage("Informe a data inicial (yyyy-MM-dd).")
            .OverridePropertyName("de");
        RuleFor(e => e.Ate)
            .Must(a => ValidacaoEntrada.TentarLerData(a, out _))
            .WithMessage("Informe a data final (yyyy-MM-dd).")
            .OverridePropertyName("ate");
        RuleFor(e => e)
            .Must(e => !ValidacaoEntrada.TentarLerData(e.De, out var de)
                || !ValidacaoEntrada.TentarLerData(e.Ate, out var ate)
                || ConsultarConsolidadoDiario.IntervaloValido(de, ate))
            .WithMessage($"A data inicial deve ser anterior ou igual à final, num intervalo de até {ConsultarConsolidadoDiario.DiasMaximos} dias.")
            .OverridePropertyName("ate");
    }
}

/// <summary>Rota e query do <c>GET .../saldo?em</c>.</summary>
internal sealed record EntradaSaldo(string ContaId, string? Em);

internal sealed class ValidadorEntradaSaldo : AbstractValidator<EntradaSaldo>
{
    public ValidadorEntradaSaldo()
    {
        RuleFor(e => e.ContaId).ContaIdValido();
        RuleFor(e => e.Em)
            .Must(em => em is null || ValidacaoEntrada.TentarLerInstante(em, out _))
            .WithMessage("Instante inválido (use ISO-8601, ex.: 2026-03-10T12:00:00Z).")
            .OverridePropertyName("em");
    }
}
