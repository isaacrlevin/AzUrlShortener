using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortenerTools.Core.Domain;
using ShortenerTools.Functions.Functions;
using SkiaSharp;

namespace ShortenerTools.Tests;

[TestClass]
public class ImageScalingTests
{
    [TestMethod]
    public async Task ScalingPreservesAspectRatioAndProducesJpeg()
    {
        using var source = new SKBitmap(80, 40);
        source.Erase(SKColors.CornflowerBlue);
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);
        var input = png.ToArray();
        var scheduler = new SchedulePost(new ShortenerSettings(), null!);

        var result = await scheduler.ScaleImage(input, input.Length / 4);

        Assert.AreEqual(0xff, (int)result[0]);
        Assert.AreEqual(0xd8, (int)result[1]);
        using var decoded = SKBitmap.Decode(result);
        Assert.IsNotNull(decoded);
        var ratio = Math.Sqrt((double)(input.Length / 4) / input.Length);
        Assert.AreEqual((int)(80 * ratio), decoded.Width);
        Assert.AreEqual((int)(40 * ratio), decoded.Height);
    }

    [TestMethod]
    public async Task InvalidImagesAndSizeAreRejected()
    {
        var scheduler = new SchedulePost(new ShortenerSettings(), null!);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => scheduler.ScaleImage(null!));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => scheduler.ScaleImage([1], 0));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => scheduler.ScaleImage([1, 2, 3]));
    }
}
