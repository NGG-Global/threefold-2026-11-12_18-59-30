using NUnit.Framework;
using Threefold.EditorTools;
using Threefold.VisualStyle;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The rules behind the material and lighting pass on the Reference Courtyard (Docs/MaterialCohesion.md):
    /// where weathering falls, what the presets ask for, and which way the light's colours lean. They check
    /// the arithmetic and the data. They cannot say that the result looks right; renders and a device do.
    /// Plain C#: these run outside Unity too.
    /// </summary>
    public sealed class MaterialCohesionTests
    {
        [Test]
        public void WeatheringFallsOnlyOnFacesThatLookUp()
        {
            Assert.AreEqual(0f, IllustratedLightingReference.TopTintAmount(0f, 1f, 1f), "A wall's face takes none.");
            Assert.AreEqual(0f, IllustratedLightingReference.TopTintAmount(-1f, 1f, 1f), "An underside takes none.");
            Assert.AreEqual(0f, IllustratedLightingReference.TopTintAmount(0.4f, 1f, 1f), 1e-5f, "A steep slope takes none.");
            Assert.AreEqual(1f, IllustratedLightingReference.TopTintAmount(1f, 1f, 1f), 1e-5f, "A top takes it in full.");
            Assert.AreEqual(1f, IllustratedLightingReference.TopTintAmount(0.8f, 1f, 1f), 1e-5f);

            float gentle = IllustratedLightingReference.TopTintAmount(0.6f, 1f, 1f);
            Assert.Greater(gentle, 0f);
            Assert.Less(gentle, 1f, "Between a slope and a top it comes in gradually.");
        }

        [Test]
        public void WeatheringLiesInSoftPatchesNotEverywhere()
        {
            Assert.AreEqual(0f, IllustratedLightingReference.TopTintAmount(1f, 0f, 1f), "Where the variation map is dark there is none.");
            Assert.AreEqual(0f, IllustratedLightingReference.TopTintAmount(1f, 0.4f, 1f), 1e-5f);
            Assert.AreEqual(1f, IllustratedLightingReference.TopTintAmount(1f, 0.7f, 1f), 1e-5f);

            // The edge of a patch is a ramp, not a step: no hard outline of moss.
            float before = IllustratedLightingReference.TopTintAmount(1f, 0.48f, 1f);
            float after = IllustratedLightingReference.TopTintAmount(1f, 0.56f, 1f);
            Assert.Greater(before, 0f);
            Assert.Less(after, 1f);
            Assert.Greater(after, before);

            // Over the whole range of the map, part of the surface is left clean and part is weathered.
            int clean = 0;
            int full = 0;
            for (int i = 0; i <= 100; i++)
            {
                float amount = IllustratedLightingReference.TopTintAmount(1f, i / 100f, 1f);
                clean += amount <= 0f ? 1 : 0;
                full += amount >= 1f ? 1 : 0;
            }

            Assert.Greater(clean, 30);
            Assert.Greater(full, 20);
        }

        [Test]
        public void AStrengthOfNothingLeavesTheColourAsItWas()
        {
            var stone = new Rgb(0.75f, 0.70f, 0.60f);
            float amount = IllustratedLightingReference.TopTintAmount(1f, 1f, 0f);
            Rgb result = IllustratedLightingReference.ApplyTopTint(stone, StylePalette.Weathering, amount);
            Assert.AreEqual(stone.R, result.R, 1e-6f);
            Assert.AreEqual(stone.G, result.G, 1e-6f);
            Assert.AreEqual(stone.B, result.B, 1e-6f);
        }

        [Test]
        public void WeatheringDarkensTowardsGreenAndNeverBrightens()
        {
            foreach (Rgb tint in new[] { StylePalette.Weathering, StylePalette.Grime })
            {
                Assert.LessOrEqual(tint.R, 1f);
                Assert.LessOrEqual(tint.G, 1f);
                Assert.LessOrEqual(tint.B, 1f);
                Assert.Greater(tint.G, tint.R, "Greener than red.");
                Assert.Greater(tint.R, tint.B, "Warm rather than blue: moss and damp, not frost.");
                Assert.Greater(tint.B, 0.5f, "A stain, not a coat of paint: the stone's own colour stays visible.");
            }

            var stone = new Rgb(0.75f, 0.70f, 0.60f);
            Rgb mossy = IllustratedLightingReference.ApplyTopTint(stone, StylePalette.Weathering, 0.5f);
            Assert.Less(mossy.R, stone.R);
            Assert.Less(mossy.B, stone.B);
            Assert.Greater(mossy.G / mossy.R, stone.G / stone.R, "The stone leans green where it is weathered.");
        }

        [Test]
        public void StoneAndPavingAreWeatheredAndTheOtherFamiliesAreNot()
        {
            foreach (StyleMaterialPreset preset in StyleMaterialPresets.All)
            {
                if (preset.Family == MaterialFamily.Stone)
                {
                    Assert.Greater(preset.TopTintStrength, 0f, $"{preset.Name} takes weathering.");
                    Assert.LessOrEqual(preset.TopTintStrength, 0.6f, $"{preset.Name}: weathering is a stain, not a cover.");
                    Assert.Greater(preset.PaintStrength, 0f, $"{preset.Name}: weathering needs paint variation, which places it.");
                    Assert.GreaterOrEqual(preset.PaintStrength, 0.15f, $"{preset.Name}: stone varies visibly across a wall.");
                    Assert.LessOrEqual(preset.PaintStrength, 0.25f, $"{preset.Name}: the variation stays subtle.");
                }
                else
                {
                    Assert.AreEqual(0f, preset.TopTintStrength, $"{preset.Name} is not stone and takes no moss.");
                }
            }
        }

        [Test]
        public void TheWalkedOnPavingIsStainedLessThanTheWalls()
        {
            StyleMaterialPreset stone = StyleMaterialPresets.Find(StyleMaterialPresets.Prefix + "Stone");
            StyleMaterialPreset paving = StyleMaterialPresets.Find(StyleMaterialPresets.Prefix + "Paving");
            Assert.Less(paving.TopTintStrength, stone.TopTintStrength);
            Assert.Greater(paving.TopTint.B, stone.TopTint.B, "Paving is grimed, not mossed.");
        }

        [Test]
        public void EveryFamilyAnswersLightSoftlyAndNothingShines()
        {
            foreach (StyleMaterialPreset preset in StyleMaterialPresets.All)
            {
                Assert.GreaterOrEqual(preset.Wrap, 0.2f, $"{preset.Name}: no hard terminator.");
                Assert.LessOrEqual(preset.Wrap, 0.6f, $"{preset.Name}: form is still described by light.");
                Assert.LessOrEqual(preset.HighlightStrength, 0.35f, $"{preset.Name}: no mirror-like shine.");
                if (preset.Family != MaterialFamily.Metal)
                {
                    Assert.AreEqual(0f, preset.HighlightStrength, $"{preset.Name}: only metal has a highlight.");
                }

                Assert.Greater(preset.Shade.B, preset.Shade.R, $"{preset.Name}: its shade is cooler than its light.");
                Assert.LessOrEqual(preset.Tint.R, 1f);
                Assert.LessOrEqual(preset.Tint.G, 1f);
                Assert.LessOrEqual(preset.Tint.B, 1f);
            }
        }

        [Test]
        public void TheCharactersLineIsAWarmDarkLighterThanInkAndStillALine()
        {
            Rgb line = StylePalette.ContourSoft;
            Assert.Greater(line.R, line.G);
            Assert.Greater(line.G, line.B, "A warm dark, never black or blue.");
            Assert.Greater(line.Luma, StylePalette.Contour.Luma, "Lighter than the first contour.");
            Assert.Less(line.Luma, 0.22f, "Still a line: darker than any painted surface of the character.");

            Assert.Less(VisualStyleAssets.OutlineWidth, 0.0022f, "Thinner than the first contour.");
            Assert.GreaterOrEqual(VisualStyleAssets.OutlineWidth, 0.0014f, "And not so thin that it breaks up on a phone.");
            Assert.GreaterOrEqual(VisualStyleAssets.CharacterRevision, 2);

            // On a 1080-pixel-high screen the line is about two pixels.
            float pixels = OutlineRules.WidthPixels(VisualStyleAssets.OutlineWidth, 1080f, 5f, VisualStyleAssets.OutlineFadeStart,
                VisualStyleAssets.OutlineFadeEnd);
            Assert.Greater(pixels, 1.5f);
            Assert.Less(pixels, 2.5f);
        }

        [Test]
        public void TheSunIsWarmAndTheShadeIsCool()
        {
            LightingPreset light = LightingPreset.GoldenAfternoon();
            Assert.Greater(light.sunColor.r, light.sunColor.g);
            Assert.Greater(light.sunColor.g, light.sunColor.b, "The sun is warm.");
            Assert.Greater(light.skyColor.b, light.skyColor.r + 0.25f, "The light that fills shade from above is blue.");
            Assert.Greater(light.groundColor.r, light.groundColor.b + 0.1f, "The light that bounces from the ground is warm.");
            Assert.Greater(StylePalette.ShadeCool.B, StylePalette.ShadeCool.R + 0.2f);

            // Shade stays readable: the fill is a good share of the sun.
            float sun = light.sunIntensity * (light.sunColor.r + light.sunColor.g + light.sunColor.b) / 3f;
            float fill = (light.skyColor.r + light.skyColor.g + light.skyColor.b) / 3f;
            Assert.Greater(fill / sun, 0.45f);
            Assert.Less(fill / sun, 0.9f, "And the sun still leads.");
        }

        [Test]
        public void TheHazeAndTheSkyBelowTheHorizonAreNearlyOneColour()
        {
            foreach (LightingPreset light in new[] { LightingPreset.GoldenAfternoon(), LightingPreset.OvercastMorning() })
            {
                Assert.Less(Mathf.Abs(light.fogColor.r - light.skyGroundColor.r), 0.1f, light.displayName);
                Assert.Less(Mathf.Abs(light.fogColor.g - light.skyGroundColor.g), 0.1f, light.displayName);
                Assert.Less(Mathf.Abs(light.fogColor.b - light.skyGroundColor.b), 0.1f, light.displayName);
                Assert.Greater(light.fogColor.b, light.fogColor.r, $"{light.displayName}: distance turns things blue, not brown.");
                Assert.Greater(light.fogEnd, light.fogStart + 100f);
            }
        }

        [Test]
        public void TheHazeDistancesAreUnchanged()
        {
            // The Reference Courtyard's composition counts on them (ReferenceCourtyardLayout.HazeEnd).
            Assert.AreEqual(26f, LightingPreset.GoldenAfternoon().fogStart);
            Assert.AreEqual(360f, LightingPreset.GoldenAfternoon().fogEnd);
        }

        [Test]
        public void AnOlderScenesProvidedPresetsTakeTheNewValuesAndOthersAreKept()
        {
            var old = new LightingPreset { sunIntensity = 0.2f };
            var own = new LightingPreset { displayName = "Storm", sunIntensity = 0.3f };
            LightingPreset[] presets = { old, own, null };

            Assert.IsTrue(LightingPreset.Upgrade(presets));
            Assert.AreEqual(LightingPreset.GoldenAfternoon().sunIntensity, presets[0].sunIntensity, "The provided preset has its present values.");
            Assert.AreSame(own, presets[1], "A preset somebody added is kept.");
            Assert.AreEqual(0.3f, presets[1].sunIntensity);
            Assert.IsNull(presets[2]);

            Assert.IsFalse(LightingPreset.Upgrade(new[] { own }));
            Assert.IsFalse(LightingPreset.Upgrade(null));
            Assert.GreaterOrEqual(LightingPreset.Revision, 2);
            Assert.GreaterOrEqual(StyleMaterialPresets.Revision, 2);
        }
    }
}
