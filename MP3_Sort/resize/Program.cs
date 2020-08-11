using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace resize
{
    class Program
    {
        private static Bitmap ResizeImage(Image image, int width, int height, bool watermarkonly)
        {
            if (watermarkonly)
                return new Bitmap(image);

            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        // Copy the watermark image over the result image.
        private static void DrawWatermark(Bitmap watermark_bm, Bitmap result_bm, int x = 0, int y = 0)
        {
            //const byte ALPHA = 128;
            //// Set the watermark's pixels' Alpha components.
            //Color clr;
            //for (int py = 0; py < watermark_bm.Height; py++)
            //{
            //    for (int px = 0; px < watermark_bm.Width; px++)
            //    {
            //        clr = watermark_bm.GetPixel(px, py);
            //        watermark_bm.SetPixel(px, py,
            //            Color.FromArgb(ALPHA, clr.R, clr.G, clr.B));
            //    }
            //}

            //// Set the watermark's transparent color.
            //watermark_bm.MakeTransparent(watermark_bm.GetPixel(0, 0));

            // Copy onto the result image.
            using (Graphics gr = Graphics.FromImage(result_bm))
            {
                gr.DrawImage(watermark_bm, x, y);
            }
        }

        static bool IsValidImage(string filename)
        {
            FileInfo file = new FileInfo(filename);
            string ext = file.Extension;
            List<string> allowedExts = new List<string>(new string[] { ".jpg", ".jpeg", ".png" });

            return allowedExts.Contains(ext);
        }

        static bool IsAIFFAudio(string filename)
        {
            FileInfo file = new FileInfo(filename);
            string ext = file.Extension;
            List<string> allowedExts = new List<string>(new string[] { ".aiff" });

            return allowedExts.Contains(ext);
        }

        static Bitmap TransformImage(Image img, string watermark = "", bool watermarkonly = false)
        {
            Bitmap bitImg = ResizeImage(img, 500, 500, watermarkonly);

            if (watermark.Length > 0)
                DrawWatermark(new Bitmap(watermark), bitImg);

            return bitImg;
        }

        static void Main(string[] args)
        {
            if (args.Length < 1)
                return;

            Console.WriteLine(args[0]);
            List<string> argList = new List<string>(args);
            bool watermarkonly = argList.Contains("watermarkonly");
            string waterPath = "";

            if(watermarkonly)
            {
                waterPath = (argList.Count == 3 ? args[1] : "");
            }
            else
            {
                waterPath = (argList.Count == 2 ? args[1] : "");
            }

            try
            {
                bool isImage = IsValidImage(args[0]);          

                if (isImage)
                {
                    MemoryStream inputStream = new MemoryStream(File.ReadAllBytes(args[0]));
                    Bitmap bitImg = TransformImage(Image.FromStream(inputStream), (waterPath.Length > 0 ? waterPath : ""), watermarkonly);
                    bitImg.Save(args[0]);
                }
                else
                {
                    bool isAiff = IsAIFFAudio(args[0]);

                    if (isAiff)
                    {
                        ATL.Track track = new ATL.Track(args[0]);
                        MemoryStream inputStream = new MemoryStream(track.EmbeddedPictures[0].PictureData);
                        Bitmap bitImg = TransformImage(Image.FromStream(inputStream), (waterPath.Length > 0 ? waterPath : ""), watermarkonly);
                        MemoryStream outputStream = new MemoryStream();
                        bitImg.Save(outputStream, ImageFormat.Jpeg);

                        ATL.PictureInfo pic = track.EmbeddedPictures[0];
                        pic = ATL.PictureInfo.fromBinaryData(outputStream.ToArray(), pic.PicType, pic.TagType, pic.NativePicCodeStr, pic.Position);
                        track.EmbeddedPictures.Clear();
                        track.EmbeddedPictures.Insert(0, pic);
                        track.Save();
                    }
                    else
                    {
                        TagLib.File track = TagLib.File.Create(args[0]);
                        MemoryStream inputStream = new MemoryStream(track.Tag.Pictures[0].Data.Data);
                        Bitmap bitImg = TransformImage(Image.FromStream(inputStream), (waterPath.Length > 0 ? waterPath : ""), watermarkonly);
                        MemoryStream outputStream = new MemoryStream();
                        bitImg.Save(outputStream, ImageFormat.Jpeg);
                        track.Tag.Pictures[0].Data = outputStream.ToArray();
                        track.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            Console.WriteLine("Done");
        }
    }
}
