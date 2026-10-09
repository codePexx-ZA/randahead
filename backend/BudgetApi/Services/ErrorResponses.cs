// Error responses: gives every failed request (bad input, unhandled exception, bare status code) a readable { message } body
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetApi.Services;

public static class ErrorResponses
{
    public static IMvcBuilder AddValidationResponses(this IMvcBuilder mvc) =>
        mvc.ConfigureApiBehaviorOptions(options =>
        {
            options.SuppressMapClientErrors = true;
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(Validation(context.ModelState));
        });

    public static IServiceCollection AddErrorResponses(this IServiceCollection services)
    {
        services.AddExceptionHandler<ServerErrorHandler>();
        return services;
    }

    public static WebApplication UseErrorResponses(this WebApplication app)
    {
        app.UseExceptionHandler(_ => { });
        app.UseStatusCodePages(context =>
            context.HttpContext.Response.WriteAsJsonAsync(
                new { message = StatusMessage(context.HttpContext.Response.StatusCode) },
                context.HttpContext.RequestAborted
            )
        );
        return app;
    }

    private static object Validation(ModelStateDictionary state)
    {
        var failed = state.Where(e => e.Value is { Errors.Count: > 0 }).ToList();
        var unreadable = failed.Where(e => IsJsonKey(e.Key)).ToList();
        var errors = (unreadable.Count > 0 ? unreadable : failed)
            .ToDictionary(e => FieldKey(e.Key), e => e.Value!.Errors.Select(x => FieldMessage(e.Key, x)).ToArray());
        var message = errors.Values.SelectMany(m => m).FirstOrDefault() ?? "Some of the details sent are not valid.";
        return new { message, errors };
    }

    private static bool IsJsonKey(string key) => key is "" || key.StartsWith('$');

    private static string FieldKey(string key) => key.StartsWith("$.") ? key[2..] : key;

    private static string FieldMessage(string key, ModelError error)
    {
        if (key.StartsWith("$."))
        {
            var field = FieldKey(key).Split('.').Last().Replace('_', ' ');
            return $"The {field} isn't in the right format.";
        }
        if (key is "" or "$" || error.ErrorMessage.Contains("request body", StringComparison.OrdinalIgnoreCase))
        {
            return "The request was empty or not valid JSON.";
        }
        return string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Some of the details sent are not valid." : error.ErrorMessage;
    }

    private static string StatusMessage(int status) =>
        status switch
        {
            StatusCodes.Status400BadRequest => "The request was not valid.",
            StatusCodes.Status401Unauthorized => "Your session has ended. Sign in again.",
            StatusCodes.Status403Forbidden => "Your role can't do this.",
            StatusCodes.Status404NotFound => "This wasn't found. It may have been deleted.",
            StatusCodes.Status405MethodNotAllowed => "This action isn't allowed here.",
            StatusCodes.Status409Conflict => "This clashes with a change someone else made. Reload and try again.",
            StatusCodes.Status413PayloadTooLarge => "The file or request is too large.",
            StatusCodes.Status415UnsupportedMediaType => "The data was sent in a format the server doesn't accept.",
            StatusCodes.Status503ServiceUnavailable => "A service the app needs isn't running. Try again in a moment.",
            >= 500 => "Something went wrong on the server. Try again.",
            _ => $"The request failed (status {status}). Try again.",
        };

    private sealed class ServerErrorHandler(ILogger<ServerErrorHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                return true;
            }

            var (status, message) = Describe(exception);
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            if (status >= 500)
            {
                logger.LogError(exception, "Request {Method} {Path} failed ({TraceId})", context.Request.Method, context.Request.Path, traceId);
            }
            else
            {
                logger.LogWarning("Request {Method} {Path} answered {Status}: {Error}", context.Request.Method, context.Request.Path, status, exception.Message);
            }

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { message, traceId }, cancellationToken);
            return true;
        }

        private static (int Status, string Message) Describe(Exception exception)
        {
            if (exception is BadHttpRequestException bad)
            {
                return bad.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? (bad.StatusCode, "The file or request is too large.")
                    : (bad.StatusCode, "The request could not be read. Check the details and try again.");
            }
            if (exception is DbUpdateConcurrencyException)
            {
                return (StatusCodes.Status409Conflict, "Someone else changed this at the same time. Reload and try again.");
            }

            var postgres = Chain(exception).OfType<PostgresException>().FirstOrDefault();
            if (postgres?.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return (StatusCodes.Status409Conflict, "This already exists. Reload and check before trying again.");
            }
            if (postgres?.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                return (StatusCodes.Status409Conflict, "This is linked to other records, so it can't be saved or deleted.");
            }
            if (Chain(exception).OfType<NpgsqlException>().Any(e => e.IsTransient || e is not PostgresException))
            {
                return (StatusCodes.Status503ServiceUnavailable, "The database isn't reachable right now. Try again in a moment.");
            }

            return (StatusCodes.Status500InternalServerError, "Something went wrong on the server. Try again; if it keeps happening, tell your administrator.");
        }

        private static IEnumerable<Exception> Chain(Exception exception)
        {
            for (var e = exception; e is not null; e = e.InnerException)
            {
                yield return e;
            }
        }
    }
}
