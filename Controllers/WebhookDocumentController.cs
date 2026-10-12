using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevCoreBlog.Configuration;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Security;
using DevCoreBlog.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Explicit v2 ingress authenticates before parsing; strict input excludes server-owned fields.</summary>
[ApiController]
[Route("api/webhooks/v2/posts")]
[EnableRateLimiting("WebhookLimiter")]
public sealed class WebhookDocumentController(WebhookIngressOptions options, WebhookDocumentService documents) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict, MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new ExplicitOffsetConverter() }
    };

    /// <summary>Cookie-independent secret authentication is the sole reason for this narrow CSRF exemption.</summary>
    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(WebhookIngressOptions.MaximumRequestBodyBytes)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!WebhookRequestAuthentication.HasValidSecret(Request, options)) return Failure(401, "Yetkisiz istek.");
        if (!WebhookRequestAuthentication.TryReadKey(Request, out var key, required: true) || key is null)
            return Failure(400, "1–128 ASCII harf/rakam, nokta, alt çizgi veya tire içeren tek Idempotency-Key gereklidir.");
        if (Request.ContentLength > WebhookIngressOptions.MaximumRequestBodyBytes) return Failure(413, "İstek gövdesi sınırı aşıldı.");
        if (!Request.HasJsonContentType()) return Failure(415, "JSON içerik türü gereklidir.");
        WebhookDocumentPayload? payload;
        try { payload = await JsonSerializer.DeserializeAsync<WebhookDocumentPayload>(Request.Body, JsonOptions, cancellationToken); }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { return Failure(413, "İstek gövdesi sınırı aşıldı."); }
        catch (JsonException) { return Failure(400, "Geçersiz veya desteklenmeyen JSON girdisi."); }
        if (payload is null) return Failure(400, "İçerik girdisi gereklidir.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));
        var result = await documents.CreateAsync(new(payload.Title, payload.DocumentJson, payload.CategoryId,
            payload.ContentKind, payload.AccessScope, payload.Summary, payload.Excerpt, payload.IsPublished, payload.PublishDate),
            key, hash, options.AllowPublish, cancellationToken);
        if (result.Status == WebhookDocumentStatus.Conflict) return Failure(409, "Anahtar farklı bir içerik için kullanılmış.");
        if (result.Status is WebhookDocumentStatus.Invalid or WebhookDocumentStatus.Limit)
            return StatusCode(result.Status == WebhookDocumentStatus.Limit ? 413 : 400,
                new { success = false, message = "İçerik doğrulanamadı.", errors = result.Errors });
        var post = result.Post ?? throw new InvalidOperationException("Committed webhook result is required.");
        return Ok(new { success = true, contractVersion = WebhookDocumentService.ContractVersion, postId = post.Id,
            title = post.Title, slug = post.Slug, isPublished = post.IsPublished, publishDate = post.PublishDate });
    }

    private ObjectResult Failure(int status, string message) => StatusCode(status, new { success = false, message });

    // Machine timestamps must name their offset; host-local timezone is never inferred.
    private sealed class ExplicitOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions serializerOptions)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException();
            var value = reader.GetString();
            var hasOffset = value is not null && (value.EndsWith('Z') ||
                (value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':' &&
                 char.IsAsciiDigit(value[^5]) && char.IsAsciiDigit(value[^4]) &&
                 char.IsAsciiDigit(value[^2]) && char.IsAsciiDigit(value[^1])));
            if (!hasOffset || !reader.TryGetDateTimeOffset(out var date)) throw new JsonException();
            return date;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions serializerOptions) =>
            writer.WriteStringValue(value);
    }
}

/// <summary>The v2 wire contract accepts canonical JSON text and explicit bounded metadata only.</summary>
public sealed class WebhookDocumentPayload
{
    [JsonRequired] public string? Title { get; init; }
    [JsonRequired] public string? DocumentJson { get; init; }
    [JsonRequired] public int CategoryId { get; init; }
    [JsonRequired] public PostContentKind ContentKind { get; init; }
    public PostAccessScope AccessScope { get; init; } = PostAccessScope.Public;
    public string? Summary { get; init; }
    public string? Excerpt { get; init; }
    public bool IsPublished { get; init; }
    public DateTimeOffset? PublishDate { get; init; }
}
