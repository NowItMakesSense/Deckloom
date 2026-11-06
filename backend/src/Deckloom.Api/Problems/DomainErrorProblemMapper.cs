using System.Diagnostics;
using Deckloom.SharedKernel;

namespace Deckloom.Api.Problems;

// Unico lugar onde DomainError vira status HTTP e code estavel (design: Tratamento de erros).
public static class DomainErrorProblemMapper
{
    public const string ValidationFailed = "validation_failed";
    public const string InternalError = "internal_error";
    public const string CsrfFailed = "csrf_failed";
    public const string EmailUnavailable = "email_unavailable";
    public const string AvatarRejectedCode = "avatar_rejected";

    private static readonly Dictionary<string, int> StatusByCode = new(StringComparer.Ordinal)
    {
        ["unauthenticated"] = StatusCodes.Status401Unauthorized,
        ["invalid_credentials"] = StatusCodes.Status401Unauthorized,
        ["incorrect_password"] = StatusCodes.Status401Unauthorized,
        [CsrfFailed] = StatusCodes.Status403Forbidden,
        ["reauth_required"] = StatusCodes.Status403Forbidden,
        ["reauth_expired"] = StatusCodes.Status403Forbidden,
        ["pending_not_found"] = StatusCodes.Status404NotFound,
        ["email_unavailable"] = StatusCodes.Status409Conflict,
        ["google_not_completed"] = StatusCodes.Status409Conflict,
        ["no_local_password"] = StatusCodes.Status409Conflict,
        ["concurrency_conflict"] = StatusCodes.Status409Conflict,
        ["reset_link_invalid"] = StatusCodes.Status400BadRequest,
        ["not_found"] = StatusCodes.Status404NotFound,
        // SEC-01: acima do orcamento por conta. Sem Retry-After nem dica de tempo (U14).
        ["too_many_attempts"] = StatusCodes.Status429TooManyRequests,
    };

    // Motivo da rejeicao do avatar (campo do erro `avatar_rejected`) -> status HTTP.
    private static readonly Dictionary<string, int> AvatarRejectionStatusByReason = new(StringComparer.Ordinal)
    {
        ["size"] = StatusCodes.Status413PayloadTooLarge,
        ["type"] = StatusCodes.Status415UnsupportedMediaType,
        ["content"] = StatusCodes.Status422UnprocessableEntity,
    };

    private static readonly Dictionary<string, string> TitleByCode = new(StringComparer.Ordinal)
    {
        [ValidationFailed] = "Dados inválidos",
        [InternalError] = "Erro interno",
    };

    public static IResult ToResult(IReadOnlyCollection<DomainError> errors)
    {
        // Email ja em uso: 409 (nao 400) mantendo errors.email, para o front marcar o campo (contrato do cadastro).
        if (errors.Count == 1 && errors.First() is { Code: "unavailable", Field: "email" })
        {
            return new ProblemResult(
                StatusCodes.Status409Conflict,
                EmailUnavailable,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["email"] = ["unavailable"] });
        }

        if (errors.Count == 1
            && errors.First() is { Code: AvatarRejectedCode, Field: { } reason }
            && AvatarRejectionStatusByReason.ContainsKey(reason))
        {
            return AvatarRejectedResult(reason);
        }

        var fieldErrors = errors.Where(e => e.Field is not null).ToList();
        if (fieldErrors.Count > 0)
        {
            var grouped = fieldErrors
                .GroupBy(e => e.Field!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray(), StringComparer.Ordinal);
            return new ProblemResult(StatusCodes.Status400BadRequest, ValidationFailed, grouped);
        }

        var code = errors.FirstOrDefault()?.Code;
        if (code is not null && StatusByCode.TryGetValue(code, out var status))
        {
            return new ProblemResult(status, code);
        }

        // Codigo desconhecido: falha fechada, sem repassar nada do chamador.
        return InternalErrorResult();
    }

    public static IResult InternalErrorResult() =>
        new ProblemResult(StatusCodes.Status500InternalServerError, InternalError);

    public static IResult MalformedBodyResult() =>
        new ProblemResult(StatusCodes.Status400BadRequest, ValidationFailed);

    public static IResult PayloadTooLargeResult() =>
        new ProblemResult(StatusCodes.Status413PayloadTooLarge, "payload_too_large");

    public static IResult UnsupportedMediaTypeResult() =>
        new ProblemResult(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type");

    public static IResult CsrfFailedResult() =>
        new ProblemResult(StatusCodes.Status403Forbidden, CsrfFailed);

    // `reason` precisa ser size, type ou content.
    public static IResult AvatarRejectedResult(string reason) =>
        new ProblemResult(AvatarRejectionStatusByReason[reason], AvatarRejectedCode, reason: reason);

    private sealed class ProblemResult(
        int status,
        string code,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        string? reason = null) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = $"https://deckloom.app/problems/{code}",
                Title = TitleByCode.GetValueOrDefault(code, code),
                Status = status,
            };
            problem.Extensions["code"] = code;
            if (fieldErrors is not null)
            {
                problem.Extensions["errors"] = fieldErrors;
            }

            if (reason is not null)
            {
                problem.Extensions["reason"] = reason;
            }

            problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
            return Results.Problem(problem).ExecuteAsync(httpContext);
        }
    }
}
