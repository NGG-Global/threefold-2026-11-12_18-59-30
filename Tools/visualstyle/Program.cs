using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Threefold.EditorTools;
using Threefold.VisualStyle;

// Renders preview images of the visual style without Unity: the painted textures exactly as the
// setup tool will generate them, and material samples lit with the C# copy of the shader's
// arithmetic (IllustratedLightingReference). The images show the textures, the palette, the
// light response and the line weight. They are not screenshots of the game: Unity's renderer,
// shadows, fog and the real meshes are not involved.
//
// Usage: preview <output folder> [--all-textures]
// Without --all-textures only two sample textures are written at full size, next to the sheets.
static class Program
{
    private const int Tile = 256;
    private static readonly Rgb Paper = new Rgb(0.93f, 0.91f, 0.86f);

    static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "previews";
        bool allTextures = Array.IndexOf(args, "--all-textures") >= 0;
        Directory.CreateDirectory(output);

        var textures = new Dictionary<string, Image>();
        foreach (StyleMaterialPreset preset in StyleMaterialPresets.All)
        {
            if (preset.Texture == null)
            {
                continue;
            }

            Image image = Image.FromRgba(PaintedTextureGenerator.Generate(preset.Texture), preset.Texture.Size);
            textures[preset.Name] = image;

            string name = preset.Texture.Name;
            if (allTextures || name == "Stone" || name == "Foliage")
            {
                image.Save(Path.Combine(output, "texture_" + name + ".png"));
            }
        }

        if (allTextures)
        {
            Image.FromRgba(PaintedTextureGenerator.GenerateVariationMap(128, 5), 128).Save(Path.Combine(output, "texture_PaintVariation.png"));
        }

        TextureSheet(textures).Save(Path.Combine(output, "textures.png"));

        LightingPreset[] lightings = { LightingPreset.GoldenAfternoon(), LightingPreset.OvercastMorning() };
        foreach (LightingPreset lighting in lightings)
        {
            string name = lighting.displayName.Replace(" ", string.Empty);
            MaterialSheet(textures, lighting, true).Save(Path.Combine(output, "materials_" + name + ".png"));
        }

        Comparison(textures, lightings[0]).Save(Path.Combine(output, "light_response_comparison.png"));
        OutlineSheet().Save(Path.Combine(output, "outline_weights.png"));

        Console.WriteLine("Wrote previews to " + Path.GetFullPath(output));
        return 0;
    }

    // ---------------------------------------------------------------- sheets

    /// <summary>Every texture, each tiled two by two and reduced, so seams and repetition would show.</summary>
    private static Image TextureSheet(Dictionary<string, Image> textures)
    {
        const int columns = 4;
        const int gap = 12;
        int rows = (textures.Count + columns - 1) / columns;
        var sheet = new Image(columns * (Tile + gap) + gap, rows * (Tile + gap) + gap, Paper);

        int index = 0;
        foreach (Image texture in textures.Values)
        {
            int originX = gap + (index % columns) * (Tile + gap);
            int originY = gap + (index / columns) * (Tile + gap);

            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    // Two repeats across the tile, averaged from the source.
                    float u = x * 2f / Tile;
                    float v = y * 2f / Tile;
                    sheet.Set(originX + x, originY + y, texture.SampleArea(u, v, 2f / Tile));
                }
            }

            index++;
        }

        return sheet;
    }

    /// <summary>For each material: a ball, which shows how light turns on a round form, and a block with one face in shade.</summary>
    private static Image MaterialSheet(Dictionary<string, Image> textures, LightingPreset lighting, bool illustrated)
    {
        IReadOnlyList<StyleMaterialPreset> presets = StyleMaterialPresets.All;
        const int cell = 190;
        const int gap = 10;
        const int columns = 7;
        int rows = (presets.Count + columns - 1) / columns;
        var sheet = new Image(columns * (cell + gap) + gap, rows * (cell * 2 + gap * 2) + gap, Paper);

        for (int i = 0; i < presets.Count; i++)
        {
            int originX = gap + (i % columns) * (cell + gap);
            int originY = gap + (i / columns) * (cell * 2 + gap * 2);
            textures.TryGetValue(presets[i].Name, out Image texture);

            DrawBall(sheet, originX, originY, cell, presets[i], texture, lighting, illustrated);
            DrawBlock(sheet, originX, originY + cell + gap, cell, presets[i], texture, lighting, illustrated);
        }

        return sheet;
    }

    /// <summary>
    /// The same stone, wood, cloth, metal and foliage three ways: flat colour under a plain
    /// Lambert response; the painted texture under the same plain response; and the painted
    /// texture under the illustrated response.
    /// </summary>
    private static Image Comparison(Dictionary<string, Image> textures, LightingPreset lighting)
    {
        string[] names = { "Stone", "Wood", "Cloth", "Metal", "Foliage" };
        const int cell = 200;
        const int gap = 10;
        var sheet = new Image(names.Length * 2 * (cell + gap) + gap, 3 * (cell + gap) + gap, Paper);

        for (int i = 0; i < names.Length; i++)
        {
            StyleMaterialPreset preset = StyleMaterialPresets.Find(StyleMaterialPresets.Prefix + names[i]);
            Image texture = textures[preset.Name];

            for (int row = 0; row < 3; row++)
            {
                int ballX = gap + (i * 2) * (cell + gap);
                int blockX = gap + (i * 2 + 1) * (cell + gap);
                int originY = gap + row * (cell + gap);
                Image used = row == 0 ? null : texture;
                bool illustrated = row == 2;

                DrawBall(sheet, ballX, originY, cell, preset, used, lighting, illustrated, row == 0);
                DrawBlock(sheet, blockX, originY, cell, preset, used, lighting, illustrated, row == 0);
            }
        }

        return sheet;
    }

    /// <summary>
    /// A figure at three distances on a 1080-pixel-high screen with a 60 degree view, with the
    /// contour line as the shader sizes it, and once without. The line keeps its width as the
    /// figure shrinks, then fades out with distance.
    /// </summary>
    private static Image OutlineSheet()
    {
        const float screenHeight = 1080f;
        const float lineWidth = 0.0022f;
        const float fadeStart = 14f;
        const float fadeEnd = 32f;
        float[] distances = { 5f, 5f, 12f, 25f, 40f };
        bool[] outlined = { false, true, true, true, true };

        var sheet = new Image(1120, 400, Paper);
        var cloth = new Rgb(0.33f, 0.42f, 0.55f);
        var skin = new Rgb(0.86f, 0.70f, 0.58f);
        int x = 110;

        for (int i = 0; i < distances.Length; i++)
        {
            // Height on screen of a 1.8 m figure: 1.8 / (2 d tan(30 degrees)) of the screen.
            float figureHeight = 1.8f / (2f * distances[i] * 0.57735f) * screenHeight;
            float width = outlined[i]
                ? OutlineRules.WidthPixels(lineWidth, screenHeight, distances[i], fadeStart, fadeEnd)
                : 0f;

            DrawFigure(sheet, x, 370, figureHeight, width, cloth, skin);
            x += (int)(figureHeight * 0.45f) + 150;
        }

        return sheet;
    }

    // --------------------------------------------------------------- drawing

    private static void DrawBall(Image sheet, int originX, int originY, int cell, StyleMaterialPreset preset,
        Image texture, LightingPreset lighting, bool illustrated, bool flatColour = false)
    {
        float radius = cell * 0.44f;
        float centre = cell * 0.5f;
        Vec light = SunDirection(lighting);

        for (int y = 0; y < cell; y++)
        {
            for (int x = 0; x < cell; x++)
            {
                // Four samples per pixel for a clean edge.
                Rgb sum = new Rgb(0f, 0f, 0f);
                int hits = 0;
                for (int s = 0; s < 4; s++)
                {
                    float px = (x + 0.25f + 0.5f * (s % 2) - centre) / radius;
                    float py = (centre - (y + 0.25f + 0.5f * (s / 2))) / radius;
                    float d = px * px + py * py;
                    if (d > 1f)
                    {
                        continue;
                    }

                    var normal = new Vec(px, py, -(float)Math.Sqrt(1f - d));
                    float u = 0.5f + px * 0.2f;
                    float v = 0.5f + py * 0.2f;
                    sum += Lit(preset, texture, u, v, normal, light, lighting, illustrated, flatColour);
                    hits++;
                }

                if (hits > 0)
                {
                    Rgb colour = sum.Scaled(1f / hits);
                    sheet.Blend(originX + x, originY + y, colour, hits / 4f);
                }
            }
        }
    }

    private static void DrawBlock(Image sheet, int originX, int originY, int cell, StyleMaterialPreset preset,
        Image texture, LightingPreset lighting, bool illustrated, bool flatColour = false)
    {
        Vec light = SunDirection(lighting);
        float size = cell * 0.5f;
        float baseX = originX + cell * 0.5f;
        float baseY = originY + cell * 0.92f;
        const float uvScale = 0.375f;

        for (int y = 0; y < cell; y++)
        {
            for (int x = 0; x < cell; x++)
            {
                float sx = (originX + x + 0.5f - baseX) / size;
                float sy = (baseY - (originY + y + 0.5f)) / size;

                // Top face: points (px, 1, pz).
                float a = sx / 0.866f;
                float b = (sy - 1f) / 0.5f;
                float px = (a + b) * 0.5f;
                float pz = (b - a) * 0.5f;
                if (px >= 0f && px <= 1f && pz >= 0f && pz <= 1f)
                {
                    sheet.Set(originX + x, originY + y,
                        Lit(preset, texture, px * uvScale, pz * uvScale, new Vec(0f, 1f, 0f), light, lighting, illustrated, flatColour));
                    continue;
                }

                // Right face, towards the camera: points (px, py, 0).
                px = sx / 0.866f;
                float py = sy - px * 0.5f;
                if (px >= 0f && px <= 1f && py >= 0f && py <= 1f)
                {
                    sheet.Set(originX + x, originY + y,
                        Lit(preset, texture, px * uvScale, py * uvScale, new Vec(0f, 0f, -1f), light, lighting, illustrated, flatColour));
                    continue;
                }

                // Left face: points (0, py, pz).
                pz = -sx / 0.866f;
                py = sy - pz * 0.5f;
                if (pz >= 0f && pz <= 1f && py >= 0f && py <= 1f)
                {
                    sheet.Set(originX + x, originY + y,
                        Lit(preset, texture, pz * uvScale, py * uvScale, new Vec(-1f, 0f, 0f), light, lighting, illustrated, flatColour));
                }
            }
        }
    }

    private static void DrawFigure(Image sheet, int centreX, int feetY, float height, float lineWidth, Rgb cloth, Rgb skin)
    {
        float scale = height / 100f;
        int reach = (int)(height * 0.4f + lineWidth + 4f);
        var contour = StylePalette.Contour;

        for (int y = feetY - (int)height - reach / 4; y <= feetY + 4; y++)
        {
            for (int x = centreX - reach; x <= centreX + reach; x++)
            {
                float coverage = 0f;
                float lineCoverage = 0f;
                bool head = false;

                for (int s = 0; s < 4; s++)
                {
                    float px = (x + 0.25f + 0.5f * (s % 2) - centreX) / scale;
                    float py = (feetY - (y + 0.25f + 0.5f * (s / 2))) / scale;
                    float distance = FigureDistance(px, py, out bool isHead) * scale;

                    if (distance <= 0f)
                    {
                        coverage += 0.25f;
                        head |= isHead;
                    }
                    else if (distance <= lineWidth)
                    {
                        lineCoverage += 0.25f;
                    }
                }

                if (lineCoverage > 0f)
                {
                    sheet.Blend(x, y, contour, lineCoverage);
                }

                if (coverage > 0f)
                {
                    sheet.Blend(x, y, head ? skin : cloth, coverage);
                }
            }
        }
    }

    /// <summary>Distance to a simple robed figure 100 units tall; negative inside.</summary>
    private static float FigureDistance(float x, float y, out bool head)
    {
        float headDistance = (float)Math.Sqrt(x * x + (y - 90f) * (y - 90f)) - 9f;

        // Robe: narrow at the shoulders, wide at the hem.
        float t = Math.Clamp(y / 80f, 0f, 1f);
        float halfWidth = 19f - 9f * t;
        float dx = Math.Abs(x) - halfWidth;
        float dy = Math.Max(-y, y - 80f);
        float robe = Math.Max(dx, dy);
        if (dx > 0f && dy > 0f)
        {
            robe = (float)Math.Sqrt(dx * dx + dy * dy);
        }

        head = headDistance < robe;
        return Math.Min(headDistance, robe);
    }

    // -------------------------------------------------------------- lighting

    private static Rgb Lit(StyleMaterialPreset preset, Image texture, float u, float v, Vec normal, Vec light,
        LightingPreset lighting, bool illustrated, bool flatColour)
    {
        Rgb surface = texture != null && !flatColour
            ? texture.Sample(u, v) * preset.Tint
            : (preset.Texture != null ? preset.Texture.Base : preset.Tint);
        Rgb albedo = surface.Linear;

        Rgb sun = new Rgb(lighting.sunColor.r, lighting.sunColor.g, lighting.sunColor.b).Linear.Scaled(lighting.sunIntensity);
        Rgb ambient = Ambient(lighting, normal.Y);
        float ndl = normal.Dot(light);

        // A convex shape shadows itself exactly where it faces away from the light.
        float shadow = 1f - lighting.shadowStrength * (ndl > 0f ? 0f : 1f);

        Rgb colour;
        if (illustrated)
        {
            var view = new Vec(0f, 0f, -1f);
            Vec half = new Vec(light.X + view.X, light.Y + view.Y, light.Z + view.Z).Normalized();
            colour = IllustratedLightingReference.Shade(albedo, sun, ambient, preset.Shade, ndl, normal.Dot(half),
                preset.Wrap, shadow, preset.HighlightColour.Linear, preset.HighlightStrength, preset.HighlightSize);
        }
        else
        {
            // The plain response of a stock matte material: N.L and untinted ambient light.
            colour = albedo * (sun.Scaled(Math.Max(0f, ndl) * shadow) + ambient);
        }

        return colour.Srgb;
    }

    private static Rgb Ambient(LightingPreset lighting, float up)
    {
        Rgb sky = new Rgb(lighting.skyColor.r, lighting.skyColor.g, lighting.skyColor.b).Linear;
        Rgb horizon = new Rgb(lighting.horizonColor.r, lighting.horizonColor.g, lighting.horizonColor.b).Linear;
        Rgb ground = new Rgb(lighting.groundColor.r, lighting.groundColor.g, lighting.groundColor.b).Linear;
        return up >= 0f ? Rgb.Lerp(horizon, sky, up) : Rgb.Lerp(horizon, ground, -up);
    }

    /// <summary>
    /// Direction towards the sun as seen in the previews. The sun's height is the preset's; it is
    /// always placed to the upper right in front of the subject, so every sheet reads the same way.
    /// </summary>
    private static Vec SunDirection(LightingPreset lighting)
    {
        double elevation = lighting.sunElevation * Math.PI / 180.0;
        double heading = 38.0 * Math.PI / 180.0;
        return new Vec((float)(Math.Sin(heading) * Math.Cos(elevation)), (float)Math.Sin(elevation),
            -(float)(Math.Cos(heading) * Math.Cos(elevation))).Normalized();
    }

    private readonly struct Vec
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Dot(Vec other) => X * other.X + Y * other.Y + Z * other.Z;

        public Vec Normalized()
        {
            float length = (float)Math.Sqrt(X * X + Y * Y + Z * Z);
            return length > 0f ? new Vec(X / length, Y / length, Z / length) : this;
        }
    }

    // ----------------------------------------------------------------- image

    private sealed class Image
    {
        public readonly int Width;
        public readonly int Height;
        private readonly float[] data;

        public Image(int width, int height, Rgb fill)
        {
            Width = width;
            Height = height;
            data = new float[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                data[i * 3] = fill.R;
                data[i * 3 + 1] = fill.G;
                data[i * 3 + 2] = fill.B;
            }
        }

        /// <summary>From generator output: RGBA bytes, first row at the bottom.</summary>
        public static Image FromRgba(byte[] rgba, int size)
        {
            var image = new Image(size, size, new Rgb(0f, 0f, 0f));
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int source = ((size - 1 - y) * size + x) * 4;
                    image.Set(x, y, new Rgb(rgba[source] / 255f, rgba[source + 1] / 255f, rgba[source + 2] / 255f));
                }
            }

            return image;
        }

        public void Set(int x, int y, Rgb colour)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }

            int index = (y * Width + x) * 3;
            data[index] = colour.R;
            data[index + 1] = colour.G;
            data[index + 2] = colour.B;
        }

        public Rgb Get(int x, int y)
        {
            int index = (y * Width + x) * 3;
            return new Rgb(data[index], data[index + 1], data[index + 2]);
        }

        public void Blend(int x, int y, Rgb colour, float amount)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }

            Set(x, y, Rgb.Lerp(Get(x, y), colour, amount));
        }

        /// <summary>Bilinear, repeating. v runs upwards, as in a texture.</summary>
        public Rgb Sample(float u, float v)
        {
            float x = (u - (float)Math.Floor(u)) * Width - 0.5f;
            float y = (1f - (v - (float)Math.Floor(v))) * Height - 0.5f;
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            float fx = x - x0;
            float fy = y - y0;

            Rgb top = Rgb.Lerp(Wrapped(x0, y0), Wrapped(x0 + 1, y0), fx);
            Rgb bottom = Rgb.Lerp(Wrapped(x0, y0 + 1), Wrapped(x0 + 1, y0 + 1), fx);
            return Rgb.Lerp(top, bottom, fy);
        }

        /// <summary>The average over a square of the texture, for reducing without shimmer.</summary>
        public Rgb SampleArea(float u, float v, float extent)
        {
            const int steps = 4;
            var sum = new Rgb(0f, 0f, 0f);
            for (int j = 0; j < steps; j++)
            {
                for (int i = 0; i < steps; i++)
                {
                    sum += Sample(u + extent * (i + 0.5f) / steps, 1f - (v + extent * (j + 0.5f) / steps));
                }
            }

            return sum.Scaled(1f / (steps * steps));
        }

        private Rgb Wrapped(int x, int y)
        {
            x = ((x % Width) + Width) % Width;
            y = ((y % Height) + Height) % Height;
            return Get(x, y);
        }

        public void Save(string path)
        {
            var raw = new byte[Height * (Width * 3 + 1)];
            int offset = 0;
            for (int y = 0; y < Height; y++)
            {
                raw[offset++] = 0;
                for (int x = 0; x < Width * 3; x++)
                {
                    float value = data[y * Width * 3 + x];
                    raw[offset++] = (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
                }
            }

            using var file = File.Create(path);
            file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            var header = new byte[13];
            WriteInt(header, 0, Width);
            WriteInt(header, 4, Height);
            header[8] = 8;
            header[9] = 2;
            Chunk(file, "IHDR", header);

            using (var compressed = new MemoryStream())
            {
                using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                {
                    zlib.Write(raw, 0, raw.Length);
                }

                Chunk(file, "IDAT", compressed.ToArray());
            }

            Chunk(file, "IEND", Array.Empty<byte>());
        }

        private static void Chunk(Stream stream, string type, byte[] content)
        {
            var length = new byte[4];
            WriteInt(length, 0, content.Length);
            stream.Write(length);

            var typed = new byte[4 + content.Length];
            for (int i = 0; i < 4; i++)
            {
                typed[i] = (byte)type[i];
            }

            Array.Copy(content, 0, typed, 4, content.Length);
            stream.Write(typed);

            uint crc = 0xFFFFFFFFu;
            foreach (byte value in typed)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }

            var checksum = new byte[4];
            WriteInt(checksum, 0, (int)(crc ^ 0xFFFFFFFFu));
            stream.Write(checksum);
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
