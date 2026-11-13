using System.Collections.Generic;

namespace Threefold.Enemies
{
    /// <summary>
    /// Decides which enemies may attack, so a fight stays readable. An enemy that wants to attack
    /// asks for a token every frame; it may only start its attack while it holds one, and hands
    /// it back when the attack is over. The pool limits:
    ///
    ///   how many enemies attack at once, and how many of those are melee;
    ///   how close together two attacks may start, so their telegraphs do not overlap;
    ///   attacks from enemies the player cannot see, which must wait longer.
    ///
    /// At most one token is granted per update, to the enemy that has waited longest, with a
    /// small preference for enemies on screen. The pool keeps its own clock, advanced by
    /// <see cref="Update"/>, so enemies created at different times share one timeline. It is
    /// plain C#: the scene's director owns one and updates it, and the tests drive it directly.
    /// </summary>
    public sealed class AttackTokenPool
    {
        private const float OnScreenPreference = 0.5f;

        private struct PendingRequest
        {
            public int Id;
            public AttackKind Kind;
            public bool OnScreen;
            public float FirstTime;
            public float LastTime;
        }

        private struct Holder
        {
            public int Id;
            public AttackKind Kind;
        }

        private readonly List<PendingRequest> requests = new List<PendingRequest>(16);
        private readonly List<Holder> holders = new List<Holder>(8);
        private float lastGrantTime = float.NegativeInfinity;

        /// <summary>Seconds the pool has been updated for.</summary>
        public float Now { get; private set; }

        public AttackTokenPool()
        {
        }

        public AttackTokenPool(int maxAttackers, int maxMelee, float minSecondsBetweenStarts, float offscreenDelay)
        {
            Configure(maxAttackers, maxMelee, minSecondsBetweenStarts, offscreenDelay);
        }

        public int MaxAttackers { get; private set; } = 2;
        public int MaxMelee { get; private set; } = 1;
        public float MinSecondsBetweenStarts { get; private set; } = 0.5f;
        public float OffscreenDelay { get; private set; } = 1.5f;

        /// <summary>A request not repeated for this long is dropped.</summary>
        public float RequestLifetime { get; set; } = 0.5f;

        public int HolderCount => holders.Count;
        public int PendingCount => requests.Count;

        public int MeleeHolderCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < holders.Count; i++)
                {
                    if (holders[i].Kind == AttackKind.Melee)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>A pool without limits, for an enemy placed in a scene without a director.</summary>
        public static AttackTokenPool Unlimited()
        {
            return new AttackTokenPool(int.MaxValue, int.MaxValue, 0f, 0f);
        }

        public void Configure(int maxAttackers, int maxMelee, float minSecondsBetweenStarts, float offscreenDelay)
        {
            MaxAttackers = maxAttackers < 1 ? 1 : maxAttackers;
            MaxMelee = maxMelee < 1 ? 1 : maxMelee;
            MinSecondsBetweenStarts = minSecondsBetweenStarts < 0f ? 0f : minSecondsBetweenStarts;
            OffscreenDelay = offscreenDelay < 0f ? 0f : offscreenDelay;
        }

        public bool Holds(int id)
        {
            return IndexOfHolder(id) >= 0;
        }

        /// <summary>True if the limits leave room for one more attack of this kind, ignoring timing.</summary>
        public bool HasFreeSlot(AttackKind kind)
        {
            if (holders.Count >= MaxAttackers)
            {
                return false;
            }

            return kind != AttackKind.Melee || MeleeHolderCount < MaxMelee;
        }

        /// <summary>Asks for a token, or keeps an earlier request alive. Call it every frame while waiting.</summary>
        public void Request(int id, AttackKind kind, bool onScreen)
        {
            float now = Now;
            if (Holds(id))
            {
                return;
            }

            int index = IndexOfRequest(id);
            if (index >= 0)
            {
                PendingRequest existing = requests[index];
                existing.Kind = kind;
                existing.OnScreen = onScreen;
                existing.LastTime = now;
                requests[index] = existing;
                return;
            }

            requests.Add(new PendingRequest { Id = id, Kind = kind, OnScreen = onScreen, FirstTime = now, LastTime = now });
        }

        /// <summary>Withdraws a request that has not been granted.</summary>
        public void Withdraw(int id)
        {
            int index = IndexOfRequest(id);
            if (index >= 0)
            {
                requests.RemoveAt(index);
            }
        }

        /// <summary>Hands back a token and drops any request. Safe to call when nothing is held.</summary>
        public void Release(int id)
        {
            int index = IndexOfHolder(id);
            if (index >= 0)
            {
                holders.RemoveAt(index);
            }

            Withdraw(id);
        }

        /// <summary>Advances the clock, drops stale requests and grants at most one token. Call once per frame.</summary>
        public void Update(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                Now += deltaTime;
            }

            float now = Now;
            for (int i = requests.Count - 1; i >= 0; i--)
            {
                if (now - requests[i].LastTime > RequestLifetime)
                {
                    requests.RemoveAt(i);
                }
            }

            if (requests.Count == 0 || now - lastGrantTime < MinSecondsBetweenStarts)
            {
                return;
            }

            int best = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < requests.Count; i++)
            {
                PendingRequest request = requests[i];
                if (!HasFreeSlot(request.Kind))
                {
                    continue;
                }

                float waited = now - request.FirstTime;
                if (!request.OnScreen && waited < OffscreenDelay)
                {
                    continue;
                }

                float score = waited + (request.OnScreen ? OnScreenPreference : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0)
            {
                return;
            }

            PendingRequest granted = requests[best];
            requests.RemoveAt(best);
            holders.Add(new Holder { Id = granted.Id, Kind = granted.Kind });
            lastGrantTime = now;
        }

        /// <summary>Forgets every token and request, for a restart.</summary>
        public void Clear()
        {
            requests.Clear();
            holders.Clear();
            lastGrantTime = float.NegativeInfinity;
        }

        private int IndexOfHolder(int id)
        {
            for (int i = 0; i < holders.Count; i++)
            {
                if (holders[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfRequest(int id)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
