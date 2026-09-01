using YsFourcut.Host.Contracts;

namespace YsFourcut.Host.Api;

/// <summary>
/// 프레임워크가 만드는 오류(잘못된 JSON, 본문 크기 초과, 예기치 못한 예외)도
/// 같은 구조화된 형태로 돌려준다. 예외 메시지 원문이나 요청 본문을 응답에 넣지 않는다.
/// </summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            if (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                logger.LogWarning("요청 본문이 상한을 넘어 거부했습니다.");
                await ApiResults.WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    ErrorCodes.BodyTooLarge,
                    "요청 본문이 상한을 넘습니다.");
                return;
            }

            logger.LogWarning("요청 본문을 해석하지 못했습니다: {Reason}", ex.GetType().Name);
            await ApiResults.WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                ErrorCodes.MalformedJson,
                "요청 본문을 JSON으로 해석하지 못했습니다.");
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            // 사진·비트맵·인증값이 섞일 수 있는 상세는 응답에 넣지 않는다.
            logger.LogError(ex, "처리하지 못한 오류가 발생했습니다.");
            await ApiResults.WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorCodes.InternalError,
                "요청을 처리하지 못했습니다.");
        }
    }
}
