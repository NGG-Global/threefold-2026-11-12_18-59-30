using System;
using System.IO;
using System.IO.Compression;
using Threefold.Magic;
using Threefold.TouchControls;
using UnityEngine;

// Draws the mobile control layout from MobileControlsLayout, the numbers the setup command builds
// the controls from. Each picture has three panels: the controls at rest; moving while charging and
// aiming (with the touch areas, the joystick's activation and sprint radii, and the cancel zone);
// and the discipline selector open. Diagrams only: the game's icons, fonts and renderer are not used.
//
// Usage: preview <output folder>
static class Program
{
    private const float PixelsPerDp = 2f;

    static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "previews";
        Directory.CreateDirectory(output);

        Render(Path.Combine(output, "layout_phone.png"), new Vector2(740f, 340f), "PHONE 740 X 340 DP");
        Render(Path.Combine(output, "layout_tablet.png"), new Vector2(1280f, 800f), "TABLET 1280 X 800 DP");
        return 0;
    }

    private static void Render(string path, Vector2 safeArea, string title)
    {
        int panelWidth = (int)(safeArea.x * PixelsPerDp);
        int panelHeight = (int)(safeArea.y * PixelsPerDp);
        int gap = 24;
        int caption = 30;
        var canvas = new Canvas(panelWidth, (panelHeight + caption + gap) * 3 + 40);
        var text = new Rgb(0.95f, 0.93f, 0.88f);
        canvas.Fill(new Rgb(0.16f, 0.16f, 0.17f));
        canvas.Text(gap, 12, title, 3, text);

        string[] captions = { "1  AT REST", "2  MOVING  CHARGING AND AIMING  TOUCH AREAS SHOWN", "3  DISCIPLINE SELECTOR OPEN" };
        for (int i = 0; i < 3; i++)
        {
            int top = 40 + gap + i * (panelHeight + caption + gap);
            canvas.Text(gap, top, captions[i], 2, text);
            DrawPanel(canvas, 0, top + caption, safeArea, (Panel)i);
        }

        canvas.SavePng(path);
        Console.WriteLine($"Wrote {path}");
    }

    private enum Panel
    {
        Rest,
        Charging,
        Selector,
    }

    private static void DrawPanel(Canvas canvas, int left, int top, Vector2 safeArea, Panel panel)
    {
        bool active = panel == Panel.Charging;
        MobileControlsLayout layout = MobileControlsLayout.Default;
        int height = (int)(safeArea.y * PixelsPerDp);

        // A muted landscape stands in for the game view.
        for (int y = 0; y < height; y++)
        {
            float t = (float)y / height;
            Rgb sky = Rgb.Lerp(new Rgb(0.55f, 0.64f, 0.72f), new Rgb(0.78f, 0.76f, 0.66f), t * 1.6f);
            Rgb ground = Rgb.Lerp(new Rgb(0.45f, 0.5f, 0.33f), new Rgb(0.33f, 0.36f, 0.24f), (t - 0.6f) * 2.5f);
            canvas.Row(left, top + y, (int)(safeArea.x * PixelsPerDp), t < 0.6f ? sky : ground);
        }

        Vector2 Px(Vector2 dp) => new Vector2(left + dp.x * PixelsPerDp, top + (safeArea.y - dp.y) * PixelsPerDp);

        var ivory = new Rgb(1f, 0.96f, 0.86f);
        var dark = new Rgb(0.1f, 0.1f, 0.1f);
        Rgb arcane = Rgb.From(DisciplineCatalog.Get(0).Color);

        // Joystick: ring, knob, and while active the activation and sprint radii.
        Vector2 stick = Px(layout.Joystick.Resolve(safeArea));
        float r = PixelsPerDp;
        if (active)
        {
            canvas.Ring(stick, layout.JoystickActivationRadius * r, 2f, ivory, 0.5f);
            canvas.Ring(stick, layout.JoystickTravel * 1.45f * r, 2f, new Rgb(1f, 0.84f, 0.5f), 0.7f);
        }

        canvas.Disc(stick, layout.Joystick.Radius * r, ivory, 0.15f);
        canvas.Ring(stick, layout.Joystick.Radius * r, 3f, ivory, 0.7f);
        Vector2 knob = active ? stick + new Vector2(0f, -layout.JoystickTravel * 0.8f * r) : stick;
        canvas.Disc(knob, layout.JoystickKnobDiameter * 0.5f * r, ivory, 0.6f);
        canvas.Label(stick + new Vector2(0f, (layout.Joystick.Radius + 14f) * r), "MOVE", ivory);

        void Button(ControlPlacement placement, string label, float alpha = 0.3f)
        {
            Vector2 centre = Px(placement.Resolve(safeArea));
            canvas.Disc(centre, placement.Radius * r, ivory, alpha);
            canvas.Ring(centre, placement.Radius * r, 3f, ivory, 0.75f);
            if (active)
            {
                canvas.Ring(centre, placement.TouchDiameter * 0.5f * r, 1.5f, ivory, 0.35f);
            }

            canvas.Label(centre, label, dark);
        }

        Button(layout.Block, "BLOCK");
        Button(layout.Jump, "JUMP");
        Button(layout.Interact, "USE", 0.18f);

        Vector2 attack = Px(layout.Attack.Resolve(safeArea));
        canvas.Disc(attack, layout.Attack.Radius * r, active ? Rgb.Lerp(ivory, arcane, 0.5f) : ivory, active ? 0.6f : 0.3f);
        canvas.Ring(attack, layout.Attack.Radius * r, 4f, arcane, 0.95f);
        if (active)
        {
            canvas.Arc(attack, (layout.Attack.Radius + 6f) * r, 7f, arcane, 0.95f, 0.6f);
            canvas.Ring(attack, layout.AimRadius * r, 2f, ivory, 0.6f);
            canvas.Disc(attack + new Vector2(-0.6f * layout.AimRadius * r, -0.3f * layout.AimRadius * r), 13f * r, arcane, 0.95f);
            canvas.Ring(attack, layout.Attack.TouchDiameter * 0.5f * r, 1.5f, ivory, 0.35f);
        }

        canvas.Label(attack, "ATTACK", dark);

        Vector2 indicator = Px(layout.Indicator.Resolve(safeArea));
        canvas.Disc(indicator, layout.Indicator.Radius * r, arcane, 0.9f);
        canvas.Ring(indicator, layout.Indicator.Radius * r, 2f, ivory, 0.9f);

        if (panel == Panel.Charging)
        {
            canvas.Ring(indicator, layout.Indicator.TouchDiameter * 0.5f * r, 1.5f, ivory, 0.35f);

            // The cancel zone for a press at the centre of ATTACK.
            var bounds = new Rect(Vector2.zero, safeArea);
            Vector2 cancelDp = MobileControlsLayout.PlaceCancelZone(layout.Attack.Resolve(safeArea), layout.CancelOffset,
                layout.CancelDiameter * 0.5f, layout.CancelEdgeMargin, bounds);
            Vector2 cancel = Px(cancelDp);
            canvas.Disc(cancel, layout.CancelDiameter * 0.5f * r, new Rgb(0.6f, 0.15f, 0.12f), 0.45f);
            canvas.Ring(cancel, layout.CancelDiameter * 0.5f * r, 3f, new Rgb(0.95f, 0.4f, 0.35f), 0.9f);
            canvas.Label(cancel, "CANCEL", ivory);
            return;
        }

        if (panel != Panel.Selector)
        {
            return;
        }

        canvas.Ring(indicator, layout.Indicator.Radius * r + 6f, 3f, ivory, 0.9f);
        Vector2[] slots = layout.FanSlotOffsetsFromAttack(DisciplineCatalog.Count);
        for (int i = 0; i < slots.Length; i++)
        {
            DisciplineInfo info = DisciplineCatalog.Get(i);
            Vector2 centre = Px(layout.Attack.Resolve(safeArea) + slots[i]);
            canvas.Disc(centre, layout.SlotDiameter * 0.5f * r, new Rgb(0.12f, 0.11f, 0.1f), 0.6f);
            canvas.Ring(centre, layout.SlotDiameter * 0.5f * r, 4f, Rgb.From(info.Color), 0.95f);
            canvas.Label(centre, info.Name.ToUpperInvariant(), Rgb.From(info.Color));
        }
    }

    private readonly struct Rgb
    {
        public readonly float R, G, B;

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static Rgb From(Color color) => new Rgb(color.r, color.g, color.b);

        public static Rgb Lerp(Rgb a, Rgb b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }
    }

    private sealed class Canvas
    {
        private readonly int width;
        private readonly int height;
        private readonly Rgb[] pixels;

        public Canvas(int width, int height)
        {
            this.width = width;
            this.height = height;
            pixels = new Rgb[width * height];
        }

        public void Fill(Rgb color)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
        }

        public void Row(int x, int y, int length, Rgb color)
        {
            for (int i = 0; i < length; i++)
            {
                Blend(x + i, y, color, 1f);
            }
        }

        public void Disc(Vector2 centre, float radius, Rgb color, float alpha)
        {
            Shade(centre, radius + 1f, distance => Coverage(radius - distance), color, alpha);
        }

        public void Ring(Vector2 centre, float radius, float thickness, Rgb color, float alpha)
        {
            Shade(centre, radius + thickness, distance => Coverage(thickness * 0.5f - Math.Abs(distance - radius)), color, alpha);
        }

        /// <summary>A ring drawn clockwise from the top for the given share of a full turn.</summary>
        public void Arc(Vector2 centre, float radius, float thickness, Rgb color, float alpha, float share)
        {
            int extent = (int)(radius + thickness) + 1;
            for (int y = -extent; y <= extent; y++)
            {
                for (int x = -extent; x <= extent; x++)
                {
                    float angle = (float)(Math.Atan2(x, -y) / (2 * Math.PI));
                    if (angle < 0f)
                    {
                        angle += 1f;
                    }

                    if (angle > share)
                    {
                        continue;
                    }

                    float distance = (float)Math.Sqrt(x * x + y * y);
                    float cover = Coverage(thickness * 0.5f - Math.Abs(distance - radius));
                    Blend((int)centre.x + x, (int)centre.y + y, color, alpha * cover);
                }
            }
        }

        public void Label(Vector2 centre, string text, Rgb color)
        {
            int scale = 2;
            int textWidth = text.Length * 6 * scale - scale;
            Text((int)(centre.x - textWidth * 0.5f), (int)(centre.y - 3.5f * scale), text, scale, color);
        }

        public void Text(int x, int y, string text, int scale, Rgb color)
        {
            foreach (char c in text)
            {
                string[] glyph = Font.Get(c);
                for (int row = 0; row < 7; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if (glyph[row][column] != '#')
                        {
                            continue;
                        }

                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                Blend(x + column * scale + sx, y + row * scale + sy, color, 1f);
                            }
                        }
                    }
                }

                x += 6 * scale;
            }
        }

        public void SavePng(string path)
        {
            using var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    Rgb p = pixels[y * width + x];
                    raw.WriteByte(ToByte(p.R));
                    raw.WriteByte(ToByte(p.G));
                    raw.WriteByte(ToByte(p.B));
                }
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, true))
            {
                raw.Position = 0;
                raw.CopyTo(zlib);
            }

            using var file = File.Create(path);
            file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var header = new byte[13];
            WriteInt(header, 0, width);
            WriteInt(header, 4, height);
            header[8] = 8;
            header[9] = 2;
            Chunk(file, "IHDR", header);
            Chunk(file, "IDAT", compressed.ToArray());
            Chunk(file, "IEND", Array.Empty<byte>());
        }

        private void Shade(Vector2 centre, float extentRadius, Func<float, float> coverage, Rgb color, float alpha)
        {
            int extent = (int)extentRadius + 2;
            for (int y = -extent; y <= extent; y++)
            {
                for (int x = -extent; x <= extent; x++)
                {
                    float distance = (float)Math.Sqrt(x * x + y * y);
                    float cover = coverage(distance);
                    if (cover > 0f)
                    {
                        Blend((int)centre.x + x, (int)centre.y + y, color, alpha * cover);
                    }
                }
            }
        }

        private static float Coverage(float signedInside) => Math.Clamp(signedInside + 0.5f, 0f, 1f);

        private void Blend(int x, int y, Rgb color, float alpha)
        {
            if (x < 0 || y < 0 || x >= width || y >= height || alpha <= 0f)
            {
                return;
            }

            int i = y * width + x;
            pixels[i] = Rgb.Lerp(pixels[i], color, alpha);
        }

        private static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value * 255f), 0, 255);

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void Chunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteInt(length, 0, data.Length);
            stream.Write(length);
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes);
            stream.Write(data);
            uint crc = Crc32(data, Crc32(typeBytes, 0xFFFFFFFFu)) ^ 0xFFFFFFFFu;
            var crcBytes = new byte[4];
            WriteInt(crcBytes, 0, (int)crc);
            stream.Write(crcBytes);
        }

        /// <summary>Continues a PNG CRC-32 (polynomial 0xEDB88320) over the bytes.</summary>
        private static uint Crc32(byte[] bytes, uint crc)
        {
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }

            return crc;
        }
    }

    private static class Font
    {
        private static readonly string[] Blank = { ".....", ".....", ".....", ".....", ".....", ".....", "....." };

        public static string[] Get(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'A': return new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" };
                case 'B': return new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." };
                case 'C': return new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" };
                case 'D': return new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." };
                case 'E': return new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" };
                case 'F': return new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." };
                case 'G': return new[] { ".####", "#....", "#....", "#.###", "#...#", "#...#", ".###." };
                case 'H': return new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" };
                case 'I': return new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" };
                case 'J': return new[] { "..###", "...#.", "...#.", "...#.", "#..#.", "#..#.", ".##.." };
                case 'K': return new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" };
                case 'L': return new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" };
                case 'M': return new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" };
                case 'N': return new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" };
                case 'O': return new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." };
                case 'P': return new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." };
                case 'R': return new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" };
                case 'S': return new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." };
                case 'T': return new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." };
                case 'U': return new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." };
                case 'V': return new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." };
                case 'W': return new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" };
                case 'X': return new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" };
                case 'Y': return new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." };
                case '0': return new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." };
                case '1': return new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." };
                case '2': return new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" };
                case '3': return new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." };
                case '4': return new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." };
                case '5': return new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." };
                case '6': return new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." };
                case '7': return new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." };
                case '8': return new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." };
                case '9': return new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." };
                default: return Blank;
            }
        }
    }
}
