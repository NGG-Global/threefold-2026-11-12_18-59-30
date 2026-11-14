using System;
using System.IO;
using NUnit.Framework;
using Threefold.EditorTools;
using Threefold.VisualStyle;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The rules of the visual style: the painted textures, the light response, the contour line
    /// and the presets. None of these tests needs the Unity engine. They check arithmetic and
    /// data; how the result looks is judged from the preview images and, finally, in Unity.
    /// </summary>
    public sealed class VisualStyleTests
    {
        private const float Tolerance = 0.0001f;

        // ------------------------------------------------------------ textures

        [Test]
        public void TheSameRecipePaintsTheSameTexture()
        {
            PaintedTextureRecipe recipe = PaintedTextureRecipe.Create("Test", PaintedSurface.StoneBlocks,
                StylePalette.Stone, StylePalette.InkCool, 11, 64);

            byte[] first = PaintedTextureGenerator.Generate(recipe);
            byte[] second = PaintedTextureGenerator.Generate(recipe);

            Assert.AreEqual(64 * 64 * 4, first.Length);
            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void ADifferentSeedPaintsADifferentTexture()
        {
            byte[] a = PaintedTextureGenerator.Generate(PaintedTextureRecipe.Create("A", PaintedSurface.Cloth,
                StylePalette.Cloth, StylePalette.InkPlum, 1, 64));
            byte[] b = PaintedTextureGenerator.Generate(PaintedTextureRecipe.Create("B", PaintedSurface.Cloth,
                StylePalette.Cloth, StylePalette.InkPlum, 2, 64));

            CollectionAssert.AreNotEqual(a, b);
        }

        [Test]
        public void EveryPresetTextureIsOpaqueStaysNearItsColourAndUsesNoPureBlack()
        {
            foreach (StyleMaterialPreset preset in StyleMaterialPresets.All)
            {
                if (preset.Texture == null)
                {
                    continue;
                }

                PaintedTextureRecipe recipe = Shrunk(preset.Texture, 128);
                byte[] pixels = PaintedTextureGenerator.Generate(recipe);

                double red = 0, green = 0, blue = 0;
                int darkest = 255;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    red += pixels[i];
                    green += pixels[i + 1];
                    blue += pixels[i + 2];
                    darkest = Math.Min(darkest, Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])));
                    Assert.AreEqual(255, pixels[i + 3], $"{recipe.Name}: textures are opaque.");
                }

                int count = pixels.Length / 4;
                var average = new Rgb((float)(red / count / 255.0), (float)(green / count / 255.0), (float)(blue / count / 255.0));

                // Brushwork and lines move the average only a little from the palette colour.
                Assert.Less(Distance(average, recipe.Base), 0.16f, $"{recipe.Name}: the texture drifted from its palette colour.");
                Assert.Greater(darkest, 20, $"{recipe.Name}: lines are coloured darks, never black.");
            }
        }

        [Test]
        public void TexturesTileWithoutASeam()
        {
            foreach (PaintedSurface surface in Enum.GetValues(typeof(PaintedSurface)))
            {
                const int size = 128;
                byte[] pixels = PaintedTextureGenerator.Generate(PaintedTextureRecipe.Create(surface.ToString(), surface,
                    StylePalette.Stone, StylePalette.InkCool, 7, size));

                // A seam is a step across the wrap-around edge that is larger than any step between
                // neighbouring rows or columns inside the texture. (A drawn joint may lie on the edge;
                // that is a step like the joints inside, not a seam.)
                double acrossX = 0, acrossY = 0, largestX = 0, largestY = 0;
                for (int i = 0; i < size; i++)
                {
                    acrossX += Step(pixels, size, size - 1, i, 0, i);
                    acrossY += Step(pixels, size, i, size - 1, i, 0);
                }

                for (int line = 0; line < size - 1; line++)
                {
                    double columnStep = 0, rowStep = 0;
                    for (int i = 0; i < size; i++)
                    {
                        columnStep += Step(pixels, size, line, i, line + 1, i);
                        rowStep += Step(pixels, size, i, line, i, line + 1);
                    }

                    largestX = Math.Max(largestX, columnStep);
                    largestY = Math.Max(largestY, rowStep);
                }

                Assert.LessOrEqual(acrossX, largestX * 1.25 + size, $"{surface}: a seam shows at the left and right edges.");
                Assert.LessOrEqual(acrossY, largestY * 1.25 + size, $"{surface}: a seam shows at the top and bottom edges.");
            }
        }

        [Test]
        public void TheVariationMapIsCentredOnMidGrey()
        {
            byte[] pixels = PaintedTextureGenerator.GenerateVariationMap(64, 5);
            double red = 0, green = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                red += pixels[i];
                green += pixels[i + 1];
            }

            int count = pixels.Length / 4;

            // Mid grey means no change, so on average the paint variation leaves colours alone.
            Assert.AreEqual(127.5, red / count, 14.0);
            Assert.AreEqual(127.5, green / count, 14.0);
        }

        // ------------------------------------------------------ light response

        [Test]
        public void LightTurnsSmoothlyRoundAForm()
        {
            const float wrap = 0.35f;
            float previous = IllustratedLightingReference.Ramp(-1f, wrap);

            for (float ndl = -1f; ndl <= 1.0001f; ndl += 0.01f)
            {
                float value = IllustratedLightingReference.Ramp(ndl, wrap);
                Assert.GreaterOrEqual(value, previous - Tolerance, "More light never arrives at a steeper angle.");

                // No band: the largest step for a hundredth of a turn is small.
                Assert.Less(value - previous, 0.03f, $"The ramp jumps at N.L = {ndl:0.00}.");
                previous = value;
            }

            Assert.AreEqual(0f, IllustratedLightingReference.Ramp(-wrap, wrap), Tolerance, "Light ends where the wrap ends.");
            Assert.AreEqual(1f, IllustratedLightingReference.Ramp(1f, wrap), Tolerance);
            Assert.Greater(IllustratedLightingReference.Ramp(0f, wrap), 0f, "Light carries past the terminator.");
            Assert.AreEqual(0f, IllustratedLightingReference.Ramp(0f, 0f), Tolerance, "Without wrap it ends at the terminator.");
        }

        [Test]
        public void TheShadowMapOnlyCountsOnTheLitSide()
        {
            Assert.AreEqual(1f, IllustratedLightingReference.Shadow(-0.2f, 0f), Tolerance, "Past the terminator the ramp shapes the form.");
            Assert.AreEqual(1f, IllustratedLightingReference.Shadow(0f, 0f), Tolerance);
            Assert.AreEqual(0f, IllustratedLightingReference.Shadow(0.5f, 0f), Tolerance, "A cast shadow on a lit face is fully respected.");
            Assert.AreEqual(1f, IllustratedLightingReference.Shadow(0.5f, 1f), Tolerance);

            // Across the terminator the light stays continuous, with or without a shadow.
            float before = IllustratedLightingReference.LightAmount(-0.001f, 0.35f, 0f);
            float after = IllustratedLightingReference.LightAmount(0.001f, 0.35f, 0f);
            Assert.AreEqual(before, after, 0.01f);
        }

        [Test]
        public void ShadeIsTintedAndLightIsNot()
        {
            var albedo = new Rgb(0.5f, 0.5f, 0.5f);
            var sun = new Rgb(1f, 0.9f, 0.7f);
            var ambient = new Rgb(0.3f, 0.3f, 0.3f);
            var none = new Rgb(1f, 1f, 1f);

            Rgb shade = IllustratedLightingReference.Shade(albedo, sun, ambient, StylePalette.ShadeCool, -1f, 0f, 0.35f, 1f, none, 0f, 0.5f);
            Assert.Less(shade.R, shade.B, "In shade the fill turns cool.");
            Assert.AreEqual(albedo.B * ambient.B * StylePalette.ShadeCool.B, shade.B, Tolerance);

            Rgb lit = IllustratedLightingReference.Shade(albedo, sun, ambient, StylePalette.ShadeCool, 1f, 0f, 0.35f, 1f, none, 0f, 0.5f);
            Assert.AreEqual(albedo.R * (sun.R + ambient.R), lit.R, Tolerance, "In full light the ambient fill is untinted.");
            Assert.Greater(lit.R, lit.B, "The lit side is warm.");
        }

        [Test]
        public void TheHighlightIsOffByDefaultAndSmallWhenOn()
        {
            var albedo = new Rgb(0.3f, 0.3f, 0.3f);
            var sun = new Rgb(1f, 1f, 1f);
            var ambient = new Rgb(0.2f, 0.2f, 0.2f);
            var white = new Rgb(1f, 1f, 1f);

            Rgb matte = IllustratedLightingReference.Shade(albedo, sun, ambient, white, 1f, 1f, 0.35f, 1f, white, 0f, 0.5f);
            Rgb metal = IllustratedLightingReference.Shade(albedo, sun, ambient, white, 1f, 1f, 0.35f, 1f, white, 0.3f, 0.55f);
            Rgb away = IllustratedLightingReference.Shade(albedo, sun, ambient, white, 1f, 0.8f, 0.35f, 1f, white, 0.3f, 0.55f);

            Assert.AreEqual(0.3f, metal.R - matte.R, Tolerance, "At its centre the highlight adds exactly its strength.");
            Assert.AreEqual(matte.R, away.R, 0.001f, "A little off centre it is gone: the highlight is small.");
        }

        [Test]
        public void PaintVariationIsNeutralAtMidGreyAndShiftsWarmthOtherwise()
        {
            var albedo = new Rgb(0.5f, 0.4f, 0.3f);

            Rgb same = IllustratedLightingReference.ApplyPaintVariation(albedo, 0.5f, 0.5f, 0.2f);
            Assert.AreEqual(albedo.R, same.R, Tolerance);
            Assert.AreEqual(albedo.B, same.B, Tolerance);

            Rgb warm = IllustratedLightingReference.ApplyPaintVariation(albedo, 0.5f, 1f, 0.2f);
            Assert.Greater(warm.R, albedo.R);
            Assert.Less(warm.B, albedo.B);

            Rgb light = IllustratedLightingReference.ApplyPaintVariation(albedo, 1f, 0.5f, 0.2f);
            Assert.AreEqual(albedo.G * 1.2f, light.G, Tolerance);
        }

        [Test]
        public void ShadeStaysReadableUnderBothLightingPresets()
        {
            foreach (LightingPreset lighting in new[] { LightingPreset.GoldenAfternoon(), LightingPreset.OvercastMorning() })
            {
                Rgb sun = new Rgb(lighting.sunColor.r, lighting.sunColor.g, lighting.sunColor.b).Linear.Scaled(lighting.sunIntensity);
                Rgb horizon = new Rgb(lighting.horizonColor.r, lighting.horizonColor.g, lighting.horizonColor.b).Linear;
                Rgb albedo = StylePalette.Stone.Linear;
                var white = new Rgb(1f, 1f, 1f);

                // A wall facing the sun and a wall facing away, as they appear on screen.
                Rgb lit = IllustratedLightingReference.Shade(albedo, sun, horizon, StylePalette.ShadeCool, 0.8f, 0f, 0.35f, 1f, white, 0f, 0.5f).Srgb;
                Rgb shade = IllustratedLightingReference.Shade(albedo, sun, horizon, StylePalette.ShadeCool, -0.8f, 0f, 0.35f, 1f, white, 0f, 0.5f).Srgb;

                float ratio = shade.Luma / lit.Luma;
                Assert.Greater(ratio, 0.4f, $"{lighting.displayName}: shade is too dark for painted colour to stay visible.");
                Assert.Less(ratio, 0.85f, $"{lighting.displayName}: light and shade are too alike to describe form.");
                Assert.Less(lit.R, 0.98f, $"{lighting.displayName}: lit stone must not burn out to white.");
                Assert.Greater(lighting.fogStart, 15f, $"{lighting.displayName}: haze must not begin within fighting distance.");
            }
        }

        // ------------------------------------------------------------ linework

        [Test]
        public void TheContourLineKeepsItsWidthThenFadesWithDistance()
        {
            const float width = 0.0022f;
            float near = OutlineRules.WidthPixels(width, 1080f, 2f, 12f, 28f);
            float mid = OutlineRules.WidthPixels(width, 1080f, 11f, 12f, 28f);
            float fading = OutlineRules.WidthPixels(width, 1080f, 20f, 12f, 28f);
            float far = OutlineRules.WidthPixels(width, 1080f, 28f, 12f, 28f);

            Assert.AreEqual(2.376f, near, 0.001f, "About 2.4 pixels at 1080p: thin.");
            Assert.AreEqual(near, mid, Tolerance, "The line does not thicken or thin as the camera moves in close range.");
            Assert.Less(fading, near);
            Assert.Greater(fading, 0f);
            Assert.AreEqual(0f, far, Tolerance, "Distant objects carry no outline.");

            Assert.AreEqual(near * 720f / 1080f, OutlineRules.WidthPixels(width, 720f, 2f, 12f, 28f), Tolerance,
                "The line is the same share of any screen.");
            Assert.AreEqual(0f, OutlineRules.WidthPixels(width, 1080f, 2f, 12f, 28f, 1f), Tolerance, "The game-wide switch hides it.");
        }

        [Test]
        public void SmoothedNormalsCloseTheCornersOfAFlatShadedCube()
        {
            // One corner of a cube, stored three times, once per face.
            var corner = new Vector3(1f, 1f, 1f);
            var positions = new[] { corner, corner, corner, new Vector3(-1f, 1f, 1f) };
            var normals = new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.up };

            Vector3[] smooth = OutlineNormals.Smooth(positions, normals);

            float expected = 1f / Mathf.Sqrt(3f);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(expected, smooth[i].x, 0.001f);
                Assert.AreEqual(expected, smooth[i].y, 0.001f);
                Assert.AreEqual(expected, smooth[i].z, 0.001f);
            }

            Assert.AreEqual(1f, smooth[3].y, 0.001f, "A vertex alone at its position keeps its normal.");
        }

        [Test]
        public void RepeatedNormalsCountOnceAndOppositeNormalsAreLeftAlone()
        {
            var point = Vector3.zero;

            // A face split into two triangles repeats its normal; it must not outweigh the other face.
            Vector3[] weighted = OutlineNormals.Smooth(new[] { point, point, point },
                new[] { Vector3.up, Vector3.up, Vector3.right });
            Assert.AreEqual(weighted[0].x, weighted[0].y, 0.001f);

            // The two sides of a thin sheet cancel out; each keeps its own direction.
            Vector3[] sheet = OutlineNormals.Smooth(new[] { point, point }, new[] { Vector3.up, Vector3.down });
            Assert.AreEqual(1f, sheet[0].y, 0.001f);
            Assert.AreEqual(-1f, sheet[1].y, 0.001f);

            Assert.Throws<ArgumentException>(() => OutlineNormals.Smooth(new Vector3[2], new Vector3[3]));
        }

        // -------------------------------------------------------------- presets

        [Test]
        public void LinesAreColouredDarksNotBlack()
        {
            foreach (Rgb ink in new[] { StylePalette.InkWarm, StylePalette.InkCool, StylePalette.InkPlum, StylePalette.Contour })
            {
                Assert.Greater(ink.Luma, 0.08f, "Not black.");
                Assert.Less(ink.Luma, 0.3f, "Dark enough to read as a line.");
                Assert.Greater(Math.Max(ink.R, Math.Max(ink.G, ink.B)) - Math.Min(ink.R, Math.Min(ink.G, ink.B)), 0.05f, "Coloured, not grey.");
            }
        }

        [Test]
        public void TheFiveMaterialFamiliesAreToldApartByColour()
        {
            Rgb[] families = { StylePalette.Stone, StylePalette.Wood, StylePalette.Cloth, StylePalette.Metal, StylePalette.Foliage };
            string[] names = { "stone", "wood", "cloth", "metal", "foliage" };

            for (int i = 0; i < families.Length; i++)
            {
                for (int j = i + 1; j < families.Length; j++)
                {
                    Assert.Greater(Distance(families[i], families[j]), 0.15f, $"{names[i]} and {names[j]} are too alike.");
                }
            }
        }

        [Test]
        public void OnlyMetalHasAHighlightAndItIsRestrained()
        {
            foreach (StyleMaterialPreset preset in StyleMaterialPresets.All)
            {
                if (preset.Family == MaterialFamily.Metal)
                {
                    Assert.Greater(preset.HighlightStrength, 0f, preset.Name);
                    Assert.LessOrEqual(preset.HighlightStrength, 0.4f, preset.Name + ": no mirror-like shine.");
                }
                else
                {
                    Assert.AreEqual(0f, preset.HighlightStrength, preset.Name + " is matte.");
                }

                Assert.That(preset.Name, Does.StartWith(StyleMaterialPresets.Prefix));
            }
        }

        [Test]
        public void EveryFamilyHasAPresetAndEachGreyBoxMaterialHasOneCounterpart()
        {
            var families = new System.Collections.Generic.HashSet<MaterialFamily>();
            var replaced = new System.Collections.Generic.HashSet<string>();

            foreach (StyleMaterialPreset preset in StyleMaterialPresets.All)
            {
                families.Add(preset.Family);
                if (preset.Replaces != null)
                {
                    Assert.IsTrue(replaced.Add(preset.Replaces), $"Two presets replace '{preset.Replaces}'.");
                    Assert.AreSame(preset, StyleMaterialPresets.ForGreyBox(preset.Replaces));
                }
            }

            foreach (MaterialFamily family in Enum.GetValues(typeof(MaterialFamily)))
            {
                Assert.IsTrue(families.Contains(family), $"No preset for {family}.");
            }

            foreach (string greyBox in new[] { "Stone", "StoneDark", "Paving", "Moss", "Ground", "Bark", "Foliage", "Mountain", "Grass" })
            {
                Assert.IsNotNull(StyleMaterialPresets.ForGreyBox(greyBox), $"The grey-box material '{greyBox}' has no painted counterpart.");
            }
        }

        [Test]
        public void QualityLevelsCostLessAsTheyGoDown()
        {
            VisualQualityPreset low = VisualQualityPreset.Low();
            VisualQualityPreset medium = VisualQualityPreset.Medium();
            VisualQualityPreset high = VisualQualityPreset.High();

            Assert.Less(low.renderScale, medium.renderScale);
            Assert.Less(medium.renderScale, high.renderScale);
            Assert.Less(low.shadowResolution, medium.shadowResolution);
            Assert.Less(medium.shadowResolution, high.shadowResolution);
            Assert.Less(low.shadowDistance, medium.shadowDistance);
            Assert.LessOrEqual(low.antiAliasing, medium.antiAliasing);

            // Since LM-08 every preset keeps the contour lines around the characters: they are part of the
            // illustrated identity, and cost one extra draw of a few meshes.
            Assert.IsTrue(low.outlines, "Performance keeps the contour lines.");
            Assert.IsTrue(medium.outlines);
            Assert.AreEqual(30, low.targetFrameRate, "Performance aims for a steady 30.");
            Assert.AreEqual(60, medium.targetFrameRate);
            Assert.AreEqual(60, high.targetFrameRate);
            Assert.IsFalse(low.postProcessing);
            Assert.IsFalse(medium.postProcessing, "No full-screen effects on a mid-range phone until profiling justifies them.");
            Assert.AreEqual(StyleShadows.Hard, low.shadows);
            Assert.AreEqual(StyleShadows.Soft, medium.shadows);
        }

        [Test]
        public void ADeviceGetsACautiousStartingLevel()
        {
            Assert.AreEqual(2, VisualQualityPreset.SuggestLevel(false, 16000, 50), "Desktop and Editor: High.");
            Assert.AreEqual(1, VisualQualityPreset.SuggestLevel(true, 6000, 45), "A mid-range phone: Medium.");
            Assert.AreEqual(0, VisualQualityPreset.SuggestLevel(true, 3000, 45), "Little memory: Low.");
            Assert.AreEqual(0, VisualQualityPreset.SuggestLevel(true, 8000, 30), "An old graphics level: Low.");
            Assert.AreEqual(1, VisualQualityPreset.SuggestLevel(true, 0, 0), "Unknown hardware: Medium.");
            Assert.AreEqual(1, VisualQualityPreset.SuggestLevel(true, 24000, 50), "High is never chosen automatically on a phone.");
        }

        [Test]
        public void RequestedSizesAreRoundedToWhatThePipelineAccepts()
        {
            Assert.AreEqual(256, VisualQualityPreset.NearestShadowResolution(100));
            Assert.AreEqual(1024, VisualQualityPreset.NearestShadowResolution(1024));
            Assert.AreEqual(2048, VisualQualityPreset.NearestShadowResolution(1500));
            Assert.AreEqual(4096, VisualQualityPreset.NearestShadowResolution(9000));
            Assert.AreEqual(1, VisualQualityPreset.NearestSampleCount(0));
            Assert.AreEqual(2, VisualQualityPreset.NearestSampleCount(3));
            Assert.AreEqual(4, VisualQualityPreset.NearestSampleCount(4));
        }

        // -------------------------------------------------- shader and C# agree

        [Test]
        public void TheShaderSourceMatchesItsReferenceInCode()
        {
            string folder = Path.Combine("Assets", "Threefold", "Shaders");
            if (!Directory.Exists(folder))
            {
                Assert.Ignore("The shader sources are not next to the working directory.");
            }

            string shader = File.ReadAllText(Path.Combine(folder, "IllustratedLit.shader"));
            string lighting = File.ReadAllText(Path.Combine(folder, "IllustratedLighting.hlsl"));
            string input = File.ReadAllText(Path.Combine(folder, "IllustratedInput.hlsl"));
            string outline = File.ReadAllText(Path.Combine(folder, "IllustratedOutlinePass.hlsl"));

            StringAssert.Contains($"Shader \"{VisualStyleAssets.ShaderName}\"", shader);
            StringAssert.Contains($"\"LightMode\" = \"{VisualStyleAssets.OutlinePass}\"", shader, "The outline pass is switched by this tag.");
            StringAssert.Contains($"#define ILLUSTRATED_SHADOW_FADE {IllustratedLightingReference.ShadowFade:0.0}", lighting);
            StringAssert.Contains("float2(0.37, 0.61)", input, "The paint variation's projection differs from the reference.");
            StringAssert.Contains("_ThreefoldOutlineSuppress", outline);
            StringAssert.Contains($"#define ILLUSTRATED_OUTLINE_NEAR_CUT {OutlineRules.NearCut:0.0}", outline);
            StringAssert.Contains("clip(input.viewDepth - ILLUSTRATED_OUTLINE_NEAR_CUT)", outline,
                "The hull is no longer cut away near the camera.");
            StringAssert.Contains("if (baseWidth <= 0.0)", outline, "A hidden hull is no longer folded away.");

            // Every property the setup tool writes must exist, or it would be silently skipped.
            foreach (string property in new[]
                     {
                         "_BaseMap", "_BaseColor", "_ShadeColor", "_Wrap", "_Highlight", "_HighlightColor", "_HighlightSize",
                         "_PaintVariation", "_PaintMap", "_PaintStrength", "_PaintScale", "_Emission", "_EmissionColor",
                         "_OutlineColor", "_OutlineWidth", "_OutlineFadeStart", "_OutlineFadeEnd", "_OutlineSmoothNormals", "_Cull",
                     })
            {
                StringAssert.Contains(property + "(", shader, $"The shader has no property '{property}'.");
            }

            foreach (string keyword in new[] { "_HIGHLIGHT", "_PAINT_VARIATION", "_EMISSION", "_OUTLINE_SMOOTH_NORMALS" })
            {
                StringAssert.Contains(keyword, shader, $"The shader does not declare the keyword '{keyword}'.");
            }
        }

        // -------------------------------------------------------------- helpers

        private static PaintedTextureRecipe Shrunk(PaintedTextureRecipe recipe, int size)
        {
            return new PaintedTextureRecipe
            {
                Name = recipe.Name, Surface = recipe.Surface, Base = recipe.Base, Warm = recipe.Warm, Cool = recipe.Cool,
                Ink = recipe.Ink, InkStrength = recipe.InkStrength, Variation = recipe.Variation, Brushwork = recipe.Brushwork,
                Seed = recipe.Seed, Size = size,
            };
        }

        private static float Distance(Rgb a, Rgb b)
        {
            return (float)Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));
        }

        private static double Step(byte[] pixels, int size, int x0, int y0, int x1, int y1)
        {
            int a = (y0 * size + x0) * 4;
            int b = (y1 * size + x1) * 4;
            return Math.Abs(pixels[a] - pixels[b]) + Math.Abs(pixels[a + 1] - pixels[b + 1]) + Math.Abs(pixels[a + 2] - pixels[b + 2]);
        }
    }
}
