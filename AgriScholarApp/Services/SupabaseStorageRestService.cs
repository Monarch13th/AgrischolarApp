using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AgriScholarApp.Services;

public sealed class SupabaseStorageRestService
{
    private static readonly HttpClient Http = new();

    public async Task<SupabaseStorageUploadResult> UploadAsync(
        string bucket,
        string objectPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucket)) throw new ArgumentException("Bucket is required.", nameof(bucket));
        if (string.IsNullOrWhiteSpace(objectPath)) throw new ArgumentException("Object path is required.", nameof(objectPath));
        if (content is null) throw new ArgumentNullException(nameof(content));

        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = "application/octet-stream";
        }

        var baseUrl = FirebaseConfig.SupabaseProjectUrl.TrimEnd('/');
        var url = $"{baseUrl}/storage/v1/object/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectPath).Replace("%2F", "/")}";

        byte[] payload;
        using (var ms = new MemoryStream())
        {
            await content.CopyToAsync(ms, cancellationToken);
            payload = ms.ToArray();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", FirebaseConfig.SupabasePublishableKey);
        request.Headers.Add("apikey", FirebaseConfig.SupabasePublishableKey);

        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Supabase Storage upload failed (HTTP {(int)response.StatusCode}): {responseBody}");
        }

        // Public URL format. If your bucket is private, this URL will not work without signed URL.
        var publicUrl = $"{baseUrl}/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{objectPath}";

        // Try to read JSON if returned, but we don't require it.
        string? returnedPath = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
            if (doc.RootElement.TryGetProperty("Key", out var keyEl))
            {
                returnedPath = keyEl.GetString();
            }
        }
        catch
        {
        }

        return new SupabaseStorageUploadResult(bucket, objectPath, publicUrl, returnedPath);
    }
}

public readonly record struct SupabaseStorageUploadResult(string Bucket, string ObjectPath, string PublicUrl, string? ReturnedPath);
