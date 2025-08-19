using Microsoft.ML;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Numerics.Tensors;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
namespace MachineLearning
{
    internal class Program
    { /// <summary>
      ///     Gets the cropped region of the source image specified by the given rectangle, clamping the rectangle coordinates to
      ///     the image bounds.
      /// </summary>
      /// <param name="rect">The rectangle to crop.</param>
      /// <param name="size">The size of the source image.</param>
      /// <returns>The cropped rectangle.</returns>
        private static Rect GetCropedRect(Rect rect, Size size)
        {
            return Rect.FromLTRB(
                Math.Clamp(rect.Left, 0, size.Width),
                Math.Clamp(rect.Top, 0, size.Height),
                Math.Clamp(rect.Right, 0, size.Width),
                Math.Clamp(rect.Bottom, 0, size.Height));
        }
        static void Main(string[] args)
        {
            var src = Cv2.ImRead("1.png");
            var det = new Det(OcrVersionConfig.PpOcrV4);
            var rects = det.Run(src);
            Mat[] mats =
                rects.Select(rect =>
                {
                    var roi = src[GetCropedRect(rect.BoundingRect(), src.Size())];
                    return roi;
                })
                    .ToArray();
            var rec = new Rec(OcrVersionConfig.PpOcrV4);
            var results = rec.Run(mats);
            foreach (var label in results)
            {
                Console.WriteLine(label);
            }

            Console.ReadLine();
        } 
    }
}