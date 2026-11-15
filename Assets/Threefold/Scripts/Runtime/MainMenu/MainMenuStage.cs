using UnityEngine;

namespace Threefold.MainMenu
{
    /// <summary>
    /// The measurements of the scene behind the main menu: where the character stands, where the camera is,
    /// the terrace and the land beyond it. The setup command builds the scene from these numbers and the
    /// tests check the framing with them: change them here, not in the generated scene. Plain C#; its only
    /// use of the engine is vector arithmetic.
    ///
    /// The terrace floor is at height 0. The camera looks along +Z; the character stands at the origin, so
    /// +X is the right of the screen and the valley lies behind him.
    /// </summary>
    public static class MainMenuStage
    {
        // ------------------------------------------------------------ character

        public static readonly Vector3 CharacterPosition = Vector3.zero;

        /// <summary>Turned towards the camera and a little to the left of the screen: a three-quarter view.</summary>
        public const float CharacterYaw = 205f;

        /// <summary>The height of the Mage model (Docs/Characters/Mage.md).</summary>
        public const float CharacterHeight = 1.83f;

        /// <summary>Half the width kept clear for him on screen: shoulders, hanging arms and the cloak.</summary>
        public const float CharacterHalfWidth = 0.5f;

        // --------------------------------------------------------------- camera

        /// <summary>Below his chest and looking slightly up.</summary>
        public static readonly Vector3 CameraPosition = new Vector3(-0.8f, 0.62f, -4.86f);

        /// <summary>Degrees the camera looks upwards.</summary>
        public const float CameraPitchUp = 2f;

        /// <summary>Vertical field of view, in degrees. Narrow, as for a portrait.</summary>
        public const float FieldOfView = 30f;

        public const float NearClip = 0.3f;
        public const float FarClip = 450f;

        // -------------------------------------------------------------- terrace

        public const float TerraceHalfWidth = 9f;
        public const float TerraceBack = -9f;

        /// <summary>The open edge towards the valley. It has no parapet in the middle, or the valley would be hidden.</summary>
        public const float TerraceEdge = 2.4f;

        public const float TerraceThickness = 0.6f;

        // --------------------------------------------------------------- valley

        public const float ValleyFloor = -9f;

        /// <summary>
        /// Where the ruined spire of the levels' background stands (FantasyPlaygroundSetup.BuildBackground).
        /// The far hills rise to meet it.
        /// </summary>
        public static readonly Vector3 SpirePosition = new Vector3(0f, 6f, 235f);
        public const float ValleyHalfWidth = 150f;
        public const float ValleyLength = 262f;

        /// <summary>
        /// Height of the land beyond the terrace. It falls away below the open edge, lies low for a stretch,
        /// and rises into hills towards the mountains and to both sides. Beside the terrace it stays level
        /// for a few metres, which gives the nearest trees somewhere to stand.
        /// </summary>
        public static float ValleyHeight(float x, float z)
        {
            float beyond = Mathf.Max(0f, z - TerraceEdge);
            float side = Mathf.Abs(x);

            // The drop below the edge; later beside the terrace than in front of it.
            float shoulder = Smooth((side - 5.5f) / 3f);
            float dropStart = shoulder * 9f;
            float drop = Smooth((beyond - dropStart) / 12f);
            float height = Mathf.Lerp(-0.35f, ValleyFloor, drop);

            // Low rolling ground, growing with distance.
            float roll = Mathf.Sin(x * 0.045f + 1.3f) * Mathf.Sin(z * 0.038f + 0.4f) + 0.5f * Mathf.Sin(x * 0.11f - z * 0.07f);
            height += roll * Mathf.Min(beyond * 0.02f, 2.2f) * drop;

            // The far hills and the valley's sides.
            float far = Mathf.Max(0f, z - 140f);
            height += far * far * 0.00166f;
            float flank = Mathf.Max(0f, side - 45f);
            height += flank * 0.16f * drop;

            return height;
        }

        /// <summary>
        /// How far from the camera the valley floor first comes into view over the terrace's open edge.
        /// Anything on the floor nearer than this is hidden.
        /// </summary>
        public static float FirstVisibleFloorDistance()
        {
            float toEdge = TerraceEdge - CameraPosition.z;
            float fall = CameraPosition.y / toEdge;
            return (CameraPosition.y - ValleyFloor) / fall;
        }

        // ------------------------------------------------------------- framing

        /// <summary>
        /// Where a point of the scene appears on screen: x and y from 0 (left, bottom) to 1 (right, top).
        /// </summary>
        /// <param name="aspect">Screen width divided by height.</param>
        public static Vector2 Project(Vector3 world, float aspect)
        {
            float pitch = CameraPitchUp * Mathf.Deg2Rad;
            float sin = Mathf.Sin(pitch);
            float cos = Mathf.Cos(pitch);

            Vector3 offset = world - CameraPosition;
            float depth = offset.y * sin + offset.z * cos;
            float up = offset.y * cos - offset.z * sin;
            float half = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);

            depth = Mathf.Max(depth, 0.0001f);
            return new Vector2(0.5f + offset.x / (depth * half * Mathf.Max(aspect, 0.01f)) * 0.5f, 0.5f + up / (depth * half) * 0.5f);
        }

        /// <summary>The character's feet on screen.</summary>
        public static Vector2 FeetOnScreen(float aspect)
        {
            return Project(CharacterPosition, aspect);
        }

        /// <summary>The top of the character's head on screen.</summary>
        public static Vector2 HeadOnScreen(float aspect)
        {
            return Project(CharacterPosition + new Vector3(0f, CharacterHeight, 0f), aspect);
        }

        /// <summary>The left edge of the space kept clear for the character, from 0 to 1 across the screen.</summary>
        public static float CharacterLeftOnScreen(float aspect)
        {
            return Project(CharacterPosition + new Vector3(-CharacterHalfWidth, CharacterHeight * 0.5f, 0f), aspect).x;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
