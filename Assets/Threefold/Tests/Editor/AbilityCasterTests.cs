using NUnit.Framework;
using Threefold.Combat;
using UnityEngine;

namespace Threefold.Tests
{
    public sealed class AbilityCasterTests
    {
        private const float Tolerance = 0.0001f;

        private GameObject host;
        private Health health;
        private Mana mana;
        private AbilityCaster caster;
        private CountingAbility ability;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("AbilityCasterTests");
            health = host.AddComponent<Health>();
            mana = host.AddComponent<Mana>();
            caster = host.AddComponent<AbilityCaster>();

            health.Configure(100f, Vector3.up);
            mana.Configure(30f, 0f, 0f);
            caster.Bind(mana, health, null);

            ability = ScriptableObject.CreateInstance<CountingAbility>();
            ability.ConfigureAbility("Test", 12f, 0.5f);
            caster.SetAbility(0, ability);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(ability);
        }

        [Test]
        public void SuccessfulCastExecutesTheAbilityAndSpendsMana()
        {
            CastResult result = caster.TryCast(0);

            Assert.AreEqual(CastResult.Success, result);
            Assert.AreEqual(1, ability.ExecuteCount);
            Assert.AreEqual(18f, mana.Current, Tolerance);
        }

        [Test]
        public void CastAimsStraightAheadWithoutAnAimProvider()
        {
            host.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            caster.TryCast(0);

            Assert.AreSame(host, ability.LastContext.Caster);
            Assert.AreEqual(1f, ability.LastContext.Direction.x, 0.001f);
            Assert.AreEqual(0f, ability.LastContext.Direction.z, 0.001f);
        }

        [Test]
        public void CastIsRefusedDuringCooldownAndCostsNothing()
        {
            caster.TryCast(0);

            CastResult result = caster.TryCast(0);

            Assert.AreEqual(CastResult.OnCooldown, result);
            Assert.AreEqual(1, ability.ExecuteCount);
            Assert.AreEqual(18f, mana.Current, Tolerance);
        }

        [Test]
        public void CastIsAllowedAgainAfterTheCooldown()
        {
            caster.TryCast(0);
            Assert.IsFalse(caster.IsReady(0));

            caster.Tick(0.6f);

            Assert.IsTrue(caster.IsReady(0));
            Assert.AreEqual(CastResult.Success, caster.TryCast(0));
            Assert.AreEqual(2, ability.ExecuteCount);
        }

        [Test]
        public void CastIsRefusedWithoutEnoughManaAndReportsTheReason()
        {
            CastResult reported = CastResult.Success;
            caster.CastFailed += (failed, reason) => reported = reason;

            caster.TryCast(0);
            caster.Tick(1f);
            caster.TryCast(0);
            caster.Tick(1f);

            // 30 mana pays for two casts of 12; the third cannot be afforded.
            CastResult result = caster.TryCast(0);

            Assert.AreEqual(CastResult.NotEnoughMana, result);
            Assert.AreEqual(CastResult.NotEnoughMana, reported);
            Assert.AreEqual(2, ability.ExecuteCount);
            Assert.AreEqual(6f, mana.Current, Tolerance);
        }

        [Test]
        public void RefusedCastDoesNotStartACooldown()
        {
            mana.TrySpend(25f);

            caster.TryCast(0);

            Assert.IsTrue(caster.IsReady(0));
        }

        [Test]
        public void DefeatedCasterCannotCast()
        {
            health.ApplyDamage(new DamageInfo(100f, Vector3.zero, Vector3.forward, null));

            Assert.AreEqual(CastResult.CasterDefeated, caster.TryCast(0));
            Assert.AreEqual(0, ability.ExecuteCount);
        }

        [Test]
        public void EmptySlotReportsNoAbility()
        {
            Assert.AreEqual(CastResult.NoAbility, caster.TryCast(3));
        }

        [Test]
        public void SuccessfulCastIsAnnounced()
        {
            AbilityDefinition announced = null;
            caster.CastSucceeded += (cast, context) => announced = cast;

            caster.TryCast(0);

            Assert.AreSame(ability, announced);
        }

        private sealed class CountingAbility : AbilityDefinition
        {
            public int ExecuteCount;
            public AbilityContext LastContext;

            public override void Execute(in AbilityContext context)
            {
                ExecuteCount++;
                LastContext = context;
            }
        }
    }
}
