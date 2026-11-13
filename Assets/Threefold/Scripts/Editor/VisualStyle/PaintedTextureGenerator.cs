using System;

namespace Threefold.EditorTools
{
    /// <summary>What a painted texture depicts. Each has its own structure and brushwork.</summary>
    public enum PaintedSurface
    {
        /// <summary>Colour planes and brushwork only.</summary>
        Plain,

        /// <summary>Coursed masonry: blocks of varied tone with drawn joints.</summary>
        StoneBlocks,

        /// <summary>Flagstones one metre across in a running bond.</summary>
        Flagstones,

        /// <summary>Boards with grain along their length and drawn seams.</summary>
        Planks,

        /// <summary>Vertical ridges.</summary>
        Bark,

        /// <summary>Broad washes, a faint weave and a few fold marks.</summary>
        Cloth,

        /// <summary>Smooth planes, a few fine scratches and darker patina.</summary>
        Metal,

        /// <summary>Leaf shapes grouped into masses of shadow, mid-tone and light.</summary>
        LeafMass,

        /// <summary>Short upward strokes.</summary>
        Grass,

        /// <summary>Soft blotches and small marks.</summary>
        Earth,

        /// <summary>Ground seen from above: tufts in every direction over patches of light and dark.</summary>
        Turf,
    }

    /// <summary>Everything that defines one painted texture. The same recipe always gives the same pixels.</summary>
    public sealed class PaintedTextureRecipe
    {
        public string Name = "Painted";
        public PaintedSurface Surface = PaintedSurface.Plain;

        /// <summary>The main colour.</summary>
        public Rgb Base = new Rgb(0.6f, 0.6f, 0.6f);

        /// <summary>The colour its lighter, warmer washes drift towards.</summary>
        public Rgb Warm;

        /// <summary>The colour its darker, cooler washes drift towards.</summary>
        public Rgb Cool;

        /// <summary>Colour of the drawn lines. A coloured dark, never black.</summary>
        public Rgb Ink = StylePalette.InkWarm;

        /// <summary>How strongly the lines are drawn, 0 to 1.</summary>
        public float InkStrength = 0.55f;

        /// <summary>Strength of the colour drift between washes; 1 is the default.</summary>
        public float Variation = 1f;

        /// <summary>Strength of the visible brush strokes; 1 is the default.</summary>
        public float Brushwork = 1f;

        /// <summary>Width and height in pixels. The texture covers 4 m, so 512 gives 128 pixels per metre.</summary>
        public int Size = 512;

        public int Seed = 1;

        public static PaintedTextureRecipe Create(string name, PaintedSurface surface, Rgb baseColour, Rgb ink, int seed,
            int size = 512)
        {
            return new PaintedTextureRecipe
            {
                Name = name,
                Surface = surface,
                Base = baseColour,
                Warm = (baseColour * new Rgb(1.08f, 1.02f, 0.88f)).Clamped(),
                Cool = (baseColour * new Rgb(0.88f, 0.95f, 1.07f)).Clamped(),
                Ink = ink,
                Seed = seed,
                Size = size,
            };
        }
    }

    /// <summary>
    /// Paints the base colour maps of the style in code: large planes of slightly varied colour,
    /// visible brush strokes that follow the material's direction, and drawn lines for joints and
    /// seams (the "structural marks" of Docs/ArtBible.md, section 6). Lines are thin, vary in
    /// weight, fade in places and use a coloured dark, so they read as drawn rather than printed.
    ///
    /// Every texture tiles without seams. The code uses no Unity type and its own random numbers,
    /// so it produces the same pixels in Unity, in the tests and in the preview tool.
    /// </summary>
    public static class PaintedTextureGenerator
    {
        private const float Tau = 6.2831853f;

        /// <summary>Returns the texture as RGBA bytes, four per pixel, first row at the bottom.</summary>
        public static byte[] Generate(PaintedTextureRecipe recipe)
        {
            if (recipe == null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            int size = Math.Max(16, recipe.Size);
            var canvas = new Canvas(size);
            var random = new Sequence(recipe.Seed);
            float scale = size / 512f;

            PaintGround(canvas, recipe);
            PaintBrushwork(canvas, recipe, random, scale);
            PaintDetail(canvas, recipe, random, scale);
            PaintInk(canvas, recipe);
            return canvas.ToBytes();
        }

        /// <summary>
        /// The map behind the shader's paint variation: slow drifts, red for value and green for
        /// warmth, both centred on mid grey. Returned as RGBA bytes.
        /// </summary>
        public static byte[] GenerateVariationMap(int size, int seed)
        {
            size = Math.Max(16, size);
            int count = size * size;
            var values = new float[count];
            var warmths = new float[count];
            double valueSum = 0.0;
            double warmthSum = 0.0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    int index = y * size + x;
                    values[index] = 0.6f * Noise(u, v, 3, 3, seed) + 0.4f * Noise(u, v, 7, 7, seed + 1);
                    warmths[index] = 0.6f * Noise(u, v, 2, 2, seed + 2) + 0.4f * Noise(u, v, 5, 5, seed + 3);
                    valueSum += values[index];
                    warmthSum += warmths[index];
                }
            }

            // Centred exactly on mid grey: with so few noise cells the raw average is not 0.5, and
            // an off-centre map would lighten or warm every surface instead of only varying it.
            float valueMean = (float)(valueSum / count);
            float warmthMean = (float)(warmthSum / count);
            var bytes = new byte[count * 4];

            for (int i = 0; i < count; i++)
            {
                bytes[i * 4] = ToByte(0.5f + (values[i] - valueMean) * 1.6f);
                bytes[i * 4 + 1] = ToByte(0.5f + (warmths[i] - warmthMean) * 1.6f);
                bytes[i * 4 + 2] = 128;
                bytes[i * 4 + 3] = 255;
            }

            return bytes;
        }

        // ------------------------------------------------------------ ground

        /// <summary>The underpainting: colour planes, the material's structure, and where its lines go.</summary>
        private static void PaintGround(Canvas canvas, PaintedTextureRecipe recipe)
        {
            int size = canvas.Size;
            int seed = recipe.Seed;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;

                    // Two slow washes, one warm and one cool, drift across the whole surface.
                    float warm = Smooth(Noise(u, v, 3, 3, seed + 101)) * 0.42f * recipe.Variation;
                    float cool = Smooth(Noise(u, v, 4, 4, seed + 102)) * 0.36f * recipe.Variation;

                    Structure(recipe.Surface, u, v, size, seed, out float tone, out float warmShift, out float ink);

                    Rgb colour = Rgb.Lerp(recipe.Base, recipe.Warm, Clamp01(warm + warmShift));
                    colour = Rgb.Lerp(colour, recipe.Cool, Clamp01(cool - warmShift));
                    colour = colour.Scaled(1f + tone);

                    canvas.Set(x, y, colour);
                    canvas.Ink[y * size + x] = ink;
                }
            }
        }

        /// <summary>
        /// The drawn structure of a surface at one point: how much lighter or darker its piece is,
        /// how much warmer, and how much line falls on it (0 to 1).
        /// </summary>
        private static void Structure(PaintedSurface surface, float u, float v, int size, int seed, out float tone,
            out float warmShift, out float ink)
        {
            tone = 0f;
            warmShift = 0f;
            ink = 0f;
            float scale = size / 512f;

            switch (surface)
            {
                case PaintedSurface.StoneBlocks:
                    Masonry(u, v, size, seed, 8, 3, 2, 0.011f, 0.9f, 1.0f, 0.11f, out tone, out warmShift, out ink);
                    break;

                case PaintedSurface.Flagstones:
                    Masonry(u, v, size, seed, 4, 4, 1, 0.02f, 1.2f, 1.3f, 0.13f, out tone, out warmShift, out ink);
                    break;

                case PaintedSurface.Planks:
                {
                    const int boards = 16;
                    float row = Frac(v + (Noise(u, v, 5, 5, seed + 12) - 0.5f) * 0.004f) * boards;
                    int board = (int)row % boards;
                    float inBoard = row - (float)Math.Floor(row);

                    float seamPixels = Math.Min(inBoard, 1f - inBoard) / boards * size;
                    float weight = Noise(u, v, 20, 20, seed + 21);
                    float seam = 1f - SmoothStep((0.7f + 0.8f * weight) * scale, (1.9f + 0.8f * weight) * scale, seamPixels);

                    // Each board is cut in one or two places along its length.
                    float end = 0f;
                    for (int cut = 0; cut < 2; cut++)
                    {
                        float position = Hash(board, 40 + cut, seed);
                        float distance = Math.Abs(Frac(u - position + 0.5f) - 0.5f) * size;
                        end = Math.Max(end, 1f - SmoothStep(0.8f * scale, 2.0f * scale, distance));
                    }

                    float presence = 0.4f + 0.6f * SmoothStep(0.2f, 0.6f, Noise(u, v, 9, 9, seed + 31));
                    ink = Math.Max(seam, end * 0.8f) * presence;

                    // Grain: long streaks along the board.
                    float grain = Noise(u, v, 5, 96, seed + 51 + board) - 0.5f;
                    tone = (Hash(board, 3, seed) - 0.5f) * 0.14f + grain * 0.09f;
                    warmShift = (Hash(board, 5, seed) - 0.5f) * 0.4f;
                    break;
                }

                case PaintedSurface.Bark:
                {
                    float ridge = Noise(u, v, 40, 3, seed + 12);
                    float line = 1f - SmoothStep(0.02f, 0.07f, Math.Abs(ridge - 0.5f));
                    float presence = 0.3f + 0.7f * SmoothStep(0.3f, 0.65f, Noise(u, v, 7, 7, seed + 31));
                    ink = line * presence;
                    tone = (ridge - 0.5f) * 0.22f + (Noise(u, v, 12, 4, seed + 13) - 0.5f) * 0.08f;
                    break;
                }

                case PaintedSurface.Cloth:
                {
                    // A weave that is felt more than seen.
                    float weave = (float)(Math.Sin(u * Tau * 110f) * Math.Sin(v * Tau * 110f));
                    tone = weave * 0.014f + (Noise(u, v, 6, 6, seed + 14) - 0.5f) * 0.07f;
                    warmShift = (Noise(u, v, 2, 2, seed + 15) - 0.5f) * 0.3f;
                    break;
                }

                case PaintedSurface.Metal:
                    tone = (Noise(u, v, 2, 2, seed + 16) - 0.5f) * 0.12f + (Noise(u, v, 3, 40, seed + 17) - 0.5f) * 0.03f;
                    warmShift = (Noise(u, v, 3, 3, seed + 18) - 0.5f) * 0.25f;
                    break;

                case PaintedSurface.LeafMass:
                case PaintedSurface.Grass:
                case PaintedSurface.Turf:
                    // Broad masses of light and shade; the leaves and blades are painted over them.
                    tone = (MassNoise(u, v, seed) - 0.5f) * 0.42f;
                    warmShift = (Noise(u, v, 3, 3, seed + 20) - 0.5f) * 0.45f;
                    break;

                case PaintedSurface.Earth:
                    tone = (MassNoise(u, v, seed) - 0.5f) * 0.26f;
                    warmShift = (Noise(u, v, 3, 3, seed + 20) - 0.5f) * 0.4f;
                    break;
            }
        }

        /// <summary>Where the masses of light (high) and shade (low) lie on vegetation.</summary>
        private static float MassNoise(float u, float v, int seed)
        {
            return 0.65f * Noise(u, v, 3, 3, seed + 19) + 0.35f * Noise(u, v, 7, 7, seed + 29);
        }

        /// <summary>
        /// Rows of blocks. Each row has its own number of blocks and offset, so joints never line up;
        /// the lines wobble slightly, change weight along their length and fade in places.
        /// </summary>
        private static void Masonry(float u, float v, int size, int seed, int rows, int minBlocks, int blockSpread,
            float wobble, float lineWidth, float lineSoftness, float toneRange, out float tone, out float warmShift,
            out float ink)
        {
            float scale = size / 512f;
            float uu = u + (Noise(u, v, 6, 6, seed + 11) - 0.5f) * wobble;
            float vv = v + (Noise(u, v, 6, 6, seed + 12) - 0.5f) * wobble;

            float rowPosition = Frac(vv) * rows;
            int row = (int)rowPosition % rows;
            float inRow = rowPosition - (float)Math.Floor(rowPosition);

            int blocks = minBlocks + (int)(Hash(row, 7, seed) * blockSpread * 0.999f);
            float offset = blockSpread > 1 ? Hash(row, 13, seed) : (row % 2) * 0.5f + (Hash(row, 13, seed) - 0.5f) * 0.1f;
            float blockPosition = Frac(uu + offset) * blocks;
            int block = (int)blockPosition % blocks;
            float inBlock = blockPosition - (float)Math.Floor(blockPosition);

            float toTop = (1f - inRow) / rows * size;
            float toBottom = inRow / rows * size;
            float toSide = Math.Min(inBlock, 1f - inBlock) / blocks * size;
            float toJoint = Math.Min(Math.Min(toTop, toBottom), toSide);

            float weight = Noise(u, v, 24, 24, seed + 21);
            float halfWidth = (lineWidth * (0.55f + 0.9f * weight)) * scale;
            float line = 1f - SmoothStep(halfWidth, halfWidth + lineSoftness * scale, toJoint);
            float presence = 0.35f + 0.65f * SmoothStep(0.25f, 0.6f, Noise(u, v, 10, 10, seed + 31));
            ink = line * presence;

            int id = row * 31 + block;
            tone = (Hash(id, 3, seed) - 0.5f) * toneRange;
            warmShift = (Hash(id, 5, seed) - 0.5f) * 0.5f;

            // A lighter upper edge and a darker lower edge give each block a little thickness.
            tone += 0.05f * (1f - SmoothStep(2f * scale, 9f * scale, toTop));
            tone -= 0.045f * (1f - SmoothStep(2f * scale, 8f * scale, toBottom));
        }

        // --------------------------------------------------------- brushwork

        private static void PaintBrushwork(Canvas canvas, PaintedTextureRecipe recipe, Sequence random, float scale)
        {
            int size = canvas.Size;
            float direction;
            float spread;
            float length = 46f;
            float width = 11f;
            float density = 1f;

            switch (recipe.Surface)
            {
                case PaintedSurface.Planks: direction = 0f; spread = 0.06f; length = 90f; width = 5f; density = 1.3f; break;
                case PaintedSurface.Bark: direction = Tau * 0.25f; spread = 0.12f; length = 60f; width = 6f; break;
                case PaintedSurface.Cloth: direction = Tau * 0.1f; spread = 0.25f; length = 70f; width = 16f; density = 0.8f; break;
                case PaintedSurface.Metal: direction = 0f; spread = 0.5f; length = 80f; width = 18f; density = 0.5f; break;
                case PaintedSurface.Flagstones: direction = 0f; spread = Tau * 0.5f; break;
                case PaintedSurface.LeafMass:
                case PaintedSurface.Grass:
                case PaintedSurface.Turf:
                case PaintedSurface.Earth: direction = 0f; spread = Tau * 0.5f; density = 0.5f; break;
                default: direction = 0f; spread = 0.6f; break;
            }

            int count = (int)(size * size / 1500f * density);
            for (int i = 0; i < count; i++)
            {
                float x = random.Next() * size;
                float y = random.Next() * size;
                float angle = direction + (random.Next() - 0.5f) * 2f * spread;
                float strokeLength = length * (0.6f + 0.8f * random.Next()) * scale;
                float strokeWidth = width * (0.6f + 0.8f * random.Next()) * scale;

                // Half the strokes lift towards the warm light, half settle towards the cool shade.
                Rgb colour = random.Next() < 0.5f ? recipe.Warm.Scaled(1.05f) : recipe.Cool.Scaled(0.95f);
                float opacity = (0.09f + 0.11f * random.Next()) * recipe.Brushwork;
                canvas.Stroke(x, y, strokeLength, strokeWidth, angle, colour, opacity, random.Next() * Tau);
            }
        }

        /// <summary>Marks particular to a material, painted over the general brushwork.</summary>
        private static void PaintDetail(Canvas canvas, PaintedTextureRecipe recipe, Sequence random, float scale)
        {
            int size = canvas.Size;
            Rgb light = Rgb.Lerp(recipe.Warm, new Rgb(1f, 0.98f, 0.9f), 0.25f);
            Rgb dark = Rgb.Lerp(recipe.Cool, recipe.Ink, 0.45f);

            switch (recipe.Surface)
            {
                case PaintedSurface.Cloth:
                    // A few long, soft fold shadows.
                    for (int i = 0; i < 7; i++)
                    {
                        canvas.Stroke(random.Next() * size, random.Next() * size, (150f + 120f * random.Next()) * scale,
                            (16f + 12f * random.Next()) * scale, Tau * 0.2f + (random.Next() - 0.5f) * 0.5f, dark,
                            0.16f, random.Next() * Tau);
                    }

                    break;

                case PaintedSurface.Metal:
                    // Patina in the hollows, then a handful of fine, light scratches.
                    for (int i = 0; i < 26; i++)
                    {
                        canvas.Stroke(random.Next() * size, random.Next() * size, (40f + 50f * random.Next()) * scale,
                            (26f + 26f * random.Next()) * scale, random.Next() * Tau, dark, 0.12f, random.Next() * Tau);
                    }

                    for (int i = 0; i < 34; i++)
                    {
                        canvas.Stroke(random.Next() * size, random.Next() * size, (30f + 70f * random.Next()) * scale,
                            1.6f * scale, (random.Next() - 0.5f) * 1.2f, light, 0.3f, 0f);
                    }

                    break;

                case PaintedSurface.LeafMass:
                {
                    // Leaves are grouped, not scattered: dark leaves gather in the shaded masses and
                    // lit leaves on the light ones, with mid-tones between. Shadow is painted first.
                    Rgb shade = Rgb.Lerp(recipe.Cool, recipe.Ink, 0.3f).Scaled(0.8f);
                    Rgb lit = Rgb.Lerp(recipe.Warm, new Rgb(0.86f, 0.9f, 0.5f), 0.35f).Scaled(1.18f);
                    Dabs(canvas, random, 900, 40f, 22f, Tau * 0.5f, shade, 0.6f, scale, 0f, recipe.Seed, -1, 0.75f);
                    Dabs(canvas, random, 1500, 36f, 18f, Tau * 0.5f, recipe.Base.Scaled(1.04f), 0.6f, scale, 0f, recipe.Seed, 0, 0.75f);
                    Dabs(canvas, random, 1100, 32f, 15f, Tau * 0.5f, lit, 0.7f, scale, 0f, recipe.Seed, 1, 0.75f);
                    break;
                }

                case PaintedSurface.Grass:
                {
                    // Blades, mostly upright, in tufts of shade and light.
                    Rgb shade = Rgb.Lerp(recipe.Cool, recipe.Ink, 0.25f).Scaled(0.82f);
                    Rgb lit = Rgb.Lerp(recipe.Warm, new Rgb(0.9f, 0.92f, 0.5f), 0.3f).Scaled(1.15f);
                    Dabs(canvas, random, 1400, 44f, 8f, 0.3f, shade, 0.55f, scale, Tau * 0.25f, recipe.Seed, -1, 0.8f);
                    Dabs(canvas, random, 2400, 40f, 7f, 0.3f, recipe.Base.Scaled(1.05f), 0.55f, scale, Tau * 0.25f, recipe.Seed, 0, 0.8f);
                    Dabs(canvas, random, 1500, 36f, 6f, 0.28f, lit, 0.65f, scale, Tau * 0.25f, recipe.Seed, 1, 0.8f);
                    break;
                }

                case PaintedSurface.Turf:
                {
                    // Seen from above, tufts point every way. A few bare, earthy patches break the green.
                    Rgb shade = Rgb.Lerp(recipe.Cool, recipe.Ink, 0.25f).Scaled(0.84f);
                    Rgb lit = Rgb.Lerp(recipe.Warm, new Rgb(0.88f, 0.9f, 0.5f), 0.3f).Scaled(1.14f);
                    Rgb earth = Rgb.Lerp(recipe.Base, StylePalette.Wood, 0.55f);
                    Dabs(canvas, random, 60, 90f, 60f, Tau * 0.5f, earth, 0.3f, scale, 0f, recipe.Seed, -1, 0.5f);
                    Dabs(canvas, random, 1300, 30f, 9f, Tau * 0.5f, shade, 0.5f, scale, 0f, recipe.Seed, -1, 0.8f);
                    Dabs(canvas, random, 2200, 28f, 8f, Tau * 0.5f, recipe.Base.Scaled(1.05f), 0.5f, scale, 0f, recipe.Seed, 0, 0.8f);
                    Dabs(canvas, random, 1300, 24f, 7f, Tau * 0.5f, lit, 0.6f, scale, 0f, recipe.Seed, 1, 0.8f);
                    break;
                }

                case PaintedSurface.Earth:
                    Dabs(canvas, random, 420, 46f, 30f, Tau * 0.5f, dark, 0.3f, scale, 0f, recipe.Seed, -1, 0.6f);
                    Dabs(canvas, random, 520, 38f, 24f, Tau * 0.5f, light, 0.3f, scale, 0f, recipe.Seed, 1, 0.6f);
                    Dabs(canvas, random, 360, 12f, 8f, Tau * 0.5f, dark, 0.45f, scale, 0f, recipe.Seed, 0, 0.8f);
                    break;

                case PaintedSurface.StoneBlocks:
                case PaintedSurface.Flagstones:
                    // Weathering: a few broad, pale and dark patches that cross the joints.
                    Dabs(canvas, random, 16, 90f, 50f, Tau * 0.5f, light, 0.1f, scale);
                    Dabs(canvas, random, 14, 80f, 44f, Tau * 0.5f, dark, 0.1f, scale);
                    break;
            }
        }

        /// <summary>
        /// Many short strokes. With <paramref name="mass"/> they gather where the surface's masses
        /// are dark (-1) or light (1), which groups them into shapes instead of an even scatter.
        /// </summary>
        /// <param name="crispness">Where a stroke's soft edge begins, 0 to 1. Leaves want crisp edges; washes soft ones.</param>
        private static void Dabs(Canvas canvas, Sequence random, int count, float length, float width, float spread,
            Rgb colour, float opacity, float scale, float direction = 0f, int massSeed = 0, int mass = 0,
            float crispness = 0.45f)
        {
            int size = canvas.Size;
            count = (int)(count * scale * scale);

            for (int i = 0; i < count; i++)
            {
                float x = random.Next() * size;
                float y = random.Next() * size;

                if (mass != 0)
                {
                    // Try a few places and keep the one that best suits the wanted mass.
                    float best = MassNoise(x / size, y / size, massSeed) * mass;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        float tryX = random.Next() * size;
                        float tryY = random.Next() * size;
                        float value = MassNoise(tryX / size, tryY / size, massSeed) * mass;
                        if (value > best)
                        {
                            best = value;
                            x = tryX;
                            y = tryY;
                        }
                    }
                }

                canvas.Stroke(x, y, length * (0.6f + 0.8f * random.Next()) * scale,
                    width * (0.6f + 0.8f * random.Next()) * scale, direction + (random.Next() - 0.5f) * 2f * spread,
                    colour.Scaled(0.94f + 0.12f * random.Next()), opacity * (0.7f + 0.6f * random.Next()),
                    random.Next() * Tau, crispness);
            }
        }

        private static void PaintInk(Canvas canvas, PaintedTextureRecipe recipe)
        {
            int count = canvas.Size * canvas.Size;
            for (int i = 0; i < count; i++)
            {
                float amount = canvas.Ink[i] * recipe.InkStrength;
                if (amount > 0f)
                {
                    canvas.Blend(i, recipe.Ink, amount);
                }
            }
        }

        // ------------------------------------------------------------- maths

        /// <summary>Smooth noise from 0 to 1 that repeats exactly at the texture's edges.</summary>
        internal static float Noise(float u, float v, int cellsU, int cellsV, int seed)
        {
            float x = u * cellsU;
            float y = v * cellsV;
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            float fx = Smooth(x - x0);
            float fy = Smooth(y - y0);

            int xa = Wrap(x0, cellsU);
            int xb = Wrap(x0 + 1, cellsU);
            int ya = Wrap(y0, cellsV);
            int yb = Wrap(y0 + 1, cellsV);

            float bottom = Hash(xa, ya, seed) + (Hash(xb, ya, seed) - Hash(xa, ya, seed)) * fx;
            float top = Hash(xa, yb, seed) + (Hash(xb, yb, seed) - Hash(xa, yb, seed)) * fx;
            return bottom + (top - bottom) * fy;
        }

        internal static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        private static int Wrap(int value, int size)
        {
            int result = value % size;
            return result < 0 ? result + size : result;
        }

        private static float Frac(float value)
        {
            return value - (float)Math.Floor(value);
        }

        private static float Smooth(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float SmoothStep(float from, float to, float value)
        {
            return to <= from ? (value >= to ? 1f : 0f) : Smooth((value - from) / (to - from));
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private static byte ToByte(float value)
        {
            return (byte)(Clamp01(value) * 255f + 0.5f);
        }

        /// <summary>A small random number generator, so results do not depend on the runtime's own.</summary>
        private sealed class Sequence
        {
            private uint state;

            public Sequence(int seed)
            {
                state = (uint)seed * 747796405u + 2891336453u;
                if (state == 0)
                {
                    state = 1;
                }
            }

            /// <summary>The next number from 0 (inclusive) to 1 (exclusive).</summary>
            public float Next()
            {
                unchecked
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    return (state & 0xFFFFFFu) / 16777216f;
                }
            }
        }

        /// <summary>The picture being painted: three colour channels and the amount of line at each pixel.</summary>
        private sealed class Canvas
        {
            public readonly int Size;
            public readonly float[] Ink;
            private readonly float[] red;
            private readonly float[] green;
            private readonly float[] blue;

            public Canvas(int size)
            {
                Size = size;
                red = new float[size * size];
                green = new float[size * size];
                blue = new float[size * size];
                Ink = new float[size * size];
            }

            public void Set(int x, int y, Rgb colour)
            {
                int index = y * Size + x;
                red[index] = colour.R;
                green[index] = colour.G;
                blue[index] = colour.B;
            }

            public void Blend(int index, Rgb colour, float amount)
            {
                amount = Clamp01(amount);
                red[index] += (colour.R - red[index]) * amount;
                green[index] += (colour.G - green[index]) * amount;
                blue[index] += (colour.B - blue[index]) * amount;
            }

            /// <summary>
            /// One brush stroke: soft at its ends and edges, with faint streaks along its length as
            /// from bristles. It wraps round the texture's edges, which keeps the texture tileable.
            /// </summary>
            public void Stroke(float centreX, float centreY, float length, float width, float angle, Rgb colour,
                float opacity, float streakPhase, float crispness = 0.45f)
            {
                float halfLength = Math.Max(1f, length * 0.5f);
                float halfWidth = Math.Max(0.6f, width * 0.5f);
                float cos = (float)Math.Cos(angle);
                float sin = (float)Math.Sin(angle);
                int reach = (int)Math.Ceiling(Math.Max(halfLength, halfWidth)) + 1;
                int startX = (int)Math.Floor(centreX);
                int startY = (int)Math.Floor(centreY);

                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        float px = startX + dx + 0.5f - centreX;
                        float py = startY + dy + 0.5f - centreY;
                        float along = (px * cos + py * sin) / halfLength;
                        float across = (-px * sin + py * cos) / halfWidth;

                        float a = Math.Abs(along);
                        float b = Math.Abs(across);
                        if (a >= 1f || b >= 1f)
                        {
                            continue;
                        }

                        // A leaf shape: full in the middle, tapering to both ends.
                        float taper = crispness > 0.6f ? 1f - a * a : 1f;
                        float ends = 1f - SmoothStep(Math.Max(0.55f, crispness), 1f, a);
                        float edges = 1f - SmoothStep(crispness * taper, Math.Max(0.05f, taper), b);
                        float streaks = 0.78f + 0.22f * (float)Math.Sin(across * 9f + streakPhase);
                        float amount = opacity * ends * edges * streaks;
                        if (amount <= 0.002f)
                        {
                            continue;
                        }

                        int x = Wrap(startX + dx, Size);
                        int y = Wrap(startY + dy, Size);
                        Blend(y * Size + x, colour, amount);
                    }
                }
            }

            public byte[] ToBytes()
            {
                var bytes = new byte[Size * Size * 4];
                for (int i = 0; i < Size * Size; i++)
                {
                    bytes[i * 4] = ToByte(red[i]);
                    bytes[i * 4 + 1] = ToByte(green[i]);
                    bytes[i * 4 + 2] = ToByte(blue[i]);
                    bytes[i * 4 + 3] = 255;
                }

                return bytes;
            }
        }
    }
}
