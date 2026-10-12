// Forecast client: sends a unit's approved history to the Python forecasting service and gets back Moving Average and Exponential Smoothing forecasts
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetApi.Services;

public record ForecastPoint(
    [property: JsonPropertyName("period")] DateOnly Period,
    [property: JsonPropertyName("amount")] decimal Amount
);

public record ForecastInput(
    [property: JsonPropertyName("history")] Dictionary<string, ForecastPoint[]> History,
    [property: JsonPropertyName("horizon")] int Horizon,
    [property: JsonPropertyName("window_size")] int WindowSize,
    [property: JsonPropertyName("alpha")] double Alpha
);

public record ForecastOutput(
    [property: JsonPropertyName("history_start")] DateOnly HistoryStart,
    [property: JsonPropertyName("history_end")] DateOnly HistoryEnd,
    [property: JsonPropertyName("periods")] DateOnly[] Periods,
    [property: JsonPropertyName("forecasts")] Dictionary<string, Dictionary<string, decimal[]>> Forecasts,
    [property: JsonPropertyName("warnings")] string[] Warnings
);

public class ForecastServiceException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class ForecastClient(HttpClient http)
{
    public async Task<ForecastOutput> ForecastAsync(ForecastInput input, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/forecasts", input, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new ForecastServiceException(StatusCodes.Status422UnprocessableEntity, await DetailAsync(response, cancellationToken));
            }
            if (!response.IsSuccessStatusCode)
            {
                throw Unavailable();
            }
            return await response.Content.ReadFromJsonAsync<ForecastOutput>(cancellationToken) ?? throw Unavailable();
        }
    }

    private static ForecastServiceException Unavailable() =>
        new(StatusCodes.Status502BadGateway, "The forecasting service isn't responding. Check that it's running, then try again.");

    private static async Task<string> DetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (body.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString()!;
            }
        }
        catch (JsonException) { }
        return "The forecast could not run on this history.";
    }
}
