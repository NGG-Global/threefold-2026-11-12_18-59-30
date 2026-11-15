using System;
using System.Collections.Generic;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.CombatAnimation
{
    /// <summary>What the validation knows about one clip of the character model.</summary>
    public readonly struct CombatClipInfo
    {
        public readonly bool Exists;
        public readonly bool IsHuman;
        public readonly bool IsLooping;
        public readonly float Seconds;

        public CombatClipInfo(bool exists, bool isHuman, bool isLooping, float seconds)
        {
            Exists = exists;
            IsHuman = isHuman;
            IsLooping = isLooping;
            Seconds = seconds;
        }

        public static CombatClipInfo Missing => new CombatClipInfo(false, false, false, 0f);
    }

    public enum CombatIssueSeverity
    {
        Info,
        Warning,
        Error,
    }

    public readonly struct CombatAnimationIssue
    {
        public readonly CombatIssueSeverity Severity;
        public readonly string Message;

        public CombatAnimationIssue(CombatIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public override string ToString()
        {
            return $"{Severity}: {Message}";
        }
    }

    /// <summary>
    /// Checks a combat animation set against the clips the model really contains and against the timing of
    /// the disciplines' melee steps. It reports; it never changes anything. A missing or unsuitable clip is a
    /// warning, not an error: its slot falls back, and the game still plays. Plain C#, tested outside Unity;
    /// the Editor command <c>Threefold &gt; Animation &gt; Validate Combat Animations</c> runs it on the real assets.
    /// </summary>
    public static class CombatAnimationValidation
    {
        /// <summary>One frame at 30 frames per second: the most a contact may be shown early or late.</summary>
        public const float ContactTolerance = 1f / 30f;

        public static void Validate(CombatAnimationLibrary library, Func<string, CombatClipInfo> clips,
            Func<MagicDiscipline, MeleeComboProfile> melee, List<CombatAnimationIssue> issues)
        {
            if (library == null)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Error, "There is no combat animation set."));
                return;
            }

            foreach (MagicDiscipline discipline in (MagicDiscipline[])Enum.GetValues(typeof(MagicDiscipline)))
            {
                if (library.GetDiscipline(discipline) == null)
                {
                    issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                        $"{discipline} has no animation slots: its attacks play no animation."));
                }
            }

            library.ForEachSlot((id, slot) => CheckSlot(id, slot, clips, issues));

            if (melee == null)
            {
                return;
            }

            CombatAnimationTuning tuning = library.tuning ?? new CombatAnimationTuning();
            foreach (MagicDiscipline discipline in (MagicDiscipline[])Enum.GetValues(typeof(MagicDiscipline)))
            {
                DisciplineAnimationSet set = library.GetDiscipline(discipline);
                MeleeComboProfile combo = melee(discipline);
                if (set == null || combo == null)
                {
                    continue;
                }

                for (int step = 0; step < combo.StepCount; step++)
                {
                    CheckStep(library, tuning, discipline, set, step, combo.GetStep(step), issues);
                }

                for (int unused = combo.StepCount; unused < set.AttackCount; unused++)
                {
                    if (CombatSlotResolver.IsAssigned(set.attacks[unused]))
                    {
                        issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Info,
                            $"{discipline} attack slot {unused + 1} is not used: the discipline has {combo.StepCount} melee steps."));
                    }
                }
            }
        }

        private static void CheckSlot(CombatSlotId id, CombatAnimationSlot slot, Func<string, CombatClipInfo> clips,
            List<CombatAnimationIssue> issues)
        {
            string name = id.StateName;
            if (slot == null || !slot.IsAssigned)
            {
                if (id.Kind == CombatSlotKind.Attack || id.Kind == CombatSlotKind.ChargeRelease ||
                    id.Kind == CombatSlotKind.BlockHold || id.Kind == CombatSlotKind.Stagger)
                {
                    issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Info, $"{name} has no clip and plays nothing."));
                }

                return;
            }

            CombatClipInfo info = clips != null ? clips(slot.clip) : CombatClipInfo.Missing;
            if (!info.Exists)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                    $"Missing animation: {name} wants '{slot.clip}', which the character model does not contain. The slot plays nothing."));
                return;
            }

            if (!info.IsHuman)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                    $"{name}: '{slot.clip}' is not a Humanoid clip, so it cannot be retargeted" +
                    (slot.mirror ? " or mirrored." : ".")));
            }

            bool held = id.Kind == CombatSlotKind.Stance || id.Kind == CombatSlotKind.ChargeHold || id.Kind == CombatSlotKind.BlockHold;
            if (held && !slot.holdPose && !info.IsLooping)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                    $"{name}: '{slot.clip}' does not loop, so it stops on its last frame while held. Use a looping clip or a held pose."));
            }

            if (Mathf.Abs(info.Seconds - slot.clipSeconds) > 0.01f)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Info,
                    $"{name}: the clip lasts {info.Seconds:0.###} s but the slot says {slot.clipSeconds:0.###} s; the setup updates it."));
            }

            if (slot.entry > slot.contact + 0.0001f)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                    $"{name}: Entry ({slot.entry:0.###}) is after Contact ({slot.contact:0.###}); timed slots start at Contact."));
            }

            bool strike = id.Kind == CombatSlotKind.Attack || id.Kind == CombatSlotKind.AerialAttack;
            if (strike && !(slot.hitWindowOpen <= slot.contact + 0.0001f && slot.contact <= slot.hitWindowClose + 0.0001f &&
                            slot.hitWindowClose <= slot.recoveryEnd + 0.0001f))
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                    $"{name}: the moments are out of order; expected Hit Window Open ≤ Contact ≤ Hit Window Close ≤ Recovery End."));
            }
        }

        private static void CheckStep(CombatAnimationLibrary library, CombatAnimationTuning tuning, MagicDiscipline discipline,
            DisciplineAnimationSet set, int step, MeleeStepProfile profile, List<CombatAnimationIssue> issues)
        {
            if (profile == null)
            {
                return;
            }

            string label = $"{discipline} step {step + 1} ({profile.name})";
            ResolvedSlot resolved = CombatSlotResolver.ResolveAttack(library, discipline, step, false, 0f);
            if (!resolved.IsValid)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning, $"{label} has no animation."));
                return;
            }

            if (step >= set.AttackCount)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Info,
                    $"{label} reuses attack slot {set.AttackCount}: there are fewer attack slots than steps."));
            }

            CombatAnimationSlot slot = resolved.Slot;
            CombatTimeFit fit = CombatTimeWarp.Fit(slot, profile.startupSeconds, profile.recoverySeconds,
                tuning.minPlaybackSpeed, tuning.maxPlaybackSpeed);

            string speeds = $"wind-up at {fit.WindupSpeed:0.##}x, recovery at {fit.RecoverySpeed:0.##}x";
            if (fit.EntryMoved(slot.entry))
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Info,
                    $"{label}: '{slot.clip}' starts at {fit.Entry:P0} of the clip to land in {profile.startupSeconds:0.###} s ({speeds})."));
            }

            if (Mathf.Abs(fit.ContactError) > ContactTolerance)
            {
                string when = fit.ContactError < 0f ? "early" : "late";
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                    $"{label}: '{slot.clip}' shows contact {Mathf.Abs(fit.ContactError) * 1000f:0} ms {when} ({speeds}). " +
                    "Change the slot's Entry or Contact, the playback limits, or choose another clip."));
            }

            if (fit.RecoveryError > 0.1f)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Info,
                    $"{label}: the recovery pose lasts {fit.RecoveryError * 1000f:0} ms after the next action is allowed; " +
                    "a following action blends over it."));
            }
        }
    }
}
