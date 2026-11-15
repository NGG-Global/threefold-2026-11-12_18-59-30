using System.Text;
using Threefold.Levels;
using UnityEngine;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the aerial course at the far end of the Discipline Range from <see cref="AerialCourseLayout"/>: the
    /// Ascension pillar, the glide tower with its steps and landing, the dash gap and the dash wall. It is part of
    /// the range's generated environment, which the range's setup rebuilds as a whole, so running it again gives
    /// the same course. Change the numbers in the layout, never the scene.
    /// </summary>
    internal static class AerialCourseSetup
    {
        internal static void Build(EnvironmentAssets assets, Transform parent, StringBuilder report)
        {
            Transform course = FantasyPlaygroundSetup.CreateGroup(parent, "AerialCourse");
            foreach (CourseBlock block in AerialCourseLayout.Blocks())
            {
                Material material = block.Name == "AerialFloor" ? assets.PavingMaterial
                    : block.Name.StartsWith("Dash") ? assets.DarkStoneMaterial
                    : assets.StoneMaterial;
                FantasyPlaygroundSetup.PlaceBlock(assets, course, block.Name, block.Size, block.BaseCentre, material);
            }

            report.AppendLine("Built the aerial course beyond the arrow lane: the Ascension pillar (4.2 m), the glide " +
                              "tower and landing (12 m gap), the dash gap (7 m) and the dash wall (Docs/AerialAbilities.md).");
        }
    }
}
