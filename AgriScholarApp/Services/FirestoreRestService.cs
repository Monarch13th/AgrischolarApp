using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;


namespace AgriScholarApp.Services;

public sealed class FirestoreRestService
{
    private static readonly HttpClient Http = new();

    public async Task<Dictionary<string, object?>> GetDocumentAsync(string documentPath, string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentPath)) throw new ArgumentException("Document path is required.", nameof(documentPath));
        if (string.IsNullOrWhiteSpace(idToken)) throw new ArgumentException("Id token is required.", nameof(idToken));

        var encodedPath = string.Join("/", documentPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/{encodedPath}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore read failed: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

        var dict = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            dict["__name"] = name;
            dict["__documentId"] = ExtractDocumentIdFromName(name);
        }

        if (root.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in fieldsEl.EnumerateObject())
            {
                dict[field.Name] = ParseFirestoreValue(field.Value);
            }
        }

        return dict;
    }

    public async Task DeleteDocumentAsync(string documentPath, string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentPath)) throw new ArgumentException("Document path is required.", nameof(documentPath));
        if (string.IsNullOrWhiteSpace(idToken)) throw new ArgumentException("Id token is required.", nameof(idToken));

        var encodedPath = string.Join("/", documentPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/{encodedPath}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore delete failed: {responseBody}");
        }
    }

    public async Task UpdateDocumentAsync(string documentPath, string idToken, Dictionary<string, object?> updateData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentPath)) throw new ArgumentException("Document path is required.", nameof(documentPath));
        if (updateData is null) throw new ArgumentNullException(nameof(updateData));

        var encodedPath = string.Join("/", documentPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var mask = BuildUpdateMask(updateData.Keys);
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/{encodedPath}{mask}";

        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        var body = new Dictionary<string, object?>
        {
            ["fields"] = ToFirestoreFields(updateData)
        };

        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore update failed: {responseBody}");
        }
    }

    public async Task CreateDocumentAsync(string collectionPath, string documentId, string idToken, Dictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionPath)) throw new ArgumentException("Collection path is required.", nameof(collectionPath));
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document id is required.", nameof(documentId));

        var encodedPath = string.Join("/", collectionPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/{encodedPath}?documentId={Uri.EscapeDataString(documentId)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        var body = new Dictionary<string, object?>
        {
            ["fields"] = ToFirestoreFields(data)
        };

        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore write failed: {responseBody}");
        }
    }

    public async Task<List<Dictionary<string, object?>>> ListDocumentsAsync(string collectionPath, string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionPath)) throw new ArgumentException("Collection path is required.", nameof(collectionPath));

        var encodedPath = string.Join("/", collectionPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/{encodedPath}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore read failed: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("documents", out var documentsEl) || documentsEl.ValueKind != JsonValueKind.Array)
        {
            return new List<Dictionary<string, object?>>();
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var d in documentsEl.EnumerateArray())
        {
            var name = d.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

            if (!d.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var dict = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                dict["__name"] = name;
                dict["__documentId"] = ExtractDocumentIdFromName(name);
            }
            foreach (var field in fieldsEl.EnumerateObject())
            {
                dict[field.Name] = ParseFirestoreValue(field.Value);
            }

            result.Add(dict);
        }

        return result;
    }

    private static string ExtractDocumentIdFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var idx = name.LastIndexOf('/');
        return idx >= 0 && idx < name.Length - 1 ? name[(idx + 1)..] : name;
    }

    public async Task CreateScholarAsync(string uid, string idToken, Dictionary<string, object?> scholarData, CancellationToken cancellationToken = default)
    {
        // Create document with provided uid as doc id
        // POST .../documents/scholars?documentId={uid}
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/scholars?documentId={Uri.EscapeDataString(uid)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        var body = new Dictionary<string, object?>
        {
            ["fields"] = ToFirestoreFields(scholarData)
        };

        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore write failed: {responseBody}");
        }
    }

    public async Task<List<Dictionary<string, object?>>> GetScholarsAsync(string idToken, CancellationToken cancellationToken = default)
    {
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/scholars";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore read failed: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("documents", out var documentsEl) || documentsEl.ValueKind != JsonValueKind.Array)
        {
            return new List<Dictionary<string, object?>>();
        }

        var result = new List<Dictionary<string, object?>>();

        foreach (var d in documentsEl.EnumerateArray())
        {
            if (!d.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var dict = new Dictionary<string, object?>();
            foreach (var field in fieldsEl.EnumerateObject())
            {
                dict[field.Name] = ParseFirestoreValue(field.Value);
            }

            result.Add(dict);
        }

        return result;
    }

    public async Task UpdateScholarAsync(string uid, string idToken, Dictionary<string, object?> updateData, CancellationToken cancellationToken = default)
    {
        // PATCH .../documents/scholars/{uid}?updateMask.fieldPaths=field1&updateMask.fieldPaths=field2...
        var mask = BuildUpdateMask(updateData.Keys);
        var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/scholars/{Uri.EscapeDataString(uid)}{mask}";

        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        var body = new Dictionary<string, object?>
        {
            ["fields"] = ToFirestoreFields(updateData)
        };

        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firestore update failed: {responseBody}");
        }
    }

    private static string BuildUpdateMask(IEnumerable<string> fieldPaths)
    {
        var sb = new StringBuilder();
        foreach (var f in fieldPaths)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            sb.Append(sb.Length == 0 ? "?" : "&");
            sb.Append("updateMask.fieldPaths=");
            sb.Append(Uri.EscapeDataString(f));
        }

        return sb.ToString();
    }

    private static Dictionary<string, object?> ToFirestoreFields(Dictionary<string, object?> data)
    {
        var fields = new Dictionary<string, object?>();
        foreach (var kvp in data)
        {
            fields[kvp.Key] = ToFirestoreValue(kvp.Value);
        }

        return fields;
    }

    private static object? ToFirestoreValue(object? value)
    {
        if (value is null)
        {
            return new Dictionary<string, object?> { ["nullValue"] = null };
        }

        if (value is string s)
        {
            return new Dictionary<string, object?> { ["stringValue"] = s };
        }

        if (value is bool b)
        {
            return new Dictionary<string, object?> { ["booleanValue"] = b };
        }

        if (value is int i)
        {
            return new Dictionary<string, object?> { ["integerValue"] = i.ToString() };
        }

        if (value is long l)
        {
            return new Dictionary<string, object?> { ["integerValue"] = l.ToString() };
        }

        if (value is double d)
        {
            return new Dictionary<string, object?> { ["doubleValue"] = d };
        }

        if (value is float f)
        {
            return new Dictionary<string, object?> { ["doubleValue"] = (double)f };
        }

        if (value is DateTime dt)
        {
            // Firestore timestampValue expects RFC3339 UTC time.
            var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            return new Dictionary<string, object?> { ["timestampValue"] = utc.ToString("O") };
        }

        // Fallback: stringify
        return new Dictionary<string, object?> { ["stringValue"] = value.ToString() };
    }

    private static object? ParseFirestoreValue(JsonElement firestoreValue)
    {
        // Firestore returns an object with a single known key like: stringValue, integerValue, doubleValue, booleanValue, timestampValue, nullValue
        if (firestoreValue.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (firestoreValue.TryGetProperty("stringValue", out var s)) return s.GetString();
        if (firestoreValue.TryGetProperty("booleanValue", out var b)) return b.GetBoolean();
        if (firestoreValue.TryGetProperty("doubleValue", out var d)) return d.GetDouble();
        if (firestoreValue.TryGetProperty("integerValue", out var i))
        {
            var str = i.GetString();
            if (long.TryParse(str, out var lng)) return lng;
            return str;
        }
        if (firestoreValue.TryGetProperty("timestampValue", out var t))
        {
            var str = t.GetString();
            if (DateTime.TryParse(str, out var dt)) return dt;
            return str;
        }
        if (firestoreValue.TryGetProperty("nullValue", out _)) return null;

        return null;
    }
}
