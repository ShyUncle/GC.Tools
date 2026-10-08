using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Medium;
namespace SimdPaddleOCRTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using PaddleOcrAll ocr = await PaddleOcrAll.LoadAsync(ChineseV6MediumModels.Default);
            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(CreateDecoderOptions(), "Imgs/2.png");
            Rgba32[]? packedCopy = null;
            if (!image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> memory))
            {
                packedCopy = new Rgba32[checked(image.Width * image.Height)];
                image.CopyPixelDataTo(packedCopy);
                memory = packedCopy;
            }

            PaddleOcrResult result = ocr.Run(MemoryMarshal.AsBytes(memory.Span), image.Width, image.Height,
                format: ImagePixelFormat.Rgba32);
            foreach (var item in result.Lines)
            {
                Console.WriteLine(item.Text);
                Console.WriteLine(string.Join(", ", item.Box.X1, item.Box.Y1, item.Box.X2, item.Box.Y2, item.Box.X3, item.Box.Y3, item.Box.X4, item.Box.Y4));
            }
            Console.WriteLine(result.Text);

            // 必须 Clone Default：直接改 Configuration.Default 会丢掉 PNG/JPEG 解码器。
            // 默认分配器按 4MB 分块，大图会让 DangerousTryGetSinglePixelMemory 失败。
            static DecoderOptions CreateDecoderOptions()
            {
                Configuration configuration = Configuration.Default.Clone();
                configuration.PreferContiguousImageBuffers = true;
                return new DecoderOptions { Configuration = configuration };
            }
        }
    }
}
