using System.Security.Cryptography;
using System.Text;
using OceanFresh.SortingSystem.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed class ImageSharpManualReviewPreviewGenerator : IManualReviewPreviewGenerator
{
    private const int NominalImageWidth = 1536;
    private const int NominalImageHeight = 300;

    public ManualReviewPreview BuildPreviewImage(InspectionRecord record, DefectDetection detection)
    {
        try
        {
            var sourcePath = ResolveSourceImagePath(record.ImagePath);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return new ManualReviewPreview(
                    string.Empty,
                    false,
                    "未找到该检测记录对应的原始 X 光图片，已拒绝使用带框结果图作为复核图。");
            }

            var previewDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OceanFreshSortingSystem",
                "manual-review-previews");
            Directory.CreateDirectory(previewDirectory);

            var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"manual-review-crop-v2-lazy|{sourcePath}|{detection.Id}|{detection.X}|{detection.Y}|{detection.Width}|{detection.Height}|{File.GetLastWriteTimeUtc(sourcePath):O}")));
            var previewPath = Path.Combine(previewDirectory, $"{cacheKey}.png");
            if (File.Exists(previewPath))
            {
                return new ManualReviewPreview(previewPath, true, "已读取缓存中的单目标裁剪复核图。");
            }

            using var image = Image.Load<Rgba32>(sourcePath);
            var target = ScaleDetectionBox(detection, image.Width, image.Height);
            var crop = BuildCropRectangle(target, image.Width, image.Height);
            var relativeTarget = new Rectangle(
                target.X - crop.X,
                target.Y - crop.Y,
                target.Width,
                target.Height);

            image.Mutate(context => context.Crop(crop));
            DrawRectangle(image, relativeTarget, new Rgba32(239, 68, 68), Math.Clamp(image.Width / 140, 3, 8));
            image.SaveAsPng(previewPath);
            return new ManualReviewPreview(previewPath, true, "已生成单目标裁剪复核图。");
        }
        catch (Exception ex)
        {
            return new ManualReviewPreview(
                string.Empty,
                false,
                $"生成单目标复核图失败: {ex.Message}");
        }
    }

    private static Rectangle ScaleDetectionBox(DefectDetection detection, int imageWidth, int imageHeight)
    {
        var scaleX = imageWidth / (double)NominalImageWidth;
        var scaleY = imageHeight / (double)NominalImageHeight;
        var x = (int)Math.Round(detection.X * scaleX);
        var y = (int)Math.Round(detection.Y * scaleY);
        var width = Math.Max(1, (int)Math.Round(detection.Width * scaleX));
        var height = Math.Max(1, (int)Math.Round(detection.Height * scaleY));
        return ClampRectangle(new Rectangle(x, y, width, height), imageWidth, imageHeight);
    }

    private static Rectangle BuildCropRectangle(Rectangle target, int imageWidth, int imageHeight)
    {
        var marginX = Math.Max(target.Width * 1.6, imageWidth * 0.04);
        var marginY = Math.Max(target.Height * 1.35, imageHeight * 0.14);
        var left = Math.Max(0, (int)Math.Floor(target.Left - marginX));
        var top = Math.Max(0, (int)Math.Floor(target.Top - marginY));
        var right = Math.Min(imageWidth, (int)Math.Ceiling(target.Right + marginX));
        var bottom = Math.Min(imageHeight, (int)Math.Ceiling(target.Bottom + marginY));
        return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Rectangle ClampRectangle(Rectangle rectangle, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(rectangle.Left, 0, Math.Max(0, imageWidth - 1));
        var top = Math.Clamp(rectangle.Top, 0, Math.Max(0, imageHeight - 1));
        var right = Math.Clamp(rectangle.Right, left + 1, imageWidth);
        var bottom = Math.Clamp(rectangle.Bottom, top + 1, imageHeight);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static void DrawRectangle(Image<Rgba32> image, Rectangle rectangle, Rgba32 color, int thickness)
    {
        var box = ClampRectangle(rectangle, image.Width, image.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var offset = 0; offset < thickness; offset++)
            {
                var top = Math.Min(image.Height - 1, box.Top + offset);
                var bottom = Math.Max(0, box.Bottom - 1 - offset);
                PaintHorizontal(accessor, top, box.Left, box.Right, color);
                PaintHorizontal(accessor, bottom, box.Left, box.Right, color);

                var left = Math.Min(image.Width - 1, box.Left + offset);
                var right = Math.Max(0, box.Right - 1 - offset);
                PaintVertical(accessor, left, box.Top, box.Bottom, color);
                PaintVertical(accessor, right, box.Top, box.Bottom, color);
            }
        });
    }

    private static void PaintHorizontal(PixelAccessor<Rgba32> accessor, int y, int left, int right, Rgba32 color)
    {
        if (y < 0 || y >= accessor.Height)
        {
            return;
        }

        var row = accessor.GetRowSpan(y);
        for (var x = Math.Max(0, left); x < Math.Min(row.Length, right); x++)
        {
            row[x] = color;
        }
    }

    private static void PaintVertical(PixelAccessor<Rgba32> accessor, int x, int top, int bottom, Rgba32 color)
    {
        if (x < 0 || x >= accessor.Width)
        {
            return;
        }

        for (var y = Math.Max(0, top); y < Math.Min(accessor.Height, bottom); y++)
        {
            accessor.GetRowSpan(y)[x] = color;
        }
    }

    private static string ResolveSourceImagePath(string imagePath)
    {
        if (File.Exists(imagePath) && !LooksLikeYoloAnnotatedRunImage(imagePath))
        {
            return imagePath;
        }

        var originalName = ExtractOriginalFrameName(imagePath);
        if (string.IsNullOrWhiteSpace(originalName))
        {
            return imagePath;
        }

        foreach (var root in GetOriginalImageSearchRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var match = Directory.EnumerateFiles(root, originalName, SearchOption.AllDirectories)
                .FirstOrDefault(path => !LooksLikeYoloAnnotatedRunImage(path));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetOriginalImageSearchRoots()
    {
        yield return @"E:\deepLearning\git\ultralytics-8.3.163-2\ultralytics-8.3.163\datasets";
        yield return @"E:\deepLearning\git\ultralytics-8.3.163-2\ultralytics-8.3.163\datasets\data\youge\images";
        yield return @"E:\deepLearning\git\ultralytics-8.3.163-2\ultralytics-8.3.163\datasets\data\youge\images\train";
        yield return @"E:\deepLearning\git\ultralytics-8.3.163-2\ultralytics-8.3.163\datasets\data\youge\images\val";
    }

    private static bool LooksLikeYoloAnnotatedRunImage(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}predict_stream{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.AltDirectorySeparatorChar}predict_stream{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}service_runtime{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.AltDirectorySeparatorChar}service_runtime{Path.AltDirectorySeparatorChar}runs{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}predict_run{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.AltDirectorySeparatorChar}predict_run{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string ExtractOriginalFrameName(string path)
    {
        var fileName = Path.GetFileName(path);
        var markerIndex = fileName.IndexOf("__", StringComparison.Ordinal);
        return markerIndex >= 0 && markerIndex + 2 < fileName.Length
            ? fileName[(markerIndex + 2)..]
            : fileName;
    }
}
