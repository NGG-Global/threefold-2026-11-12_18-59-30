using UnityEngine;

namespace Threefold.Interface
{
    public enum ProceduralShape
    {
        Disc,
        Ring,
        ChevronUp,
        DoubleChevronUp,
        Diamond,
    }

    /// <summary>
    /// Small white, anti-aliased shapes generated in memory, used to draw the touch controls
    /// without any image files. Each shape is built once and shared. Tint them through the
    /// colour of the Image that displays them.
    /// </summary>
    public static class ProceduralSprites
    {
        private const int Size = 128;
        private const int ShapeCount = 5;

        private static readonly Sprite[] Cache = new Sprite[ShapeCount];

        private static readonly Vector2[] ChevronUp =
        {
            new Vector2(0.27f, 0.40f), new Vector2(0.50f, 0.63f), new Vector2(0.73f, 0.40f),
        };

        private static readonly Vector2[] UpperChevron =
        {
            new Vector2(0.29f, 0.50f), new Vector2(0.50f, 0.71f), new Vector2(0.71f, 0.50f),
        };

        private static readonly Vector2[] LowerChevron =
        {
            new Vector2(0.29f, 0.28f), new Vector2(0.50f, 0.49f), new Vector2(0.71f, 0.28f),
        };

        private static readonly Vector2[] Diamond =
        {
            new Vector2(0.50f, 0.74f), new Vector2(0.70f, 0.50f), new Vector2(0.50f, 0.26f),
            new Vector2(0.30f, 0.50f), new Vector2(0.50f, 0.74f),
        };

        public static Sprite Get(ProceduralShape shape)
        {
            int index = (int)shape;
            if (index < 0 || index >= ShapeCount)
            {
                index = 0;
            }

            if (Cache[index] == null)
            {
                Cache[index] = Create((ProceduralShape)index);
            }

            return Cache[index];
        }

        private static Sprite Create(ProceduralShape shape)
        {
            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    float coverage = Mathf.Clamp01(0.5f - SignedDistance(shape, point));
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Procedural" + shape,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            // A full-rectangle mesh: outline tracing would need to read the pixels, which were just released.
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>Distance in pixels from the point to the shape's edge. Negative inside the shape.</summary>
        private static float SignedDistance(ProceduralShape shape, Vector2 point)
        {
            float half = Size * 0.5f;
            float outerRadius = half - 1.5f;
            float fromCentre = Vector2.Distance(point, new Vector2(half, half));

            switch (shape)
            {
                case ProceduralShape.Ring:
                {
                    float thickness = Size * 0.035f;
                    return Mathf.Abs(fromCentre - (outerRadius - thickness * 0.5f)) - thickness * 0.5f;
                }

                case ProceduralShape.ChevronUp:
                    return DistanceToPolyline(point, ChevronUp) - Size * 0.045f;

                case ProceduralShape.DoubleChevronUp:
                    return Mathf.Min(DistanceToPolyline(point, UpperChevron), DistanceToPolyline(point, LowerChevron))
                           - Size * 0.04f;

                case ProceduralShape.Diamond:
                    return DistanceToPolyline(point, Diamond) - Size * 0.04f;

                default:
                    return fromCentre - outerRadius;
            }
        }

        private static float DistanceToPolyline(Vector2 point, Vector2[] normalizedPoints)
        {
            float nearest = float.MaxValue;

            for (int i = 0; i < normalizedPoints.Length - 1; i++)
            {
                Vector2 a = normalizedPoints[i] * Size;
                Vector2 b = normalizedPoints[i + 1] * Size;
                Vector2 segment = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude);
                nearest = Mathf.Min(nearest, Vector2.Distance(point, a + segment * t));
            }

            return nearest;
        }
    }
}
