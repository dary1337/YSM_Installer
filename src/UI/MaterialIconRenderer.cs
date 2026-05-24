using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using Svg;

namespace YSMInstaller {
    /// <summary>
    /// Renders the embedded Material Symbols SVG icons to anti-aliased bitmaps, tinted to any color via
    /// an alpha-preserving color matrix. Results are cached by (key, size, color) for paint performance.
    ///
    /// Threading: <see cref="Get"/> is the only public entry point and acquires <c>Gate</c> for the full
    /// render path (cache lookup, SVG load, rasterize, cache insert). All cache mutations are inside
    /// that lock, so concurrent calls from paint threads are safe.
    ///
    /// Ownership: returned <see cref="Bitmap"/> instances are shared cache entries — callers must not
    /// dispose them. The cache holds the bitmaps for the lifetime of the AppDomain.
    /// </summary>
    public static class MaterialIconRenderer {
        private static readonly Dictionary<string, Bitmap> Cache =
            new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SvgDocument?> DocCache =
            new Dictionary<string, SvgDocument?>(StringComparer.Ordinal);
        private static readonly object Gate = new object();

        public static Bitmap Get(string key, int size, Color color) {
            if (size < 1) {
                size = 1;
            }
            string cacheKey = $"{key}|{size}|{color.ToArgb()}";
            lock (Gate) {
                if (Cache.TryGetValue(cacheKey, out Bitmap cached)) {
                    return cached;
                }

                Bitmap result = Render(key, size, color);
                Cache[cacheKey] = result;
                return result;
            }
        }

        private static Bitmap Render(string key, int size, Color color) {
            SvgDocument? doc = LoadDocument(key);
            if (doc == null) {
                return new Bitmap(size, size, PixelFormat.Format32bppArgb);
            }

            // Rasterize at 4× (capped at 96) so SVGs with thin strokes (like steam.svg) keep visible
            // lines after downsampling, then downscale with bicubic into the requested icon size.
            int renderSize = Math.Min(size * 4, 96);
            if (renderSize < size) {
                renderSize = size;
            }

            using (Bitmap raw = doc.Draw(renderSize, renderSize)) {
                if (renderSize == size) {
                    return Tint(raw, color);
                }
                using (var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb)) {
                    using (Graphics g = Graphics.FromImage(resized)) {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawImage(raw, new Rectangle(0, 0, size, size));
                    }
                    return Tint(resized, color);
                }
            }
        }

        private static SvgDocument? LoadDocument(string key) {
            if (DocCache.TryGetValue(key, out SvgDocument? cached)) {
                return cached;
            }

            SvgDocument? doc = null;
            try {
                Assembly assembly = typeof(MaterialIconRenderer).Assembly;
                string suffix = $".icons.{key}.svg";
                string? resourceName = assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                if (resourceName != null) {
                    using (Stream? stream = assembly.GetManifestResourceStream(resourceName)) {
                        if (stream != null) {
                            doc = SvgDocument.Open<SvgDocument>(stream);
                        }
                    }
                }
                else {
                    AppLogger.Error($"Icon resource not found for key '{key}'.");
                }
            }
            catch (Exception exception) {
                AppLogger.Error($"Failed to load SVG icon '{key}'.", exception);
            }

            DocCache[key] = doc;
            return doc;
        }

        private static Bitmap Tint(Bitmap source, Color color) {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result)) {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var matrix = new ColorMatrix(new[] {
                    new[] { 0f, 0f, 0f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 1f, 0f },
                    new[] { color.R / 255f, color.G / 255f, color.B / 255f, 0f, 1f },
                });

                using (var attributes = new ImageAttributes()) {
                    attributes.SetColorMatrix(matrix);
                    g.DrawImage(
                        source,
                        new Rectangle(0, 0, source.Width, source.Height),
                        0, 0, source.Width, source.Height,
                        GraphicsUnit.Pixel,
                        attributes
                    );
                }
            }
            return result;
        }
    }
}
