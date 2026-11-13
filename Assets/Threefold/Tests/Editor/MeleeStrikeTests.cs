using System.Collections.Generic;
using NUnit.Framework;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The melee strike against real colliders: it hits opponents in its arc, spares allies and
    /// anything behind, and scales its damage. Needs the Unity engine (physics queries).
    /// </summary>
    public sealed class MeleeStrikeTests
    {
        private readonly List<Object> created = new List<Object>();
        private GameObject user;
        private MeleeStrikeDefinition strike;

        [SetUp]
        public void SetUp()
        {
            user = CreateCharacter("User", Vector3.zero, CombatTeam.Enemy);
            strike = ScriptableObject.CreateInstance<MeleeStrikeDefinition>();
            created.Add(strike);
            strike.ConfigureAbility("Test Strike", 0f, 0f);
            strike.ConfigureStrike(25f, 2.6f, 110f, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created)
            {
                Object.DestroyImmediate(item);
            }

            created.Clear();
        }

        private GameObject CreateCharacter(string name, Vector3 position, CombatTeam team)
        {
            var character = new GameObject(name);
            created.Add(character);
            character.transform.position = position;

            Health health = character.AddComponent<Health>();
            health.Configure(100f, Vector3.up);
            health.Team = team;

            CapsuleCollider capsule = character.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            return character;
        }

        private void Strike(float damageScale = 1f)
        {
            Physics.SyncTransforms();
            strike.Execute(new AbilityContext(user, user.transform.position + Vector3.up, Vector3.forward, Vector3.forward,
                damageScale));
        }

        [Test]
        public void HitsAnOpponentInFront()
        {
            Health opponent = CreateCharacter("Opponent", new Vector3(0f, 0f, 2f), CombatTeam.Player).GetComponent<Health>();
            Strike();
            Assert.AreEqual(75f, opponent.Current, 0.01f);
        }

        [Test]
        public void SparesAlliesAndTargetsBehindOrOutOfReach()
        {
            Health ally = CreateCharacter("Ally", new Vector3(0.5f, 0f, 1.5f), CombatTeam.Enemy).GetComponent<Health>();
            Health behind = CreateCharacter("Behind", new Vector3(0f, 0f, -2f), CombatTeam.Player).GetComponent<Health>();
            Health far = CreateCharacter("Far", new Vector3(0f, 0f, 4f), CombatTeam.Player).GetComponent<Health>();

            Strike();

            Assert.AreEqual(100f, ally.Current);
            Assert.AreEqual(100f, behind.Current);
            Assert.AreEqual(100f, far.Current);
        }

        [Test]
        public void NeutralTargetsAreHit()
        {
            Health dummy = CreateCharacter("Dummy", new Vector3(-1f, 0f, 1.5f), CombatTeam.Neutral).GetComponent<Health>();
            Strike();
            Assert.Less(dummy.Current, 100f);
        }

        [Test]
        public void DamageFollowsTheUsersDamageScale()
        {
            Health opponent = CreateCharacter("Opponent", new Vector3(0f, 0f, 2f), CombatTeam.Player).GetComponent<Health>();
            Strike(0.5f);
            Assert.AreEqual(87.5f, opponent.Current, 0.01f);
        }

        [Test]
        public void EachTargetIsHitOnceEvenWithSeveralColliders()
        {
            GameObject opponent = CreateCharacter("Opponent", new Vector3(0f, 0f, 2f), CombatTeam.Player);
            var arm = new GameObject("Arm");
            arm.transform.SetParent(opponent.transform, false);
            arm.AddComponent<BoxCollider>().center = new Vector3(0.3f, 1f, 0f);

            Strike();
            Assert.AreEqual(75f, opponent.GetComponent<Health>().Current, 0.01f);
        }
    }
}
