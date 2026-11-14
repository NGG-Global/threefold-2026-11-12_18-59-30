using System;
using System.Collections.Generic;
using Threefold.Enemies;
using Threefold.Objectives;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Checks a level's checkpoints, hazards and remembered enemy defeats, wherever they are. Part of
    /// <b>Threefold > Validate > Open Scene</b> and of every setup command's report.
    /// </summary>
    internal static class LevelFlowValidator
    {
        internal static void Validate(Scene scene, Action<string> problem)
        {
            var checkpoints = new List<Checkpoint>();
            var hazards = new List<HazardVolume>();
            var routes = new List<CheckpointRoute>();
            var defeats = new List<PersistentDefeat>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                checkpoints.AddRange(root.GetComponentsInChildren<Checkpoint>(true));
                hazards.AddRange(root.GetComponentsInChildren<HazardVolume>(true));
                routes.AddRange(root.GetComponentsInChildren<CheckpointRoute>(true));
                defeats.AddRange(root.GetComponentsInChildren<PersistentDefeat>(true));
            }

            if (checkpoints.Count == 0 && hazards.Count == 0 && routes.Count == 0 && defeats.Count == 0)
            {
                return;
            }

            if (routes.Count > 1)
            {
                problem($"There are {routes.Count} checkpoint routes; a level has one.");
            }

            var listed = new HashSet<Checkpoint>();
            var listedHazards = new HashSet<HazardVolume>();
            foreach (CheckpointRoute route in routes)
            {
                if (route.Session == null) problem($"Checkpoint route '{route.name}' has no session, so nobody is put back anywhere.");
                if (route.Player == null) problem($"Checkpoint route '{route.name}' has no player to watch.");
                for (int i = 0; i < route.CheckpointCount; i++)
                {
                    Checkpoint checkpoint = route.GetCheckpoint(i);
                    if (checkpoint == null) problem($"Checkpoint route '{route.name}' lists a checkpoint that no longer exists.");
                    else listed.Add(checkpoint);
                }

                for (int i = 0; i < route.HazardCount; i++)
                {
                    HazardVolume hazard = route.GetHazard(i);
                    if (hazard == null) problem($"Checkpoint route '{route.name}' lists a hazard that no longer exists.");
                    else listedHazards.Add(hazard);
                }
            }

            var orders = new HashSet<int>();
            var keys = new HashSet<string>();
            foreach (Checkpoint checkpoint in checkpoints)
            {
                if (!listed.Contains(checkpoint))
                {
                    problem($"Checkpoint '{checkpoint.name}' is not on any checkpoint route, so it is never reached.");
                }

                if (!orders.Add(checkpoint.Order))
                {
                    problem($"Checkpoint '{checkpoint.name}' has order {checkpoint.Order}, which another checkpoint also has.");
                }

                if (string.IsNullOrEmpty(checkpoint.PersistenceKey))
                {
                    problem($"Checkpoint '{checkpoint.name}' has no persistence key, so reaching it is forgotten on a reload.");
                }
                else if (!keys.Add(checkpoint.PersistenceKey))
                {
                    problem($"Checkpoint '{checkpoint.name}' shares its persistence key '{checkpoint.PersistenceKey}' with another.");
                }

                Vector3 size = checkpoint.ZoneSize;
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                {
                    problem($"Checkpoint '{checkpoint.name}' has an empty box, so it can never be reached.");
                }
            }

            for (int order = 0; order < checkpoints.Count; order++)
            {
                if (!orders.Contains(order))
                {
                    problem($"No checkpoint has order {order}; checkpoints are numbered 0 to {checkpoints.Count - 1} in the order they are played.");
                    break;
                }
            }

            foreach (HazardVolume hazard in hazards)
            {
                if (!listedHazards.Contains(hazard))
                {
                    problem($"Hazard '{hazard.name}' is not on any checkpoint route, so it never acts.");
                }

                Vector3 size = hazard.Size;
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                {
                    problem($"Hazard '{hazard.name}' has an empty box.");
                }
            }

            foreach (PersistentDefeat defeat in defeats)
            {
                if (defeat.Enemy == null) problem($"'{defeat.name}' remembers the defeat of no enemy.");
                if (string.IsNullOrEmpty(defeat.PersistenceKey))
                {
                    problem($"'{defeat.name}' has no persistence key, so the defeat is not remembered.");
                }
            }
        }
    }
}
