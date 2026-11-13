using NUnit.Framework;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Tests
{
    public sealed class CombinedCharacterInputTests
    {
        private GameObject host;
        private CombinedCharacterInput combined;
        private FakeInputSource first;
        private FakeInputSource second;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("CombinedCharacterInputTests");
            combined = host.AddComponent<CombinedCharacterInput>();
            first = host.AddComponent<FakeInputSource>();
            second = host.AddComponent<FakeInputSource>();
            combined.AddSource(first);
            combined.AddSource(second);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void MoveIsSummedAndLimitedToFullSpeed()
        {
            first.MoveValue = new Vector2(1f, 0f);
            second.MoveValue = new Vector2(1f, 0f);

            Assert.AreEqual(1f, combined.Move.magnitude, 0.0001f);
        }

        [Test]
        public void MoveFromASingleSourcePassesThrough()
        {
            second.MoveValue = new Vector2(0.25f, -0.5f);

            Assert.AreEqual(second.MoveValue, combined.Move);
        }

        [Test]
        public void LookIsSummed()
        {
            first.LookValue = new Vector2(3f, -1f);
            second.LookValue = new Vector2(2f, 4f);

            Assert.AreEqual(new Vector2(5f, 3f), combined.LookDelta);
        }

        [Test]
        public void ButtonsAreTrueWhenAnySourceIsTrue()
        {
            second.Sprint = true;
            first.JumpIsHeld = true;
            second.JumpPressed = true;
            first.AttackIsHeld = true;
            second.AttackPressed = true;

            Assert.IsTrue(combined.AttackHeld);
            Assert.IsTrue(combined.AttackPressedThisFrame);
            Assert.IsTrue(combined.SprintHeld);
            Assert.IsTrue(combined.JumpHeld);
            Assert.IsTrue(combined.JumpPressedThisFrame);
        }

        [Test]
        public void ButtonsAreFalseWhenNoSourceIsTrue()
        {
            Assert.IsFalse(combined.SprintHeld);
            Assert.IsFalse(combined.JumpHeld);
            Assert.IsFalse(combined.JumpPressedThisFrame);
        }

        [Test]
        public void InteractIsTrueWhenAnySourceIsTrue()
        {
            Assert.IsFalse(combined.InteractPressedThisFrame);

            second.InteractPressed = true;

            Assert.IsTrue(combined.InteractPressedThisFrame);
        }

        [Test]
        public void DisabledSourceIsIgnored()
        {
            first.MoveValue = new Vector2(0f, 1f);
            first.Sprint = true;
            first.enabled = false;

            Assert.AreEqual(Vector2.zero, combined.Move);
            Assert.IsFalse(combined.SprintHeld);
        }

        [Test]
        public void SameSourceCannotBeAddedTwice()
        {
            Assert.IsFalse(combined.AddSource(first));
            Assert.AreEqual(2, combined.Sources.Count);
        }

        [Test]
        public void CombinedInputCannotContainItself()
        {
            Assert.IsFalse(combined.AddSource(combined));
        }

        private sealed class FakeInputSource : CharacterInputSource
        {
            public Vector2 MoveValue;
            public Vector2 LookValue;
            public bool Sprint;
            public bool JumpIsHeld;
            public bool JumpPressed;
            public bool AttackIsHeld;
            public bool AttackPressed;
            public bool InteractPressed;

            public override Vector2 Move => MoveValue;
            public override Vector2 LookDelta => LookValue;
            public override bool SprintHeld => Sprint;
            public override bool JumpHeld => JumpIsHeld;
            public override bool JumpPressedThisFrame => JumpPressed;
            public override bool AttackHeld => AttackIsHeld;
            public override bool AttackPressedThisFrame => AttackPressed;
            public override bool InteractPressedThisFrame => InteractPressed;
        }
    }
}
