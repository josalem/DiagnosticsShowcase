using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageMage
{
    class Program
    {
        // switch to TelemetryCacheBounded to use the bounded
        static TelemetryCacheUnbounded cache = new();
        static string[] Transforms = new string[] { "ppm", "ppm-slow", "ppm-bulk", "greyscale", "history" };
        static void Main(string[] args)
        {
            if (args.Length < 1) throw new ArgumentException("Usage: ImageMage <imagefile>");

            var imageFileInfo = new FileInfo(args[0]);
            if (!imageFileInfo.Exists)
                throw new ArgumentException($"Image file '{args[0]}' does not exist");

            Console.WriteLine($"Valid transforms: {Transforms.Aggregate((s1, s2) => $"{s1}, {s2}")}");
            Console.WriteLine("Type 'quit' to quit.");

            while (true)
            {
                Console.Write("Type a transform: ");
                var transform = Console.ReadLine();

                if (transform == "quit")
                    break;

                if (!Transforms.Contains(transform))
                {
                    Console.WriteLine($"Unknown transform!!");
                    continue;
                }

                var sw = new Stopwatch();
                sw.Start();
                switch (transform.ToLowerInvariant())
                {
                    case "ppm":
                        ConvertToPPM(imageFileInfo);
                        break;
                    case "ppm-slow":
                        ConvertToPPMSlow(imageFileInfo);
                        break;
                    case "greyscale":
                        ConvertToGreyscale(imageFileInfo);
                        break;
                    case "history":
                        PrintHistory();
                        break;
                    case "ppm-bulk":
                        for (int i = 0; i < 256; i++)
                            ConvertToPPM(imageFileInfo);
                        break;
                    default:
                        break;
                }
                sw.Stop();

                Console.WriteLine($"Time elapsed: {sw.Elapsed.ToString(@"hh\:mm\:ss\.ffff")}");
            }

            Console.WriteLine("Goodbye!");
        }

        private static void PrintHistory()
        {
            Console.WriteLine($"History ({cache.Count()}):");
            foreach (Telemetry tel in cache)
            {
                Console.WriteLine($"* {tel}");
            }
        }

        private static void ConvertToGreyscale(FileInfo imageFileInfo)
        {
            if (File.Exists("out.ppm"))
                File.Delete("out.ppm");

            using (Image<Rgba32> image = Image.Load<Rgba32>(imageFileInfo.FullName))
            using (var outFile = File.OpenWrite("out.ppm"))
            using (var writer = new BinaryWriter(outFile))
            {
                cache.AddTelemetry(image, "greyscale");
                image.Mutate(x => x.Resize(250, 250));
                var sb = new StringBuilder();
                sb.Append($"P3\n{image.Width} {image.Height}\n255\n");
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        // this line will cause the new value to be larger than a byte and throw a
                        // System.OverflowException at runtime! The fix is changing `1.11` to `0.11`.
                        byte newPixelValue = checked((byte)(0.3 * image[x, y].R + 0.59 * image[x, y].G + 1.11 * image[x, y].B));
                        // byte newPixelValue = checked((byte)(0.3 * image[x,y].R + 0.59 * image[x,y].G + 0.11 * image[x,y].B));

                        sb.Append($"{newPixelValue} {newPixelValue} {newPixelValue} ");
                    }
                    sb.Append('\n');
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                writer.Write(bytes);
            }
        }

        private static void ConvertToPPM(FileInfo imageFileInfo)
        {
            if (File.Exists("out.ppm"))
                File.Delete("out.ppm");

            using (Image<Rgba32> image = Image.Load<Rgba32>(imageFileInfo.FullName))
            using (var outFile = File.OpenWrite("out.ppm"))
            using (var writer = new BinaryWriter(outFile))
            {
                cache.AddTelemetry(image, "ppm");
                image.Mutate(x => x.Resize(250, 250));
                var sb = new StringBuilder();
                sb.Append("P3\n");
                sb.Append($"{image.Width} {image.Height}\n");
                sb.Append("255\n");
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        sb.Append($"{image[x, y].R} {image[x, y].G} {image[x, y].B} ");
                    }
                    sb.Append("\n");
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                writer.Write(bytes);
            }
        }

        // The intent of this method is consume a high amount of CPU
        // and run slowly. The broken approach will
        // concatenate a bunch of short strings to one string
        // in a tight loop. The "fixed" method will use
        // a StringBuilder.
        private static void ConvertToPPMSlow(FileInfo imageFileInfo)
        {
            if (File.Exists("out.ppm"))
                File.Delete("out.ppm");

            using (Image<Rgba32> image = Image.Load<Rgba32>(imageFileInfo.FullName))
            using (var outFile = File.OpenWrite("out.ppm"))
            using (var writer = new BinaryWriter(outFile))
            {
                cache.AddTelemetry(image, "ppm-slow");
                image.Mutate(x => x.Resize(250, 250));
                var outString = "";
                outString += "P3\n";
                outString += $"{image.Width} {image.Height}\n";
                outString += "255\n";
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        outString += $"{image[x, y].R} {image[x, y].G} {image[x, y].B} ";
                    }
                    outString += "\n";
                }

                var bytes = Encoding.UTF8.GetBytes(outString);
                writer.Write(bytes);
            }
        }

        // The intent of this code is to leak memory.
        // The broken approach will naively cache without bounds causing the memory footprint to slowly grow.
        // The "fixed" method will add a bound to the cache.
        // This naive implementation stores the whole image
        private class Telemetry
        {
            private ImageMetadata metadata;
            private string conversion;
            private DateTime timestamp = DateTime.Now;
            private Guid id = Guid.NewGuid();
            private Byte[] buf = new Byte[1024 * 1024];
            
            public Telemetry(Image image, string conversion)
            {
                this.metadata = image.Metadata.DeepClone();
                this.conversion = conversion;
            }

            public override string ToString() => $"[{timestamp:MM/dd/yyyy hh:mm:ss.ffff}] conversion: '{conversion}', horizontal resolution: {metadata.HorizontalResolution}, vertical resolution: {metadata.VerticalResolution}";
        }

        private interface TelemetryCache : IEnumerable<Telemetry>
        {
            public void AddTelemetry(Image image, string conversion);
        }

        // this naive cache is unbounded and will continually grow
        private class TelemetryCacheUnbounded : TelemetryCache
        {
            private List<Telemetry> cache = new();

            public void AddTelemetry(Image image, string conversion) => cache.Add(new Telemetry(image, conversion));

            public IEnumerator<Telemetry> GetEnumerator() => cache.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => cache.GetEnumerator();
        }

        // switch to this implementation to have a bounded size for the cache
        private class TelemetryCacheBounded : TelemetryCache
        {
            private Queue<Telemetry> cache = new();

            public void AddTelemetry(Image image, string conversion)
            {
                if (cache.Count == 10)
                    cache.Dequeue();

                cache.Enqueue(new Telemetry(image, conversion));
            }

            public IEnumerator<Telemetry> GetEnumerator() => cache.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => cache.GetEnumerator();
        }
    }
}
