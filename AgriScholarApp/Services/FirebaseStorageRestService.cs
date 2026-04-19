using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AgriScholarApp.Services;

public sealed class FirebaseStorageRestService
{
    private static readonly HttpClient Http = new();

    private static readonly string[] BucketCandidates =
    [
        FirebaseConfig.StorageBucket,
        $"{FirebaseConfig.ProjectId}.appspot.com",
        $"{FirebaseConfig.ProjectId}.firebasestorage.app"
    ];

    public async Task<FirebaseStorageUploadResult> UploadAsync(
        string idToken,
        string objectPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new InvalidOperationException("Missing Firebase id token.");
        }

        if (string.IsNullOrWhiteSpace(objectPath))
        {
            throw new ArgumentException("Object path is required.", nameof(objectPath));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = "application/octet-stream";
        }

        byte[] payload;
        using (var ms = new MemoryStream())
        {
            await content.CopyToAsync(ms, cancellationToken);
            payload = ms.ToArray();
        }

        (HttpResponseMessage? response, string? body, string? bucketTried) = (null, null, null);
        foreach (var bucketName in BucketCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var url = $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucketName)}/o?uploadType=media&name={Uri.EscapeDataString(objectPath)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            response = await Http.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            bucketTried = bucketName;

            if (response.IsSuccessStatusCode)
            {
                break;
            }

            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                break;
            }

        }

        if (response is null)
        {
            throw new InvalidOperationException("Firebase Storage upload failed: no response.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firebase Storage upload failed (HTTP {(int)response.StatusCode}) bucket='{bucketTried}': {body}");
        }

        using var doc = JsonDocument.Parse(body ?? "{}");
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var bucket = root.TryGetProperty("bucket", out var bucketEl) ? bucketEl.GetString() : null;
        var token = root.TryGetProperty("downloadTokens", out var tokenEl) ? tokenEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(bucket))
        {
            throw new InvalidOperationException("Firebase Storage upload returned invalid response.");
        }

        var downloadUrl = $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucket)}/o/{Uri.EscapeDataString(name)}?alt=media";
        if (!string.IsNullOrWhiteSpace(token))
        {
            downloadUrl += $"&token={Uri.EscapeDataString(token)}";
        }

        return new FirebaseStorageUploadResult(name, bucket, downloadUrl);
    }
}

public readonly record struct FirebaseStorageUploadResult(string ObjectName, string Bucket, string DownloadUrl);
