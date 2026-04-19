using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgriScholarApp.Services;

public sealed class FirebaseAuthRestService
{
    private static readonly HttpClient Http = new();

    private static string SignUpUrl => $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseConfig.ApiKey}";

    private static string SignInUrl => $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseConfig.ApiKey}";

    private static string SendOobCodeUrl => $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConfig.ApiKey}";

    public async Task<FirebaseSignUpResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            email,
            password,
            returnSecureToken = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.PostAsync(SignUpUrl, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(TryGetFirebaseErrorMessage(responseBody) ?? "Firebase sign-up failed.");
        }

        var result = JsonSerializer.Deserialize<FirebaseSignUpResponse>(responseBody);
        if (result is null || string.IsNullOrWhiteSpace(result.LocalId) || string.IsNullOrWhiteSpace(result.IdToken))
        {
            throw new InvalidOperationException("Firebase sign-up returned an invalid response.");
        }

        return new FirebaseSignUpResult(result.LocalId, result.IdToken);
    }

    public async Task<FirebaseSignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            email,
            password,
            returnSecureToken = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.PostAsync(SignInUrl, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(TryGetFirebaseErrorMessage(responseBody) ?? "Firebase sign-in failed.");
        }

        var result = JsonSerializer.Deserialize<FirebaseSignInResponse>(responseBody);
        if (result is null || string.IsNullOrWhiteSpace(result.LocalId) || string.IsNullOrWhiteSpace(result.IdToken))
        {
            throw new InvalidOperationException("Firebase sign-in returned an invalid response.");
        }

        return new FirebaseSignInResult(result.LocalId, result.IdToken);
    }

    public async Task SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            requestType = "PASSWORD_RESET",
            email
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.PostAsync(SendOobCodeUrl, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(TryGetFirebaseErrorMessage(responseBody) ?? "Password reset failed.");
        }
    }

    private static string? TryGetFirebaseErrorMessage(string responseBody)
    {
        try
        {
            var err = JsonSerializer.Deserialize<FirebaseErrorResponse>(responseBody);
            return err?.Error?.Message;
        }
        catch
        {
            return null;
        }
    }

    private sealed class FirebaseSignUpResponse
    {
        [JsonPropertyName("localId")] public string? LocalId { get; set; }
        [JsonPropertyName("idToken")] public string? IdToken { get; set; }
    }

    private sealed class FirebaseSignInResponse
    {
        [JsonPropertyName("localId")] public string? LocalId { get; set; }
        [JsonPropertyName("idToken")] public string? IdToken { get; set; }
    }

    private sealed class FirebaseErrorResponse
    {
        [JsonPropertyName("error")] public FirebaseError? Error { get; set; }
    }

    private sealed class FirebaseError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}

public readonly record struct FirebaseSignUpResult(string Uid, string IdToken);

public readonly record struct FirebaseSignInResult(string Uid, string IdToken);
