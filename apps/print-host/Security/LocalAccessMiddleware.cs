using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using YsFourcut.Host.Contracts;

namespace YsFourcut.Host.Security;

/// <summary>
/// 모든 요청에 적용하는 로컬 경계 검사.
/// 1) 루프백 Host 확인 2) /api 응답은 no-store 3) /api는 전용 헤더 요구
/// 4) 변경 요청은 정확한 Origin과 JSON Content-Type 요구 5) 본문 크기 상한.
/// Vite 프록시는 Host만 바꾸고 Origin은 유지하므로 프록시를 지나도 같은 검사가 성립한다.
/// </summary>
public sealed class LocalAccessMiddleware(RequestDelegate next, LocalAccessOptions options)
{
    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (!IsAllowedHost(request.Host))
        {
            await ApiResults.WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                ErrorCodes.HostNotAllowed,
                "이 호스트 주소로는 접근할 수 없습니다. 127.0.0.1로 접속하세요.");
            return;
        }

        var isApi = request.Path.StartsWithSegments("/api", StringComparison.Ordinal);
        var isPairing = request.Path.StartsWithSegments("/pair", StringComparison.Ordinal);

        if (isApi || isPairing)
        {
            // 사진·작업·인증 응답은 캐시하지 않는다. 페어링 화면도 코드를 만들므로 캐시 금지다.
            context.Response.Headers[HeaderNames.CacheControl] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        }

        if (isApi)
        {
            if (!HasClientHeader(request))
            {
                await ApiResults.WriteErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    ErrorCodes.ClientHeaderRequired,
                    $"{LocalAccessOptions.ClientHeaderName} 헤더가 필요합니다.");
                return;
            }

            if (IsMutating(request.Method))
            {
                if (!IsAllowedOrigin(request.Headers.Origin))
                {
                    await ApiResults.WriteErrorAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        ErrorCodes.OriginNotAllowed,
                        "허용되지 않은 Origin입니다.");
                    return;
                }

                if (!IsJsonContentType(request.ContentType))
                {
                    await ApiResults.WriteErrorAsync(
                        context,
                        StatusCodes.Status415UnsupportedMediaType,
                        ErrorCodes.ContentTypeUnsupported,
                        "변경 요청은 Content-Type: application/json이어야 합니다.");
                    return;
                }
            }

            if (request.ContentLength is { } length && length > options.MaxRequestBodyBytes)
            {
                await ApiResults.WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    ErrorCodes.BodyTooLarge,
                    "요청 본문이 상한을 넘습니다.",
                    new Dictionary<string, object?> { ["maxRequestBodyBytes"] = options.MaxRequestBodyBytes });
                return;
            }

            // Kestrel 밖(테스트 서버 등)에서도 같은 상한이 걸리도록 기능 자체를 설정한다.
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
            {
                sizeFeature.MaxRequestBodySize = options.MaxRequestBodyBytes;
            }
        }

        await next(context);
    }

    private bool IsAllowedHost(HostString host)
        => !string.IsNullOrEmpty(host.Host) &&
           options.AllowedHostNames.Contains(host.Host, StringComparer.OrdinalIgnoreCase);

    private static bool HasClientHeader(HttpRequest request)
        => request.Headers.TryGetValue(LocalAccessOptions.ClientHeaderName, out var values) &&
           values.Count > 0 &&
           string.Equals(values[0], LocalAccessOptions.ClientHeaderValue, StringComparison.Ordinal);

    private static bool IsMutating(string method)
        => MutatingMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    private bool IsAllowedOrigin(Microsoft.Extensions.Primitives.StringValues origin)
        => origin.Count == 1 &&
           !string.IsNullOrEmpty(origin[0]) &&
           options.AllowedOrigins.Contains(origin[0]!, StringComparer.Ordinal);

    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType) ||
            !MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return false;
        }

        return parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
