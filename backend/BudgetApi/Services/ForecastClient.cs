// Forecast client: asks the Python forecasting service to run Moving Average and Exponential Smoothing for a unit
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetApi.Services;

public record ForecastServiceRequest(
    [property: JsonPropertyName("unit_id")] string UnitId,
    [property: JsonPropertyName("frequency")] string Frequency,
    [property: JsonPropertyName("horizon")] int Horizon,
    [property: JsonPropertyName("window_size")] int WindowSize,
    [property: JsonPropertyName("alpha")] double Alpha
);

public class ForecastServiceException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class ForecastClient(HttpClient http)
{
    private record RunCreated([property: JsonPropertyName("run_id")] string RunId);

    public async Task<string[]> RunAsync(ForecastServiceRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/forecasts", request, cancellationToken);
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
            var runs = await response.Content.ReadFromJsonAsync<RunCreated[]>(cancellationToken) ?? [];
            return runs.Select(r => r.RunId).ToArray();
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
