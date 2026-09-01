using YsFourcut.Host.Jobs;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 개발자가 눈으로 확인하기 위한 인공 패턴 산출물. 기본으로는 파일을 만들지 않는다.
/// `YSFOURCUT_RENDER_INSPECT_DIR=/경로 dotnet test`처럼 명시적으로 켤 때만 저장한다.
/// 사용자 사진의 자동 저장과는 무관하다. 앱 실행 경로는 결과를 디스크에 쓰지 않는다.
/// </summary>
public sealed class RenderInspectionTool
{
    [Fact]
    public void Writes_sample_pngs_only_when_explicitly_requested()
    {
        var directory = Environment.GetEnvironmentVariable("YSFOURCUT_RENDER_INSPECT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        var presets = PrinterProfileStore.LoadPresets(Path.Combine(AppContext.BaseDirectory, "Profiles"));
        var profile80 = presets.Single(p => p.ProfileId == "virtual-80mm-8dpmm");
        var profile58 = presets.Single(p => p.ProfileId == "virtual-58mm-8dpmm");

        // 80mm 기본(straight), 절취 방식별, 58mm, 짧은 내용과 긴 스트립.
        Write(directory, "80-straight", profile80, 576, 480);
        Write(directory, "80-tear", WithCut(profile80, CutStyles.Tear), 576, 480);
        Write(directory, "80-none", WithCut(profile80, CutStyles.None), 576, 480);
        Write(directory, "58-straight", profile58, 384, 420);
        Write(directory, "80-short", profile80, 576, 120);
        Write(directory, "80-long", profile80, 576, 1788);
        Write(directory, "80-effects", profile80, 576, 480, [EstimatedEffects.InkBleed, EstimatedEffects.Banding]);
    }

    private static PrinterProfile WithCut(PrinterProfile profile, string cutStyle)
        => profile with { Paper = profile.Paper with { CutStyle = cutStyle } };

    private static void Write(
        string directory,
        string name,
        PrinterProfile profile,
        int width,
        int height,
        IReadOnlyList<string>? effects = null)
    {
        var stride = (width + 7) / 8;
        var data = BuildInspectionPattern(width, height, stride);

        var backend = new CrossEscPosSkiaAdapter();
        var renderer = new ReceiptRenderer(
            backend,
            new ReceiptPaperComposer(backend),
            new ReceiptAppearanceRenderer(backend),
            RenderLimits.Default);

        var result = renderer.Render(
            new DecodedBitmap(width, height, stride, data, PrintJobRequestValidator.ComputeDigest(data, width, height)),
            profile,
            appearanceSeed: 20260831,
            estimatedEffects: effects);

        File.WriteAllBytes(Path.Combine(directory, $"{name}-content.png"), result.Content.Bytes);
        File.WriteAllBytes(Path.Combine(directory, $"{name}-paper.png"), result.Paper.Bytes);
        File.WriteAllBytes(Path.Combine(directory, $"{name}-appearance.png"), result.Appearance!.Bytes);
    }

    /// <summary>테두리, 사진 자리(격자), 문구 자리(굵은 막대), 끝 행/열 표시가 있는 인공 패턴.</summary>
    private static byte[] BuildInspectionPattern(int width, int height, int stride)
    {
        var data = new byte[stride * height];

        void SetBlack(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            data[(y * stride) + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
        }

        void FillRect(int x0, int y0, int x1, int y1, Func<int, int, bool> predicate)
        {
            for (var y = Math.Max(0, y0); y < Math.Min(height, y1); y++)
            {
                for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
                {
                    if (predicate(x, y)) SetBlack(x, y);
                }
            }
        }

        // 바깥 테두리 (최상단·최하단 행과 좌우 끝 열을 포함)
        FillRect(0, 0, width, 3, (_, _) => true);
        FillRect(0, height - 3, width, height, (_, _) => true);
        FillRect(0, 0, 3, height, (_, _) => true);
        FillRect(width - 3, 0, width, height, (_, _) => true);

        // 헤더 문구 자리
        FillRect(40, 24, width - 40, 40, (x, y) => (x / 6 + y / 8) % 3 != 0);

        // 사진 네 칸 자리 (디더링 느낌의 1픽셀 격자)
        var slotHeight = Math.Max(24, (height - 140) / 4);
        for (var slot = 0; slot < 4; slot++)
        {
            var top = 56 + (slot * slotHeight);
            if (top + slotHeight - 8 >= height - 20) break;
            FillRect(24, top, width - 24, top + slotHeight - 8, (x, y) => (x + y) % 2 == 0);
        }

        // 푸터 문구 자리
        FillRect(60, height - 28, width - 60, height - 16, (x, _) => x % 3 != 0);

        return data;
    }
}
