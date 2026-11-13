using System;
using UnityEngine;

namespace Threefold.Puzzles
{
    /// <summary>The placeholder sounds <see cref="PuzzleSounds"/> can generate.</summary>
    public enum PuzzleTone
    {
        None,

        /// <summary>A bright bell: something switched on.</summary>
        Chime,

        /// <summary>A falling tone: something switched off or ran out.</summary>
        Fade,

        /// <summary>A short crackle: an attempt was refused.</summary>
        Fizzle,

        /// <summary>A low grinding rumble: heavy stone moving.</summary>
        Rumble,

        /// <summary>A rising arpeggio of bells: a puzzle solved.</summary>
        Fanfare,

        /// <summary>A dry knock: a plate or lever settling.</summary>
        Click,

        /// <summary>A short high pip: a timer counting down.</summary>
        Tick,
    }

    /// <summary>
    /// Generates simple placeholder sounds in memory, so puzzles can be heard before real audio
    /// exists. No audio files are involved. Every hook that plays one of these also accepts a clip,
    /// which replaces it. The waveforms are plain C# and can be tested without Unity.
    /// </summary>
    public static class PuzzleSounds
    {
        public const int SampleRate = 22050;

        private const float Peak = 0.8f;
        private const float TwoPi = (float)(Math.PI * 2.0);

        private static readonly AudioClip[] Cache = new AudioClip[Enum.GetValues(typeof(PuzzleTone)).Length];

        /// <summary>The generated clip for a tone, built on first use and then shared. Null for None.</summary>
        public static AudioClip Get(PuzzleTone tone)
        {
            int index = (int)tone;
            if (tone == PuzzleTone.None || index < 0 || index >= Cache.Length)
            {
                return null;
            }

            if (Cache[index] == null)
            {
                float[] samples = Synthesize(tone);
                AudioClip clip = AudioClip.Create("Puzzle" + tone, samples.Length, 1, SampleRate, false);
                clip.SetData(samples, 0);
                Cache[index] = clip;
            }

            return Cache[index];
        }

        /// <summary>The waveform of a tone: mono, <see cref="SampleRate"/> samples per second, within -1 to 1.</summary>
        public static float[] Synthesize(PuzzleTone tone)
        {
            float[] samples;
            switch (tone)
            {
                case PuzzleTone.Chime:
                    samples = Silence(1.2f);
                    AddBell(samples, 0f, 880f, 1f);
                    break;
                case PuzzleTone.Fade:
                    samples = Glide(0.9f, 660f, 300f);
                    break;
                case PuzzleTone.Fizzle:
                    samples = Fizzle(0.4f);
                    break;
                case PuzzleTone.Rumble:
                    samples = Rumble(2.6f);
                    break;
                case PuzzleTone.Fanfare:
                    samples = Silence(2f);
                    AddBell(samples, 0f, 523.25f, 0.8f);
                    AddBell(samples, 0.13f, 659.25f, 0.8f);
                    AddBell(samples, 0.26f, 783.99f, 0.85f);
                    AddBell(samples, 0.42f, 1046.5f, 1f);
                    break;
                case PuzzleTone.Click:
                    samples = Knock(0.1f, 2000f, 60f);
                    break;
                case PuzzleTone.Tick:
                    samples = Knock(0.07f, 1600f, 80f);
                    break;
                default:
                    return new float[0];
            }

            FadeEdges(samples);
            Normalise(samples);
            return samples;
        }

        private static float[] Silence(float seconds)
        {
            return new float[Math.Max(1, (int)(seconds * SampleRate))];
        }

        /// <summary>A struck bell: a few slightly inharmonic partials that die away at different rates.</summary>
        private static void AddBell(float[] samples, float startSeconds, float frequency, float level)
        {
            int start = (int)(startSeconds * SampleRate);
            for (int i = start; i < samples.Length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float attack = Math.Min(1f, t / 0.004f);
                float value = (float)(Math.Sin(TwoPi * frequency * t) * Math.Exp(-t * 3.2)
                                      + 0.45 * Math.Sin(TwoPi * frequency * 2.76f * t) * Math.Exp(-t * 5.5)
                                      + 0.22 * Math.Sin(TwoPi * frequency * 5.4f * t) * Math.Exp(-t * 9.0));
                samples[i] += value * attack * level;
            }
        }

        /// <summary>A soft tone sliding from one pitch to another while it fades.</summary>
        private static float[] Glide(float seconds, float from, float to)
        {
            float[] samples = Silence(seconds);
            double phase = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / seconds;
                float frequency = from + (to - from) * progress;
                phase += TwoPi * frequency / SampleRate;
                float envelope = Math.Min(1f, t / 0.01f) * (float)Math.Exp(-t * 2.6);
                samples[i] = (float)(Math.Sin(phase) + 0.3 * Math.Sin(phase * 2.0)) * envelope;
            }

            return samples;
        }

        /// <summary>Crackling noise over a low buzz, dying quickly.</summary>
        private static float[] Fizzle(float seconds)
        {
            float[] samples = Silence(seconds);
            uint seed = 0x9E3779B9u;
            float smoothed = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                smoothed += (Noise(ref seed) - smoothed) * 0.45f;
                float crackle = (Noise(ref seed) > 0.75f ? 1f : 0.35f) * smoothed;
                float buzz = (float)Math.Sin(TwoPi * 110f * t) * 0.5f;
                samples[i] = (crackle + buzz * 0.6f) * (float)Math.Exp(-t * 9.0);
            }

            return samples;
        }

        /// <summary>Low, slowly varying noise with a deep hum, swelling in and out.</summary>
        private static float[] Rumble(float seconds)
        {
            float[] samples = Silence(seconds);
            uint seed = 0x51ED270Bu;
            float low = 0f;
            float lower = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                low += (Noise(ref seed) - low) * 0.02f;
                lower += (low - lower) * 0.05f;
                float hum = (float)Math.Sin(TwoPi * 48f * t) * 0.35f;
                float grind = 1f + 0.4f * (float)Math.Sin(TwoPi * 3.1f * t);
                float envelope = Math.Min(1f, t / 0.25f) * Math.Min(1f, (seconds - t) / 0.6f);
                samples[i] = (lower * 6f * grind + hum) * envelope;
            }

            return samples;
        }

        /// <summary>A short pitched knock.</summary>
        private static float[] Knock(float seconds, float frequency, float decay)
        {
            float[] samples = Silence(seconds);
            uint seed = 0x2545F491u;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = (float)Math.Exp(-t * decay);
                samples[i] = ((float)Math.Sin(TwoPi * frequency * t) * 0.8f + Noise(ref seed) * 0.25f) * envelope;
            }

            return samples;
        }

        /// <summary>Uniform noise between -1 and 1 from a fixed seed, so every build sounds the same.</summary>
        private static float Noise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state / (float)uint.MaxValue * 2f - 1f;
        }

        /// <summary>Very short fades at both ends, so a clip never starts or stops with a click.</summary>
        private static void FadeEdges(float[] samples)
        {
            int fade = Math.Min(samples.Length / 2, SampleRate / 200);
            for (int i = 0; i < fade; i++)
            {
                float gain = i / (float)fade;
                samples[i] *= gain;
                samples[samples.Length - 1 - i] *= gain;
            }
        }

        private static void Normalise(float[] samples)
        {
            float max = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                max = Math.Max(max, Math.Abs(samples[i]));
            }

            if (max <= 0f)
            {
                return;
            }

            float gain = Peak / max;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }

        // Generated clips are not saved anywhere and must not outlive a Play session in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Array.Clear(Cache, 0, Cache.Length);
        }
    }
}
