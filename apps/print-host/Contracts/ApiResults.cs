namespace YsFourcut.Host.Contracts;

/// <summary>구조화된 오류 응답을 만드는 단일 지점.</summary>
public static class ApiResults
{
    public static IResult Error(
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
        => Results.Json(
            new ApiErrorBody(ApiSchema.Version, new ApiErrorDetail(code, message, details)),
            statusCode: statusCode);

    public static IResult Error(int statusCode, ApiErrorDetail detail)
        => Results.Json(new ApiErrorBody(ApiSchema.Version, detail), statusCode: statusCode);

    /// <summary>아직 구현하지 않은 경계. 가짜 성공을 반환하지 않는다.</summary>
    public static IResult NotImplemented(string what, string implementedIn)
        => Error(
            StatusCodes.Status501NotImplemented,
            ErrorCodes.NotImplemented,
            $"{what}은(는) 아직 구현하지 않았습니다.",
            new Dictionary<string, object?>
            {
                ["implementedIn"] = implementedIn,
            });

    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorBody(ApiSchema.Version, new ApiErrorDetail(code, message, details)));
    }
}
