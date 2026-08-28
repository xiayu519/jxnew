using System;
using System.Collections.Generic;
using System.Linq;
using Jxqy.Domain.World;

namespace Jxqy.Domain.Simulation
{
    public sealed class JxqyDeathExperienceProfile
    {
        public JxqyDeathExperienceProfile(
            IEnumerable<double> byLevelDifference,
            double beneficiaryLevelGrowth,
            int minimumExperience,
            int bonusExclusiveMaximum)
        {
            ByLevelDifference = (byLevelDifference ??
                                 throw new ArgumentNullException(
                                     nameof(byLevelDifference)))
                .ToArray();
            if (ByLevelDifference.Count != 21 ||
                ByLevelDifference.Any(value => value < 0d))
            {
                throw new ArgumentException(
                    "Death experience profile requires 21 non-negative " +
                    "entries for level differences -10 through 10.",
                    nameof(byLevelDifference));
            }
            if (beneficiaryLevelGrowth <= 0d)
                throw new ArgumentOutOfRangeException(
                    nameof(beneficiaryLevelGrowth));
            if (minimumExperience < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(minimumExperience));
            if (bonusExclusiveMaximum <= 1)
                throw new ArgumentOutOfRangeException(
                    nameof(bonusExclusiveMaximum));
            BeneficiaryLevelGrowth = beneficiaryLevelGrowth;
            MinimumExperience = minimumExperience;
            BonusExclusiveMaximum = bonusExclusiveMaximum;
        }

        public IReadOnlyList<double> ByLevelDifference { get; }
        public double BeneficiaryLevelGrowth { get; }
        public int MinimumExperience { get; }
        public int BonusExclusiveMaximum { get; }

        public static JxqyDeathExperienceProfile XinJianXia { get; } =
            new(
                new[]
                {
                    300d, 260d, 215d, 172d, 142d,
                    115d, 90d, 70d, 50d, 35d,
                    25d, 17d, 10d, 4d, 3d,
                    2d, 1d, 0.8d, 0.5d, 0.3d, 0.2d,
                },
                beneficiaryLevelGrowth: 1.125d,
                minimumExperience: 1,
                bonusExclusiveMaximum: 200);
    }

    public static class JxqyExperienceRules
    {
        public static bool IsPlayerPartyMember(
            JxqyCharacter character,
            JxqyPlayer player)
        {
            if (character == null || player == null)
                return false;
            if (ReferenceEquals(character, player))
                return true;
            return character is JxqyNpc npc &&
                   npc.Kind == JxqyCharacterKind.Follower &&
                   npc.Relation == JxqyRelationType.Friend;
        }

        public static bool IsPlayerExperienceKiller(
            JxqyCharacter killer,
            JxqyPlayer player)
        {
            if (IsPlayerPartyMember(killer, player))
                return true;
            if (killer == null || player == null)
                return false;
            return ReferenceEquals(killer.MagicController, player) ||
                   IsPlayerPartyMember(killer.MagicSummoner, player);
        }

        public static bool IsPlayerMagicExperienceSource(
            JxqyCharacter source,
            JxqyPlayer player)
        {
            return source != null &&
                   player != null &&
                   ReferenceEquals(source, player);
        }

        public static IReadOnlyList<JxqyNpc>
            GetSharedPartnerExperienceBeneficiaries(
                IEnumerable<JxqyNpc> npcs,
                JxqyPlayer player)
        {
            if (npcs == null || player == null)
                return Array.Empty<JxqyNpc>();
            var result = new List<JxqyNpc>();
            var seen = new HashSet<JxqyNpc>();
            foreach (JxqyNpc npc in npcs)
            {
                if (npc == null || !seen.Add(npc) ||
                    npc.Kind != JxqyCharacterKind.Follower ||
                    npc.Relation != JxqyRelationType.Friend ||
                    npc.CanLevelUp <= 0)
                {
                    continue;
                }
                result.Add(npc);
            }
            return result;
        }

        public static bool CanOwnPoisonExperience(
            JxqyCharacter source)
        {
            if (source is JxqyPlayer)
                return true;
            return source is JxqyNpc npc &&
                   npc.Kind == JxqyCharacterKind.Follower &&
                   npc.Relation == JxqyRelationType.Friend;
        }

        public static string ResolvePlayerMagicExperienceId(
            string projectileMagicId,
            bool projectileMagicIsLearned)
        {
            // The original Xin Jian Xia executable looks up the projectile's
            // own magic file in the player's magic list. An embedded FlyIni
            // or AttackFile is not redirected to the selected shortcut.
            return projectileMagicIsLearned &&
                   !string.IsNullOrWhiteSpace(projectileMagicId)
                ? projectileMagicId
                : string.Empty;
        }

        public static int GetAutomaticMagicExperience(int magicLevel)
        {
            return checked(Math.Max(0, magicLevel) + 60);
        }

        public static int GetAutomaticMagicLevelLimit(int characterLevel)
        {
            return Math.Max(1, characterLevel / 4 + 1);
        }

        public static bool CanGainAutomaticMagicExperience(
            int magicLevel,
            int characterLevel)
        {
            return magicLevel > 0 &&
                   magicLevel < 10 &&
                   magicLevel < GetAutomaticMagicLevelLimit(characterLevel);
        }

        public static int GetSelectedMagicKillExperience(
            int characterExperience)
        {
            return Math.Max(0, characterExperience) / 10;
        }

        public static bool ShouldAdvancePartnerBaseSkills(
            int newCharacterLevel)
        {
            return newCharacterLevel > 0 &&
                   newCharacterLevel % 3 == 0;
        }

        public static int AdvancePartnerBaseSkills(
            JxqyNpc partner)
        {
            if (partner == null)
                throw new ArgumentNullException(nameof(partner));
            if (!ShouldAdvancePartnerBaseSkills(partner.Level) ||
                partner.Skills == null)
            {
                return 0;
            }

            int advanced = 0;
            for (int legacyListIndex = 1;
                 legacyListIndex <= 2;
                 legacyListIndex++)
            {
                JxqySkillEntry entry =
                    partner.Skills.FindAtLegacyIndex(legacyListIndex);
                if (entry == null || entry.Level >= 10)
                    continue;
                if (partner.Skills.SetLevel(
                        entry.Magic.Id,
                        entry.Level + 1))
                {
                    advanced++;
                }
            }
            return advanced;
        }

        public static int CalculateDeathExperience(
            JxqyCharacter beneficiary,
            JxqyCharacter defeated,
            JxqyDeathExperienceProfile profile)
        {
            if (beneficiary == null)
                throw new ArgumentNullException(nameof(beneficiary));
            if (defeated == null)
                throw new ArgumentNullException(nameof(defeated));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            int difference = Math.Max(
                -10,
                Math.Min(10, beneficiary.Level - defeated.Level));
            double beneficiaryLevelFactor = Math.Pow(
                profile.BeneficiaryLevelGrowth,
                Math.Max(0, beneficiary.Level - 1));
            // The original uses _ftol, whose local control word explicitly
            // selects truncation toward zero.
            int experience = (int)(
                profile.ByLevelDifference[difference + 10] *
                beneficiaryLevelFactor);
            experience = Math.Max(profile.MinimumExperience, experience);
            if (defeated.ExpBonus > 0 &&
                defeated.ExpBonus < profile.BonusExclusiveMaximum)
            {
                experience = checked(
                    experience +
                    (int)(Math.Pow(
                              profile.BeneficiaryLevelGrowth,
                              Math.Max(0, defeated.Level - 1)) *
                          defeated.ExpBonus));
            }
            return experience;
        }

        public static int FindLevelForExperience(
            IEnumerable<KeyValuePair<int, int>> orderedThresholds,
            int experience)
        {
            if (orderedThresholds == null)
                throw new ArgumentNullException(nameof(orderedThresholds));
            int highestLevel = 0;
            foreach (KeyValuePair<int, int> pair in orderedThresholds)
            {
                highestLevel = Math.Max(highestLevel, pair.Key);
                if (pair.Value > experience)
                    return pair.Key;
            }
            return highestLevel + 1;
        }

        public static void ApplyTerminalLevel(
            JxqyCharacter character,
            int level)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));
            character.Experience = 0;
            character.LevelUpExperience = 0;
            character.Level = level;
        }
    }

    public static class JxqyLegacyMagicTiming
    {
        private const int AnimationElapsedMillisecondsPerTick = 16;
        private const float FixedTicksPerSecond = 60f;

        public static int GetPositiveLifeFrameActiveTickCount(
            int lifeFrame,
            int animationIntervalMilliseconds)
        {
            if (lifeFrame <= 0)
                return 0;

            long safeInterval = Math.Max(
                0,
                animationIntervalMilliseconds);
            // XNA runs at 60 Hz, but Sprite.Update truncates each fixed
            // elapsed time to 16 ms and advances at most one ASF frame when
            // accumulatedElapsed > Interval. MagicSprite checks collision
            // before Sprite.Update, so one final collision/movement tick
            // occurs after the requested animation frames finish.
            long thresholdTicks =
                safeInterval * lifeFrame /
                AnimationElapsedMillisecondsPerTick + 1L;
            long playbackTicks = Math.Max((long)lifeFrame, thresholdTicks);
            return (int)Math.Min(int.MaxValue, playbackTicks + 1L);
        }

        public static float GetPositiveLifeFrameActiveSeconds(
            int lifeFrame,
            int animationIntervalMilliseconds)
        {
            return GetPositiveLifeFrameActiveTickCount(
                       lifeFrame,
                       animationIntervalMilliseconds) /
                   FixedTicksPerSecond;
        }
    }

    public enum JxqyStatusKind
    {
        Frozen,
        Petrified,
        Poisoned,
        MovementDisabled,
        SkillDisabled,
    }

    public enum JxqyMagicAdditionalEffect
    {
        None,
        Frozen,
        Poisoned,
        Petrified,
    }

    public enum JxqyDropKind
    {
        Weapon,
        Armor,
        Money,
        Drug,
    }

    public sealed class JxqyDrop
    {
        public JxqyDrop(JxqyDropKind kind, string resourcePath, string scriptFile)
        {
            Kind = kind;
            ResourcePath = resourcePath ?? string.Empty;
            ScriptFile = scriptFile ?? string.Empty;
        }

        public JxqyDropKind Kind { get; }
        public string ResourcePath { get; }
        public string ScriptFile { get; }
        public JxqyIntPoint TilePosition { get; private set; }

        public void PlaceAt(JxqyIntPoint tilePosition)
        {
            TilePosition = tilePosition;
        }
    }

    public sealed class JxqyMagicLevelDefinition
    {
        public int Level { get; set; } = 1;
        public int MoveKind { get; set; } = 2;
        public int PassThroughWall { get; set; }
        public int EffectLevel { get; set; } = 1;
        public int Region { get; set; }
        public int SpecialKind { get; set; }
        public int SpecialKindValue { get; set; }
        public int SpecialKindMilliseconds { get; set; }
        public int NoSpecialKindEffect { get; set; }
        public int NoInterruption { get; set; }
        public int WaitFrame { get; set; }
        public int LifeFrame { get; set; }
        public int KeepMilliseconds { get; set; }
        public int ColdMilliseconds { get; set; }
        public int Effect { get; set; }
        public int EffectExt { get; set; }
        public int Effect2 { get; set; }
        public int Effect3 { get; set; }
        public int EffectMana { get; set; }
        public int ManaCost { get; set; }
        public int ThewCost { get; set; }
        public int LifeCost { get; set; }
        public float ProjectileSpeed { get; set; }
        // Per-level world-target range from the shipped magic data. The
        // widescreen input adapter applies it after resolving the pointer so
        // expanding the viewport does not expand authored martial-art range.
        // NPC.AttackRadius remains a separate combat-AI field.
        public int AttackRadius { get; set; } = 5;
        public int LevelUpExperience { get; set; }
        public int RestoreType { get; set; }
        public int RestorePercent { get; set; }
        public int RestoreProbability { get; set; }
        public int DisableMoveMilliseconds { get; set; }
        public int DisableSkillMilliseconds { get; set; }
        public int SideEffectType { get; set; }
        public int SideEffectPercent { get; set; }
        public int SideEffectProbability { get; set; }
        public int DieAfterUse { get; set; }
        public int LifeMax { get; set; }
        public int ThewMax { get; set; }
        public int ManaMax { get; set; }
        public int Attack { get; set; }
        public int Attack2 { get; set; }
        public int Attack3 { get; set; }
        public int Defend { get; set; }
        public int Defend2 { get; set; }
        public int Defend3 { get; set; }
        public int Evade { get; set; }
        public int AddLifeRestorePercent { get; set; }
        public int AddThewRestorePercent { get; set; }
        public int AddManaRestorePercent { get; set; }
        public int ReviveBodyRadius { get; set; }
        public int ReviveBodyMaxCount { get; set; }
        public int ReviveBodyLifeMilliseconds { get; set; }
        public string FlyIni { get; set; } = string.Empty;
        public string FlyIni2 { get; set; } = string.Empty;
        public string MagicToUseWhenBeAttacked { get; set; } = string.Empty;
        public int MagicDirectionWhenBeAttacked { get; set; }

        internal void ApplyTo(JxqyMagicDefinition magic)
        {
            magic.MoveKind = MoveKind;
            magic.PassThroughWall = PassThroughWall;
            magic.EffectLevel = EffectLevel;
            magic.Region = Region;
            magic.SpecialKind = SpecialKind;
            magic.SpecialKindValue = SpecialKindValue;
            magic.SpecialKindMilliseconds = SpecialKindMilliseconds;
            magic.NoSpecialKindEffect = NoSpecialKindEffect;
            magic.NoInterruption = NoInterruption;
            magic.WaitFrame = WaitFrame;
            magic.LifeFrame = LifeFrame;
            magic.KeepMilliseconds = KeepMilliseconds;
            magic.ColdMilliseconds = ColdMilliseconds;
            magic.Effect = Effect;
            magic.EffectExt = EffectExt;
            magic.Effect2 = Effect2;
            magic.Effect3 = Effect3;
            magic.EffectMana = EffectMana;
            magic.ManaCost = ManaCost;
            magic.ThewCost = ThewCost;
            magic.LifeCost = LifeCost;
            magic.ProjectileSpeed = ProjectileSpeed;
            magic.AttackRadius = AttackRadius;
            magic.LevelUpExperience = LevelUpExperience;
            magic.RestoreType = RestoreType;
            magic.RestorePercent = RestorePercent;
            magic.RestoreProbability = RestoreProbability;
            magic.DisableMoveSeconds =
                Math.Max(0, DisableMoveMilliseconds) / 1000f;
            magic.DisableSkillSeconds =
                Math.Max(0, DisableSkillMilliseconds) / 1000f;
            magic.SideEffectType = SideEffectType;
            magic.SideEffectPercent = SideEffectPercent;
            magic.SideEffectProbability = SideEffectProbability;
            magic.DieAfterUse = DieAfterUse != 0;
            magic.LifeMax = LifeMax;
            magic.ThewMax = ThewMax;
            magic.ManaMax = ManaMax;
            magic.Attack = Attack;
            magic.Attack2 = Attack2;
            magic.Attack3 = Attack3;
            magic.Defend = Defend;
            magic.Defend2 = Defend2;
            magic.Defend3 = Defend3;
            magic.Evade = Evade;
            magic.AddLifeRestorePercent = AddLifeRestorePercent;
            magic.AddThewRestorePercent = AddThewRestorePercent;
            magic.AddManaRestorePercent = AddManaRestorePercent;
            magic.ReviveBodyRadius = ReviveBodyRadius;
            magic.ReviveBodyMaxCount = ReviveBodyMaxCount;
            magic.ReviveBodyLifeMilliseconds =
                ReviveBodyLifeMilliseconds;
            magic.FlyIni = FlyIni;
            magic.FlyIni2 = FlyIni2;
            magic.MagicToUseWhenBeAttacked =
                MagicToUseWhenBeAttacked;
            magic.MagicDirectionWhenBeAttacked =
                MagicDirectionWhenBeAttacked;
            float specialSeconds = SpecialKindMilliseconds > 0
                ? SpecialKindMilliseconds / 1000f
                : Math.Max(1, EffectLevel + 1);
            magic.FrozenSeconds = SpecialKind == 1
                ? specialSeconds
                : 0f;
            magic.PoisonSeconds = SpecialKind == 2
                ? specialSeconds
                : 0f;
            magic.PetrifiedSeconds = SpecialKind == 3
                ? specialSeconds
                : 0f;
        }
    }

    public sealed class JxqyMagicDefinition
    {
        private readonly Dictionary<int, JxqyMagicLevelDefinition>
            _levels = new Dictionary<int, JxqyMagicLevelDefinition>();

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Introduction { get; set; } = string.Empty;
        public string ImageFileName { get; set; } = string.Empty;
        public string IconFileName { get; set; } = string.Empty;
        public string FlyingImageFileName { get; set; } = string.Empty;
        public string FlyingSoundFileName { get; set; } = string.Empty;
        public string VanishImageFileName { get; set; } = string.Empty;
        public string VanishSoundFileName { get; set; } = string.Empty;
        public string SuperModeImageFileName { get; set; } = string.Empty;
        public string ActionFileName { get; set; } = string.Empty;
        public string AttackFileName { get; set; } = string.Empty;
        public int Belong { get; set; }
        public int MoveKind { get; set; } = 2;
        public int PassThroughWall { get; set; }
        public int EffectLevel { get; set; } = 1;
        public int Region { get; set; }
        public int SpecialKind { get; set; }
        public int SpecialKindValue { get; set; }
        public int SpecialKindMilliseconds { get; set; }
        public int NoSpecialKindEffect { get; set; }
        public int NoInterruption { get; set; }
        public int WaitFrame { get; set; }
        public int LifeFrame { get; set; }
        public int KeepMilliseconds { get; set; }
        public int ColdMilliseconds { get; set; }
        public int AlphaBlend { get; set; }
        public int FlyingLum { get; set; }
        public int VanishLum { get; set; }
        public int Effect { get; set; }
        public int EffectExt { get; set; }
        public int Effect2 { get; set; }
        public int Effect3 { get; set; }
        public int EffectMana { get; set; }
        public int LevelUpExperience { get; set; }
        public int ManaCost { get; set; }
        public int ThewCost { get; set; }
        public int LifeCost { get; set; }
        public float ProjectileSpeed { get; set; }
        // Per-level player-magic target range. Do not conflate it with the
        // independently configured NPC.AttackRadius.
        public int AttackRadius { get; set; } = 5;
        public float Range { get; set; } = 48f;
        public float Radius { get; set; } = 12f;
        public float LifeSeconds { get; set; } = 3f;
        public float FrozenSeconds { get; set; }
        public float PetrifiedSeconds { get; set; }
        public float PoisonSeconds { get; set; }
        public JxqyMagicAdditionalEffect AdditionalEffect { get; set; }
        public float DisableMoveSeconds { get; set; }
        public float DisableSkillSeconds { get; set; }
        public int SideEffectPercent { get; set; }
        public int SideEffectProbability { get; set; }
        public int SideEffectType { get; set; }
        public int RestoreType { get; set; }
        public int RestorePercent { get; set; }
        public int RestoreProbability { get; set; }
        public bool DieAfterUse { get; set; }
        public int LifeMax { get; set; }
        public int ThewMax { get; set; }
        public int ManaMax { get; set; }
        public int Attack { get; set; }
        public int Attack2 { get; set; }
        public int Attack3 { get; set; }
        public int Defend { get; set; }
        public int Defend2 { get; set; }
        public int Defend3 { get; set; }
        public int Evade { get; set; }
        public int AddLifeRestorePercent { get; set; }
        public int AddThewRestorePercent { get; set; }
        public int AddManaRestorePercent { get; set; }
        public int ReviveBodyRadius { get; set; }
        public int ReviveBodyMaxCount { get; set; }
        public int ReviveBodyLifeMilliseconds { get; set; }
        public string FlyIni { get; set; } = string.Empty;
        public string FlyIni2 { get; set; } = string.Empty;
        public string MagicToUseWhenBeAttacked { get; set; } =
            string.Empty;
        public int MagicDirectionWhenBeAttacked { get; set; }

        public int MaximumLevel => _levels.Count == 0
            ? 10
            : Math.Max(1, GetMaximumDefinedLevel());

        public void SetLevelDefinitions(
            IEnumerable<JxqyMagicLevelDefinition> levels)
        {
            _levels.Clear();
            if (levels == null)
                return;
            foreach (JxqyMagicLevelDefinition level in levels)
            {
                if (level == null || level.Level < 1)
                    continue;
                _levels[level.Level] = level;
            }
        }

        public bool ApplyLevel(int level)
        {
            if (_levels.Count == 0)
            {
                EffectLevel = Math.Max(1, level);
                return false;
            }
            int clamped = Math.Max(1, Math.Min(MaximumLevel, level));
            if (!_levels.TryGetValue(
                    clamped,
                    out JxqyMagicLevelDefinition definition))
            {
                return false;
            }
            definition.ApplyTo(this);
            return true;
        }

        public int GetLevelUpExperience(int level)
        {
            return _levels.TryGetValue(
                Math.Max(1, level),
                out JxqyMagicLevelDefinition definition)
                ? Math.Max(0, definition.LevelUpExperience)
                : Math.Max(0, LevelUpExperience);
        }

        public JxqyMagicDefinition CreateRuntimeSnapshot()
        {
            return (JxqyMagicDefinition)MemberwiseClone();
        }

        private int GetMaximumDefinedLevel()
        {
            int maximum = 1;
            foreach (int level in _levels.Keys)
                maximum = Math.Max(maximum, level);
            return maximum;
        }
    }

    public static class JxqyMagicTargeting
    {
        public static JxqyFloat2 ClampWorldDestinationToRange(
            JxqyCharacter source,
            JxqyFloat2 requestedDestination,
            int attackRadius)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            int radius = Math.Max(0, attackRadius);
            JxqyIntPoint requestedTile =
                JxqyIsometricMapMath.WorldPixelToTile(
                    (int)Math.Round(requestedDestination.X),
                    (int)Math.Round(requestedDestination.Y));
            if (JxqyPathfinder.GetViewTileDistance(
                    source.TilePosition,
                    requestedTile) <= radius)
            {
                return requestedDestination;
            }
            if (radius == 0)
                return source.PositionInWorld;

            JxqyFloat2 requestedDirection =
                requestedDestination - source.PositionInWorld;
            if (requestedDirection == JxqyFloat2.Zero)
                return source.PositionInWorld;
            int direction = JxqyDirection.GetIndex(
                requestedDirection,
                8);
            JxqyIntPoint boundaryTile = source.TilePosition;
            for (int step = 0; step < radius; step++)
            {
                boundaryTile =
                    JxqyPathfinder.GetAllNeighbors(boundaryTile)[direction];
            }
            JxqyIntPoint boundaryWorld =
                JxqyIsometricMapMath.TileToWorldPixel(
                    boundaryTile.X,
                    boundaryTile.Y,
                    boundCheck: false);
            return new JxqyFloat2(boundaryWorld.X, boundaryWorld.Y);
        }

        public static JxqyCharacter ResolveBodyEffectTarget(
            JxqyCharacter source,
            JxqyCharacter requestedTarget)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.Kind == JxqyCharacterKind.Player &&
                requestedTarget != null &&
                requestedTarget.Relation == JxqyRelationType.Friend &&
                (requestedTarget.Kind == JxqyCharacterKind.Fighter ||
                 requestedTarget.Kind == JxqyCharacterKind.Follower))
            {
                return requestedTarget;
            }
            return source;
        }

    }

    public readonly struct JxqyDamageResult
    {
        public JxqyDamageResult(bool hit, int lifeDamage, int manaDamage)
        {
            Hit = hit;
            LifeDamage = lifeDamage;
            ManaDamage = manaDamage;
        }

        public bool Hit { get; }
        public int LifeDamage { get; }
        public int ManaDamage { get; }
    }

    public static class JxqyDamageCalculator
    {
        public const int MinimalDamage = 5;

        public static int GetMagicEffectAmount(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            int channel)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (magic == null)
                throw new ArgumentNullException(nameof(magic));

            bool useConfiguredEffect =
                source.Kind == JxqyCharacterKind.Player;
            switch (channel)
            {
                case 1:
                    return (useConfiguredEffect && magic.Effect != 0
                               ? magic.Effect
                               : source.Attack) +
                           magic.EffectExt;
                case 2:
                    return useConfiguredEffect && magic.Effect2 != 0
                        ? magic.Effect2
                        : source.Attack2;
                case 3:
                    return useConfiguredEffect && magic.Effect3 != 0
                        ? magic.Effect3
                        : source.Attack3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        public static int GetHitPercent(int attackerEvade, int targetEvade)
        {
            attackerEvade = Math.Max(0, attackerEvade);
            targetEvade = Math.Max(0, targetEvade);
            const float baseHitRatio = 0.05f;
            const float belowRatio = 0.5f;
            const float upRatio = 0.45f;
            float hitRatio = baseHitRatio;
            if (targetEvade >= attackerEvade)
            {
                hitRatio += targetEvade == 0
                    ? belowRatio
                    : attackerEvade / (float)targetEvade * belowRatio;
            }
            else
            {
                float offset = (attackerEvade - targetEvade) / 100f;
                hitRatio += belowRatio + Math.Min(1f, offset) * upRatio;
            }
            return Math.Max(0, Math.Min(100, (int)(hitRatio * 100f)));
        }

        public static bool ShouldEnterHurtState(
            JxqyCharacter target,
            int hurtRoll)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            return hurtRoll == 0 &&
                   !target.IsDead &&
                   !target.IsPetrified &&
                   target.State != JxqyCharacterState.Hurt &&
                   target.State != JxqyCharacterState.Death &&
                   !target.IsCurrentMagicUninterruptible &&
                   target.IsActionEnabled(JxqyCharacterState.Hurt);
        }

        public static JxqyDamageResult Resolve(
            JxqyCharacter attacker,
            JxqyCharacter target,
            int damage,
            int damage2,
            int damage3,
            int manaDamage,
            JxqyDeterministicRandom random,
            bool guaranteedHit = false,
            bool enterHurtState = true)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (target.IsDead || target.Invincible)
                return new JxqyDamageResult(false, 0, 0);

            int hitPercent = GetHitPercent(attacker.Evade, target.Evade);
            if (!guaranteedHit &&
                target.CanEvade &&
                random.Next(0, 101) > hitPercent)
                return new JxqyDamageResult(false, 0, 0);

            int lifeDamage = damage - target.Defend;
            int secondary = damage2 - target.Defend2;
            int tertiary = damage3 - target.Defend3;
            if (secondary > 0)
                lifeDamage += secondary;
            if (tertiary > 0)
                lifeDamage += tertiary;
            lifeDamage = Math.Max(MinimalDamage, lifeDamage);
            bool shouldEnterHurtState = enterHurtState &&
                                        ShouldEnterHurtState(
                                            target,
                                            random.Next(0, 4));

            int appliedLifeDamage;
            int shieldManaDamage = 0;
            // Original executable 0x424550-0x42457B: an active SpecialKind=3
            // body effect absorbs the complete hit only when current mana is
            // at least the calculated life damage, then consumes damage / 3.
            // If that reserve check fails, the complete hit reaches life and
            // the shield consumes no mana.
            if (target.HasActiveManaShield() && target.Mana >= lifeDamage)
            {
                appliedLifeDamage = 0;
                shieldManaDamage = lifeDamage / 3;
                target.Mana -= shieldManaDamage;
                if (shouldEnterHurtState)
                    target.SetState(JxqyCharacterState.Hurt);
            }
            else
            {
                appliedLifeDamage = Math.Min(target.Life, lifeDamage);
                target.TakeDamage(
                    appliedLifeDamage,
                    attacker,
                    shouldEnterHurtState);
            }
            int appliedManaDamage = Math.Min(target.Mana, Math.Max(0, manaDamage));
            target.Mana -= appliedManaDamage;
            return new JxqyDamageResult(
                true,
                appliedLifeDamage,
                shieldManaDamage + appliedManaDamage);
        }
    }

    public partial class JxqyCharacter
    {
        private readonly Dictionary<JxqyStatusKind, float> _statuses =
            new Dictionary<JxqyStatusKind, float>();
        private readonly Dictionary<string, JxqyActiveMagicEffect>
            _activeMagicEffects =
                new Dictionary<string, JxqyActiveMagicEffect>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<JxqyCharacterState> _disabledActionStates =
            new HashSet<JxqyCharacterState>();
        private float _poisonAccumulator;
        private float _reviveSecondsRemaining;

        public event Action<JxqyCharacter, JxqyCharacter> Died;
        public event Action<JxqyCharacter, int, JxqyCharacter> Damaged;
        public event Action<JxqyCharacter, int> Healed;
        public event Action<JxqyCharacter> Revived;

        public int Attack { get; set; }
        public int Attack2 { get; set; }
        public int Attack3 { get; set; }
        public int Defend { get; set; }
        public int Defend2 { get; set; }
        public int Defend3 { get; set; }
        public int Evade { get; set; }
        public bool CanEvade { get; set; } = true;
        public int Level { get; set; } = 1;
        public int AttackLevel { get; set; } = 1;
        public int DialogRadius { get; set; } = 1;
        public int Experience { get; set; }
        public int LevelUpExperience { get; set; }
        public int ExpBonus { get; set; }
        public bool Invincible { get; set; }
        public bool NoDropWhenDead { get; set; }
        public float LifeMilliseconds { get; set; }
        public bool IsFrozenVisualEffect { get; private set; }
        public bool IsPoisonVisualEffect { get; private set; }
        public bool IsPetrifiedVisualEffect { get; private set; }
        public string DropIni { get; set; } = string.Empty;
        public JxqyMagicDefinition MagicToUseWhenBeAttacked { get; set; }
        public int MagicDirectionWhenBeAttacked { get; set; }
        public float ReviveDelaySeconds { get; set; }
        public bool IsDead { get; private set; }
        public JxqyCharacter LastAttacker { get; private set; }
        public JxqyCharacter MagicSummoner { get; private set; }
        public JxqyPlayer MagicController { get; private set; }
        public string PoisonExperienceOwnerName { get; private set; } =
            string.Empty;
        public string PoisonDeathExperienceOwnerName { get; private set; } =
            string.Empty;
        public float ReviveSecondsRemaining => _reviveSecondsRemaining;
        public IReadOnlyCollection<JxqyCharacterState> DisabledActionStates =>
            _disabledActionStates;

        public bool IsActionEnabled(JxqyCharacterState state)
        {
            return !_disabledActionStates.Contains(state);
        }

        public void SetMagicSummoner(JxqyCharacter summoner)
        {
            MagicSummoner = ReferenceEquals(summoner, this)
                ? null
                : summoner;
        }

        public void SetMagicController(JxqyPlayer controller)
        {
            MagicController = ReferenceEquals(controller, this)
                ? null
                : controller;
        }

        public void SetPoisonExperienceOwner(string ownerName)
        {
            PoisonExperienceOwnerName = ownerName ?? string.Empty;
        }

        public void SetActionEnabled(
            JxqyCharacterState state,
            bool enabled)
        {
            if (enabled)
                _disabledActionStates.Remove(state);
            else
                _disabledActionStates.Add(state);
        }

        public void AddOrRefreshMagicEffect(
            JxqyMagicDefinition magic,
            float seconds)
        {
            if (magic == null || string.IsNullOrWhiteSpace(magic.Id))
                return;
            _activeMagicEffects[magic.Id] =
                new JxqyActiveMagicEffect(
                    magic.CreateRuntimeSnapshot(),
                    Math.Max(0.01f, seconds));
        }

        public bool HasActiveManaShield()
        {
            foreach (JxqyActiveMagicEffect active in
                     _activeMagicEffects.Values)
            {
                JxqyMagicDefinition magic = active.Magic;
                if (magic.MoveKind == 13 && magic.SpecialKind == 3)
                    return true;
            }
            return false;
        }

        public bool HasStatus(JxqyStatusKind kind)
        {
            return _statuses.TryGetValue(kind, out float seconds) && seconds > 0;
        }

        public float GetStatusSeconds(JxqyStatusKind kind)
        {
            return _statuses.TryGetValue(kind, out float seconds)
                ? Math.Max(0, seconds)
                : 0;
        }

        public void ApplyStatus(JxqyStatusKind kind, float seconds)
        {
            ApplyStatus(kind, seconds, true);
        }

        public void ApplyStatus(
            JxqyStatusKind kind,
            float seconds,
            bool hasVisualEffect)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                return;
            if (!_statuses.TryGetValue(kind, out float current) || seconds > current)
            {
                _statuses[kind] = seconds;
                SetStatusVisualEffect(kind, hasVisualEffect);
            }
            SynchronizeStatusGates();
        }

        public bool HasStatusVisualEffect(JxqyStatusKind kind)
        {
            return kind switch
            {
                JxqyStatusKind.Frozen => IsFrozenVisualEffect,
                JxqyStatusKind.Poisoned => IsPoisonVisualEffect,
                JxqyStatusKind.Petrified => IsPetrifiedVisualEffect,
                _ => false,
            };
        }

        public void RestoreStatusVisualEffects(
            bool frozen,
            bool poisoned,
            bool petrified)
        {
            IsFrozenVisualEffect = frozen &&
                HasStatus(JxqyStatusKind.Frozen);
            IsPoisonVisualEffect = poisoned &&
                HasStatus(JxqyStatusKind.Poisoned);
            IsPetrifiedVisualEffect = petrified &&
                HasStatus(JxqyStatusKind.Petrified);
        }

        public bool ClearStatus(JxqyStatusKind kind)
        {
            bool removed = _statuses.Remove(kind);
            SetStatusVisualEffect(kind, false);
            if (kind == JxqyStatusKind.Poisoned)
            {
                _poisonAccumulator = 0;
                PoisonExperienceOwnerName = string.Empty;
            }
            SynchronizeStatusGates();
            return removed;
        }

        private void SetStatusVisualEffect(
            JxqyStatusKind kind,
            bool enabled)
        {
            switch (kind)
            {
                case JxqyStatusKind.Frozen:
                    IsFrozenVisualEffect = enabled;
                    break;
                case JxqyStatusKind.Poisoned:
                    IsPoisonVisualEffect = enabled;
                    break;
                case JxqyStatusKind.Petrified:
                    IsPetrifiedVisualEffect = enabled;
                    break;
            }
        }

        public void AddLife(int amount)
        {
            if (IsDead && amount > 0)
                return;
            int previousLife = Life;
            Life += amount;
            int applied = Life - previousLife;
            if (applied > 0)
                Healed?.Invoke(this, applied);
            if (Life <= 0)
            {
                PoisonDeathExperienceOwnerName = string.Empty;
                Die(LastAttacker);
            }
        }

        public bool TakeDamage(
            int amount,
            JxqyCharacter attacker = null,
            bool enterHurtState = true)
        {
            if (amount <= 0 || IsDead || Invincible)
                return false;
            LastAttacker = attacker;
            int applied = Math.Min(Life, amount);
            Life -= applied;
            Damaged?.Invoke(this, applied, attacker);
            if (Life <= 0)
            {
                PoisonDeathExperienceOwnerName = string.Empty;
                Die(attacker);
            }
            else if (enterHurtState && !IsPetrified &&
                     IsActionEnabled(JxqyCharacterState.Hurt))
                SetState(JxqyCharacterState.Hurt);
            return true;
        }

        public bool Die(JxqyCharacter attacker = null)
        {
            if (IsDead)
                return false;
            IsDead = true;
            LastAttacker = attacker;
            Life = 0;
            Stop();
            SetState(JxqyCharacterState.Death);
            _reviveSecondsRemaining = Math.Max(0, ReviveDelaySeconds);
            Died?.Invoke(this, LastAttacker);
            return true;
        }

        public bool Revive()
        {
            if (!IsDead)
                return false;
            IsDead = false;
            Life = LifeMax;
            PoisonDeathExperienceOwnerName = string.Empty;
            _reviveSecondsRemaining = 0;
            SetState(JxqyCharacterState.Stand);
            Revived?.Invoke(this);
            return true;
        }

        public void ResetDeathStateForRestore()
        {
            // The original loader constructs a fresh Player before applying
            // the save. Reusing the live Unity player must therefore discard
            // death-only state without raising a gameplay revive event.
            IsDead = false;
            LastAttacker = null;
            PoisonDeathExperienceOwnerName = string.Empty;
            _reviveSecondsRemaining = 0;
        }

        public void RestoreReviveSecondsRemaining(float seconds)
        {
            _reviveSecondsRemaining = IsDead
                ? Math.Max(0, seconds)
                : 0;
        }

        public void TickCombat(float elapsedSeconds)
        {
            if (elapsedSeconds < 0 || float.IsNaN(elapsedSeconds) ||
                float.IsInfinity(elapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (IsDead)
            {
                if (_reviveSecondsRemaining > 0)
                {
                    _reviveSecondsRemaining -= elapsedSeconds;
                    if (_reviveSecondsRemaining <= 0)
                        Revive();
                }
                return;
            }

            if (LifeMilliseconds > 0)
            {
                LifeMilliseconds = Math.Max(
                    0,
                    LifeMilliseconds - elapsedSeconds * 1000f);
                if (LifeMilliseconds <= 0)
                {
                    Die();
                    return;
                }
            }

            if (HasStatus(JxqyStatusKind.Poisoned))
            {
                _poisonAccumulator += elapsedSeconds;
                if (_poisonAccumulator > 0.25f)
                {
                    _poisonAccumulator = 0;
                    string poisonOwner = PoisonExperienceOwnerName;
                    bool lethal = Life <= 10;
                    ApplyPoisonDamage(10);
                    if (lethal && IsDead)
                    {
                        PoisonDeathExperienceOwnerName =
                            poisonOwner ?? string.Empty;
                    }
                }
            }
            TickStatuses(elapsedSeconds);
            TickActiveMagicEffects(elapsedSeconds);
        }

        private void ApplyPoisonDamage(int amount)
        {
            if (amount <= 0 || IsDead)
                return;
            int applied = Math.Min(Life, amount);
            Life -= applied;
            Damaged?.Invoke(this, applied, null);
            if (Life <= 0)
                Die();
        }

        private void TickActiveMagicEffects(float elapsedSeconds)
        {
            if (_activeMagicEffects.Count == 0)
                return;
            var expired = new List<string>();
            foreach (KeyValuePair<string, JxqyActiveMagicEffect> pair in
                     _activeMagicEffects)
            {
                pair.Value.RemainingSeconds -= elapsedSeconds;
                if (pair.Value.RemainingSeconds <= 0)
                    expired.Add(pair.Key);
            }
            foreach (string key in expired)
                _activeMagicEffects.Remove(key);
        }

        private void TickStatuses(float elapsedSeconds)
        {
            if (_statuses.Count == 0)
                return;
            var keys = new List<JxqyStatusKind>(_statuses.Keys);
            foreach (JxqyStatusKind key in keys)
            {
                float remaining = _statuses[key] - elapsedSeconds;
                if (remaining <= 0)
                    ClearStatus(key);
                else
                    _statuses[key] = remaining;
            }
            if (!HasStatus(JxqyStatusKind.Poisoned))
                _poisonAccumulator = 0;
            SynchronizeStatusGates();
        }

        private void SynchronizeStatusGates()
        {
            IsPetrified = HasStatus(JxqyStatusKind.Petrified);
            IsMovementDisabled =
                HasStatus(JxqyStatusKind.MovementDisabled);
        }
    }

    internal sealed class JxqyActiveMagicEffect
    {
        public JxqyActiveMagicEffect(
            JxqyMagicDefinition magic,
            float remainingSeconds)
        {
            Magic = magic;
            RemainingSeconds = remainingSeconds;
        }

        public JxqyMagicDefinition Magic { get; }
        public float RemainingSeconds { get; set; }
    }

    public sealed class JxqyDropContentProfile
    {
        public JxqyDropContentProfile(
            string objectIniDirectory,
            IReadOnlyDictionary<JxqyDropKind, string> resourceFileNames,
            IReadOnlyList<string> drugScriptFileNames,
            IReadOnlyDictionary<JxqyDropKind, string> gradedScriptSuffixes)
        {
            ObjectIniDirectory = string.IsNullOrWhiteSpace(objectIniDirectory)
                ? throw new ArgumentException(
                    "Drop object directory is required.",
                    nameof(objectIniDirectory))
                : objectIniDirectory.Trim().Replace('\\', '/').Trim('/');
            ResourceFileNames = new Dictionary<JxqyDropKind, string>(
                resourceFileNames ??
                throw new ArgumentNullException(nameof(resourceFileNames)));
            DrugScriptFileNames =
                (drugScriptFileNames ??
                 throw new ArgumentNullException(nameof(drugScriptFileNames)))
                .ToArray();
            if (DrugScriptFileNames.Count != 4)
                throw new ArgumentException(
                    "Drop profile requires four drug script tiers.",
                    nameof(drugScriptFileNames));
            GradedScriptSuffixes =
                new Dictionary<JxqyDropKind, string>(
                    gradedScriptSuffixes ??
                    throw new ArgumentNullException(
                        nameof(gradedScriptSuffixes)));
        }

        public string ObjectIniDirectory { get; }
        public IReadOnlyDictionary<JxqyDropKind, string> ResourceFileNames
        {
            get;
        }
        public IReadOnlyList<string> DrugScriptFileNames { get; }
        public IReadOnlyDictionary<JxqyDropKind, string> GradedScriptSuffixes
        {
            get;
        }

        public string ResolveResourcePath(JxqyDropKind kind)
        {
            if (!ResourceFileNames.TryGetValue(kind, out string fileName) ||
                string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(
                    $"Drop resource is not configured for {kind}.");
            }
            return ObjectIniDirectory + "/" + fileName;
        }

        public string ResolveScriptFile(JxqyDropKind kind, int level)
        {
            if (kind == JxqyDropKind.Drug)
            {
                int tier = level <= 10
                    ? 0
                    : level <= 30
                        ? 1
                        : level <= 60
                            ? 2
                            : 3;
                return DrugScriptFileNames[tier];
            }
            if (!GradedScriptSuffixes.TryGetValue(
                    kind,
                    out string suffix) ||
                string.IsNullOrWhiteSpace(suffix))
            {
                throw new InvalidOperationException(
                    $"Drop script suffix is not configured for {kind}.");
            }
            int grade = Math.Min(7, Math.Max(0, level) / 12 + 1);
            return grade + suffix;
        }

        public static JxqyDropContentProfile XinJianXia { get; } =
            new(
                "ini/obj",
                new Dictionary<JxqyDropKind, string>
                {
                    [JxqyDropKind.Weapon] = "可捡武器.ini",
                    [JxqyDropKind.Armor] = "可捡防具.ini",
                    [JxqyDropKind.Money] = "可捡钱.ini",
                    [JxqyDropKind.Drug] = "可捡药品.ini",
                },
                new[]
                {
                    "低级药品.txt",
                    "中级药品.txt",
                    "高级药品.txt",
                    "特级药品.txt",
                },
                new Dictionary<JxqyDropKind, string>
                {
                    [JxqyDropKind.Weapon] = "级武器.txt",
                    [JxqyDropKind.Armor] = "级防具.txt",
                    [JxqyDropKind.Money] = "级钱.txt",
                });
    }

    public static class JxqyDropGenerator
    {
        public static JxqyDrop Generate(
            JxqyCharacter character,
            JxqyDeterministicRandom random,
            JxqyDropContentProfile profile,
            bool dropsDisabled = false)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (dropsDisabled || character.Relation != JxqyRelationType.Enemy ||
                character.NoDropWhenDead)
                return null;

            if (!string.IsNullOrWhiteSpace(character.DropIni))
            {
                string ini = character.DropIni.Trim();
                if (ini.EndsWith("]", StringComparison.Ordinal))
                {
                    int opening = ini.LastIndexOf('[', ini.Length - 1);
                    if (opening < 0 ||
                        !int.TryParse(
                            ini.Substring(opening + 1, ini.Length - opening - 2),
                            out int chance))
                        throw new FormatException(
                            $"DropIni 格式错误：{character.DropIni}");
                    if (random.Next(0, 100) > chance)
                        return null;
                    ini = ini.Substring(0, opening);
                }
                return AtCharacter(
                    new JxqyDrop(
                        JxqyDropKind.Drug,
                        profile.ObjectIniDirectory + "/" + ini,
                        string.Empty),
                    character);
            }

            JxqyDropKind kind;
            if (character.ExpBonus > 0)
            {
                kind = random.Next(0, 2) == 0
                    ? JxqyDropKind.Weapon
                    : JxqyDropKind.Armor;
            }
            else
            {
                kind = (JxqyDropKind)random.Next(0, 4);
                int denominator =
                    kind == JxqyDropKind.Weapon ||
                    kind == JxqyDropKind.Armor
                        ? 10
                        : 2;
                if (random.Next(0, denominator) != 0)
                    return null;
            }

            int effectiveLevel = character.Level;
            if (character.ExpBonus > 0)
            {
                int roll = random.Next(0, 100);
                if (roll >= 60)
                    effectiveLevel += 24;
                else if (roll >= 10)
                    effectiveLevel += 12;
            }
            return AtCharacter(
                new JxqyDrop(
                    kind,
                    profile.ResolveResourcePath(kind),
                    profile.ResolveScriptFile(kind, effectiveLevel)),
                character);
        }

        private static JxqyDrop AtCharacter(
            JxqyDrop drop,
            JxqyCharacter character)
        {
            drop.PlaceAt(character.TilePosition);
            return drop;
        }
    }

    public sealed class JxqyMagicProjectile
    {
        private const float LegacyInitialAdvance = 30f;

        private JxqyFloat2[] _pathPoints;
        private int _pathPointIndex;

        internal JxqyMagicProjectile()
        {
        }

        internal void Initialize(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 destination,
            JxqyCharacter target,
            float speed,
            float delaySeconds)
        {
            Source = source;
            Magic = magic;
            Position = origin;
            Destination = destination;
            Direction = (destination - Position).Normalized;
            PresentationDirection = Direction;
            Target = target;
            Speed = Math.Max(0, speed);
            DelaySeconds = Math.Max(0, delaySeconds);
            RemainingSeconds = Math.Max(0.01f, magic.LifeSeconds);
            MovedDistance = 0;
            _pathPoints = null;
            _pathPointIndex = 0;
            IsComplete = false;
            WasBlockedByWall = false;
        }

        internal void ApplyLegacyInitialAdvance()
        {
            if (Speed <= 0 || Direction == JxqyFloat2.Zero)
                return;
            JxqyFloat2 offset = Direction * LegacyInitialAdvance;
            Position += offset;
            MovedDistance += offset.Length;
        }

        internal void SetPath(JxqyFloat2[] points)
        {
            _pathPoints = points != null && points.Length > 1
                ? points
                : null;
            _pathPointIndex = _pathPoints == null ? 0 : 1;
            MovedDistance = 0;
            if (_pathPoints != null)
            {
                Position = _pathPoints[0];
                Destination = _pathPoints[_pathPoints.Length - 1];
                Direction = (_pathPoints[1] - Position).Normalized;
                PresentationDirection = Direction;
            }
        }

        internal void SetPresentationDirection(JxqyFloat2 direction)
        {
            if (direction != JxqyFloat2.Zero)
                PresentationDirection = direction.Normalized;
        }

        internal bool TryGetNextPathPoint(out JxqyFloat2 point)
        {
            if (_pathPoints != null &&
                _pathPointIndex < _pathPoints.Length)
            {
                point = _pathPoints[_pathPointIndex];
                return true;
            }
            point = JxqyFloat2.Zero;
            return false;
        }

        internal bool CompleteCurrentPathSegment()
        {
            _pathPointIndex++;
            MovedDistance = 0;
            return _pathPoints == null ||
                   _pathPointIndex >= _pathPoints.Length;
        }

        internal void Clear()
        {
            Source = null;
            Magic = null;
            Target = null;
            Position = JxqyFloat2.Zero;
            Destination = JxqyFloat2.Zero;
            Direction = JxqyFloat2.Zero;
            PresentationDirection = JxqyFloat2.Zero;
            Speed = 0;
            DelaySeconds = 0;
            RemainingSeconds = 0;
            MovedDistance = 0;
            _pathPoints = null;
            _pathPointIndex = 0;
            IsComplete = false;
            WasBlockedByWall = false;
        }

        public JxqyCharacter Source { get; private set; }
        public JxqyMagicDefinition Magic { get; private set; }
        public JxqyCharacter Target { get; internal set; }
        public JxqyFloat2 Position { get; internal set; }
        public JxqyFloat2 Destination { get; private set; }
        public JxqyFloat2 Direction { get; internal set; }
        public JxqyFloat2 PresentationDirection { get; private set; }
        public float Speed { get; private set; }
        public float DelaySeconds { get; internal set; }
        public float RemainingSeconds { get; internal set; }
        public float MovedDistance { get; internal set; }
        public int PathPointCount => _pathPoints?.Length ?? 0;
        public bool IsComplete { get; internal set; }
        public bool WasBlockedByWall { get; internal set; }
    }

    public sealed class JxqyCombatSystem
    {
        private const int ProjectilePoolCapacity = 256;
        private readonly List<JxqyMagicProjectile> _projectiles =
            new List<JxqyMagicProjectile>();
        private readonly Stack<JxqyMagicProjectile> _projectilePool =
            new Stack<JxqyMagicProjectile>();
        private readonly JxqyDeterministicRandom _random;
        private int _createdProjectileCount;
        private int _reusedProjectileCount;

        public JxqyCombatSystem(JxqyDeterministicRandom random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public IReadOnlyList<JxqyMagicProjectile> Projectiles => _projectiles;
        public int CreatedProjectileCount => _createdProjectileCount;
        public int ReusedProjectileCount => _reusedProjectileCount;
        public int PooledProjectileCount => _projectilePool.Count;
        public bool IsSuperModeActive
        {
            get
            {
                foreach (JxqyMagicProjectile projectile in _projectiles)
                {
                    if (projectile.Magic?.MoveKind == 15 &&
                        !projectile.IsComplete)
                        return true;
                }
                return false;
            }
        }
        public event Action<JxqyMagicProjectile> ProjectileResolved;
        public event Action<JxqyMagicProjectile> ProjectileExpired;
        public event Action<JxqyMagicProjectile> ProjectileSpawned;
        public event Action<
            JxqyMagicProjectile,
            JxqyCharacter,
            JxqyDamageResult> MagicContacted;
        public event Action<
            JxqyCharacter,
            JxqyMagicDefinition,
            JxqyFloat2> MagicUsed;
        public event Action<
            JxqyCharacter,
            JxqyCharacter,
            JxqyMagicDefinition,
            int> MagicHealed;

        public bool UseMagic(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 destination,
            JxqyCharacter target = null,
            bool consumePlayerResources = true)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (magic == null)
                throw new ArgumentNullException(nameof(magic));
            bool consumesPlayerResources =
                consumePlayerResources && source is JxqyPlayer;
            if (source.IsDead ||
                source.HasStatus(JxqyStatusKind.SkillDisabled) ||
                consumesPlayerResources &&
                (source.Mana < magic.ManaCost ||
                 source.Thew < magic.ThewCost ||
                 source.Life <= magic.LifeCost))
                return false;

            // The legacy target argument is not a generic homing target. It
            // is consumed by only a few MoveKinds. MoveKind 13 can be cast on
            // a fighter friend only by the player; every NPC body effect is
            // attached to its caster. Normalize here so presentation and
            // settlement share the exact same owner.
            if (magic.MoveKind == 13)
            {
                target = JxqyMagicTargeting.ResolveBodyEffectTarget(
                    source,
                    target);
                destination = target.PositionInWorld;
            }

            // Original Character/Partner basic-attack magic is emitted by
            // MagicManager without paying player resources. Only the Player
            // override validates and deducts Mana/Thew/Life costs.
            if (consumesPlayerResources)
            {
                source.Mana -= Math.Max(0, magic.ManaCost);
                source.Thew -= Math.Max(0, magic.ThewCost);
                source.TakeDamage(Math.Max(0, magic.LifeCost));
            }
            // The caller owns the presentation state. Normal skills and
            // ranged basic attacks reach this method only after their action
            // animation finishes, while be-attacked retaliation is emitted
            // directly by the original runtime without a cast animation.
            // Changing state here could therefore restart/replace an action
            // independently of the projectile that actually deals damage.
            MagicUsed?.Invoke(source, magic, destination);
            if (magic.ReviveBodyRadius <= 0)
                EmitProjectiles(source, magic, destination, target);

            if (magic.SideEffectProbability > 0 &&
                _random.Next(0, 100) < magic.SideEffectProbability)
            {
                int total =
                    JxqyDamageCalculator.GetMagicEffectAmount(
                        source,
                        magic,
                        1) +
                    JxqyDamageCalculator.GetMagicEffectAmount(
                        source,
                        magic,
                        2) +
                    JxqyDamageCalculator.GetMagicEffectAmount(
                        source,
                        magic,
                        3);
                int sideEffect =
                    total * magic.SideEffectPercent / 100;
                switch (magic.SideEffectType)
                {
                    case 1:
                        source.Mana -= sideEffect;
                        break;
                    case 2:
                        source.Thew -= sideEffect;
                        break;
                    default:
                        source.TakeDamage(sideEffect);
                        break;
                }
            }
            if (magic.DieAfterUse)
                source.Die();
            return true;
        }

        public void Tick(float elapsedSeconds)
        {
            Tick(elapsedSeconds, null, null);
        }

        public void Tick(
            float elapsedSeconds,
            IReadOnlyList<JxqyCharacter> collisionTargets)
        {
            Tick(elapsedSeconds, collisionTargets, null);
        }

        public void Tick(
            float elapsedSeconds,
            IReadOnlyList<JxqyCharacter> collisionTargets,
            Func<JxqyFloat2, bool> isBlocked)
        {
            if (elapsedSeconds < 0 || float.IsNaN(elapsedSeconds) ||
                float.IsInfinity(elapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            for (int index = _projectiles.Count - 1; index >= 0; index--)
            {
                JxqyMagicProjectile projectile = _projectiles[index];
                if (projectile.IsComplete)
                {
                    _projectiles.RemoveAt(index);
                    ReleaseProjectile(projectile);
                    continue;
                }
                if (projectile.DelaySeconds > 0)
                {
                    projectile.DelaySeconds = Math.Max(
                        0,
                        projectile.DelaySeconds - elapsedSeconds);
                    continue;
                }
                if (projectile.Magic.MoveKind == 15)
                {
                    projectile.RemainingSeconds -= elapsedSeconds;
                    if (projectile.RemainingSeconds > 0)
                        continue;
                    ResolveSuperModeProjectile(
                        projectile,
                        collisionTargets);
                    _projectiles.RemoveAt(index);
                    ReleaseProjectile(projectile);
                    continue;
                }
                if (projectile.TryGetNextPathPoint(
                        out JxqyFloat2 pathDestination))
                {
                    AdvancePathProjectile(
                        projectile,
                        pathDestination,
                        elapsedSeconds);
                    if (projectile.TryGetNextPathPoint(out _))
                        continue;
                    JxqyCharacter pathCollisionTarget =
                        FindCollisionTarget(
                            projectile,
                            collisionTargets,
                            projectile.Position,
                            projectile.Position);
                    if (pathCollisionTarget != null)
                    {
                        ResolveProjectile(
                            projectile,
                            pathCollisionTarget);
                    }
                    else
                    {
                        ResolveProjectileImpact(projectile);
                    }
                    _projectiles.RemoveAt(index);
                    ReleaseProjectile(projectile);
                    continue;
                }
                float activeSeconds = Math.Min(
                    elapsedSeconds,
                    Math.Max(0, projectile.RemainingSeconds));
                projectile.RemainingSeconds -= elapsedSeconds;
                bool expiresAfterMovement =
                    projectile.RemainingSeconds <= 0;
                UpdateFollowEnemyTarget(projectile, collisionTargets);
                float step = GetProjectileStep(projectile, activeSeconds);
                if (projectile.Target == null)
                {
                    JxqyFloat2 untargetedMovementOrigin =
                        projectile.Position;
                    JxqyFloat2 untargetedNext =
                        projectile.Position + projectile.Direction * step;
                    if (projectile.Magic.PassThroughWall == 0 &&
                        TryFindBlockedPosition(
                            projectile.Position,
                            untargetedNext,
                            isBlocked,
                            out JxqyFloat2 untargetedBlockedPosition))
                    {
                        MoveProjectileTo(
                            projectile,
                            untargetedBlockedPosition);
                        bool hasTravelled =
                            untargetedMovementOrigin != untargetedNext;
                        JxqyCharacter blockedCollisionTarget = hasTravelled
                            ? FindCollisionTarget(
                                projectile,
                                collisionTargets,
                                untargetedMovementOrigin,
                                projectile.Position)
                            : null;
                        if (blockedCollisionTarget != null)
                        {
                            ResolveProjectile(
                                projectile,
                                blockedCollisionTarget);
                        }
                        else
                        {
                            ResolveProjectileImpact(projectile);
                        }
                        _projectiles.RemoveAt(index);
                        ReleaseProjectile(projectile);
                        continue;
                    }
                    MoveProjectileTo(projectile, untargetedNext);
                    JxqyCharacter collisionTarget =
                        FindCollisionTarget(
                            projectile,
                            collisionTargets,
                            untargetedMovementOrigin,
                            projectile.Position);
                    if (collisionTarget != null)
                    {
                        ResolveProjectile(projectile, collisionTarget);
                        _projectiles.RemoveAt(index);
                        ReleaseProjectile(projectile);
                        continue;
                    }
                    if (expiresAfterMovement)
                        ExpireProjectile(index, projectile);
                    continue;
                }
                JxqyFloat2 destination = projectile.Target != null
                    ? projectile.Target.PositionInWorld
                    : projectile.Destination;
                JxqyFloat2 offset = destination - projectile.Position;
                if (offset != JxqyFloat2.Zero)
                    projectile.Direction = offset.Normalized;
                bool reachesTarget = offset.Length <=
                                     Math.Max(
                                         projectile.Magic.Radius,
                                         step);
                JxqyFloat2 next = reachesTarget
                    ? destination
                    : projectile.Position + offset.Normalized * step;
                JxqyFloat2 targetedMovementOrigin = projectile.Position;
                if (projectile.Magic.PassThroughWall == 0 &&
                    TryFindBlockedPosition(
                        projectile.Position,
                        next,
                        isBlocked,
                        out JxqyFloat2 blockedPosition))
                {
                    MoveProjectileTo(projectile, blockedPosition);
                    JxqyCharacter blockedCollisionTarget =
                        FindCollisionTarget(
                            projectile,
                            collisionTargets,
                            targetedMovementOrigin,
                            projectile.Position);
                    if (blockedCollisionTarget != null)
                    {
                        ResolveProjectile(
                            projectile,
                            blockedCollisionTarget);
                    }
                    else
                    {
                        ResolveProjectileImpact(projectile);
                    }
                    _projectiles.RemoveAt(index);
                    ReleaseProjectile(projectile);
                }
                else
                {
                    MoveProjectileTo(projectile, next);
                    JxqyCharacter collisionTarget =
                        FindCollisionTarget(
                            projectile,
                            collisionTargets,
                            targetedMovementOrigin,
                            projectile.Position);
                    if (collisionTarget != null)
                    {
                        ResolveProjectile(projectile, collisionTarget);
                        _projectiles.RemoveAt(index);
                        ReleaseProjectile(projectile);
                    }
                    else if (reachesTarget)
                    {
                        ResolveProjectile(projectile);
                        _projectiles.RemoveAt(index);
                        ReleaseProjectile(projectile);
                    }
                    else if (expiresAfterMovement)
                    {
                        ExpireProjectile(index, projectile);
                    }
                }
            }
        }

        private void ExpireProjectile(
            int index,
            JxqyMagicProjectile projectile)
        {
            projectile.IsComplete = true;
            ProjectileExpired?.Invoke(projectile);
            _projectiles.RemoveAt(index);
            ReleaseProjectile(projectile);
        }

        private static void AdvancePathProjectile(
            JxqyMagicProjectile projectile,
            JxqyFloat2 destination,
            float elapsedSeconds)
        {
            JxqyFloat2 offset = destination - projectile.Position;
            float distance = offset.Length;
            if (distance <= 0.001f)
            {
                projectile.Position = destination;
                projectile.CompleteCurrentPathSegment();
                return;
            }
            projectile.Direction = offset.Normalized;
            float step = Math.Max(0f, projectile.Speed * elapsedSeconds);
            if (step >= distance)
            {
                projectile.MovedDistance += distance;
                projectile.Position = destination;
                projectile.CompleteCurrentPathSegment();
                return;
            }
            projectile.Position += projectile.Direction * step;
            projectile.MovedDistance += step;
        }

        private static bool TryFindBlockedPosition(
            JxqyFloat2 start,
            JxqyFloat2 end,
            Func<JxqyFloat2, bool> isBlocked,
            out JxqyFloat2 blockedPosition)
        {
            blockedPosition = end;
            if (isBlocked == null)
                return false;
            JxqyFloat2 offset = end - start;
            float distance = offset.Length;
            int sampleCount = Math.Max(
                1,
                (int)Math.Ceiling(distance / 8f));
            for (int sample = 1; sample <= sampleCount; sample++)
            {
                JxqyFloat2 position =
                    start + offset * (sample / (float)sampleCount);
                if (!isBlocked(position))
                    continue;
                blockedPosition = position;
                return true;
            }
            return false;
        }

        private JxqyMagicProjectile AcquireProjectile(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 destination,
            JxqyCharacter target,
            float speed,
            float delaySeconds)
        {
            JxqyMagicProjectile projectile;
            if (_projectilePool.Count > 0)
            {
                projectile = _projectilePool.Pop();
                _reusedProjectileCount++;
            }
            else
            {
                projectile = new JxqyMagicProjectile();
                _createdProjectileCount++;
            }
            projectile.Initialize(
                source,
                magic,
                origin,
                destination,
                target,
                speed,
                delaySeconds);
            return projectile;
        }

        private void EmitProjectiles(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 destination,
            JxqyCharacter target)
        {
            JxqyFloat2 origin = source.PositionInWorld;
            JxqyFloat2 forward = (destination - origin).Normalized;
            if (forward == JxqyFloat2.Zero)
                forward = DirectionFromIndex(source.CurrentDirection);
            int level = Math.Max(1, magic.EffectLevel);
            switch (magic.MoveKind)
            {
                case 3:
                    float lineSpeed = GetLegacyProjectileSpeed(
                        magic,
                        destination - origin);
                    for (int index = 0; index < level; index++)
                    {
                        SpawnProjectile(
                            source,
                            magic,
                            origin,
                            destination,
                            null,
                            lineSpeed,
                            index * 0.06f);
                    }
                    break;
                case 4:
                    SpawnCircle(source, magic, origin);
                    break;
                case 6:
                    SpawnSpiral(source, magic, origin, destination);
                    break;
                case 7:
                case 8:
                    SpawnSector(
                        source,
                        magic,
                        origin,
                        forward,
                        level,
                        magic.MoveKind == 8);
                    break;
                case 9:
                    // Original MagicManager.AddFixedWallMagicSprite: center
                    // the wall at the requested target and extend it along
                    // the direction perpendicular to the cast.
                    int wallDirection = JxqyDirection.GetIndex(
                        destination - origin,
                        8);
                    int wallCount = 3 + Math.Max(0, level - 1) * 2;
                    SpawnFixedLine(
                        source,
                        magic,
                        destination,
                        GetLegacyWallOffset(wallDirection),
                        wallCount,
                        0,
                        forward);
                    break;
                case 10:
                    SpawnMovingWall(
                        source,
                        magic,
                        origin,
                        forward,
                        level);
                    break;
                case 11:
                    SpawnRegion(
                        source,
                        magic,
                        origin,
                        destination,
                        forward,
                        level);
                    break;
                case 13:
                    SpawnProjectile(
                        source,
                        magic,
                        origin,
                        target?.PositionInWorld ?? origin,
                        target ?? source,
                        0,
                        0);
                    break;
                case 16:
                    SpawnProjectile(
                        source,
                        magic,
                        origin,
                        destination == origin
                            ? origin + forward * 64f
                            : destination,
                        null,
                        magic.ProjectileSpeed,
                        0);
                    break;
                case 17:
                    SpawnThrowGrid(
                        source,
                        magic,
                        origin,
                        destination == origin
                            ? origin + forward * 64f
                            : destination,
                        level);
                    break;
                case 2:
                default:
                    SpawnProjectile(
                        source,
                        magic,
                        origin,
                        destination == origin
                            ? origin + forward * 64f
                            : destination,
                        null,
                        GetLegacyProjectileSpeed(
                            magic,
                            destination == origin
                                ? forward
                                : destination - origin),
                        0);
                    break;
            }
        }

        private void SpawnThrowGrid(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 destination,
            int level)
        {
            int count = 1 + Math.Max(0, level - 1) / 3;
            var columnOffset = new JxqyFloat2(-32f, 16f);
            var rowOffset = new JxqyFloat2(32f, 16f);
            int halfCount = count / 2;
            JxqyFloat2 rowDestination =
                destination - rowOffset * halfCount;
            for (int row = 0; row < count; row++)
            {
                JxqyFloat2 cellDestination =
                    rowDestination - columnOffset * halfCount;
                for (int column = 0; column < count; column++)
                {
                    JxqyMagicProjectile projectile = SpawnProjectile(
                        source,
                        magic,
                        origin,
                        cellDestination,
                        null,
                        magic.ProjectileSpeed,
                        0);
                    projectile.SetPath(
                        BuildLegacyThrowPath(
                            origin,
                            cellDestination));
                    cellDestination += columnOffset;
                }
                rowDestination += rowOffset;
            }
        }

        private static JxqyFloat2[] BuildLegacyThrowPath(
            JxqyFloat2 origin,
            JxqyFloat2 destination)
        {
            float distance = JxqyFloat2.Distance(origin, destination);
            int count = Math.Max(4, (int)distance / 64);
            int halfCount = count / 2;
            JxqyFloat2 pathUnit = (destination - origin) / count;
            float offsetUnit = distance / count;
            var points = new JxqyFloat2[count + 1];
            points[0] = origin;
            for (int index = 0; index < count - 1; index++)
            {
                float verticalOffset = index < halfCount - 1
                    ? -(index + 1) * offsetUnit
                    : -(count - index + 1) * offsetUnit;
                points[index + 1] =
                    origin + (index + 1) * pathUnit +
                    new JxqyFloat2(0f, verticalOffset);
            }
            points[count] = destination;
            return points;
        }

        private void SpawnCircle(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin)
        {
            for (int index = 0; index < 32; index++)
            {
                JxqyFloat2 direction = Direction32(index);
                SpawnProjectile(
                    source,
                    magic,
                    origin,
                    origin + direction * 128f,
                    null,
                    GetLegacyProjectileSpeed(magic, direction),
                    0);
            }
        }

        private void SpawnSpiral(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 destination)
        {
            int directionIndex = JxqyDirection.GetIndex(
                destination - origin,
                32);
            for (int index = 0; index < 32; index++)
            {
                JxqyFloat2 direction =
                    Direction32((directionIndex + index) % 32);
                SpawnProjectile(
                    source,
                    magic,
                    origin,
                    origin + direction * 128f,
                    null,
                    GetLegacyProjectileSpeed(magic, direction),
                    index * 0.03f);
            }
        }

        private void SpawnSector(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 forward,
            int level,
            bool staggered)
        {
            int projectileCount;
            double startAngle;
            double angleStep;
            if (level < 4)
            {
                projectileCount = 3;
                startAngle = -Math.PI / 12d;
                angleStep = Math.PI / 12d;
            }
            else if (level < 7)
            {
                projectileCount = 5;
                startAngle = -Math.PI / 10d;
                angleStep = Math.PI / 20d;
            }
            else if (level < 10)
            {
                projectileCount = 7;
                startAngle = -Math.PI / 5d;
                angleStep = Math.PI / 15d;
            }
            else
            {
                projectileCount = 9;
                startAngle = -Math.PI / 4d;
                angleStep = Math.PI / 16d;
            }

            for (int index = 0; index < projectileCount; index++)
            {
                JxqyFloat2 direction = RotateDirection(
                    forward,
                    startAngle + index * angleStep);
                SpawnSectorProjectile(
                    source,
                    magic,
                    origin,
                    direction,
                    staggered);
            }
        }

        private void SpawnSectorProjectile(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 direction,
            bool staggered)
        {
            SpawnProjectile(
                source,
                magic,
                origin,
                origin + direction * 128f,
                null,
                GetLegacyProjectileSpeed(magic, direction),
                staggered ? _random.Next(0, 200) / 1000f : 0);
        }

        private static JxqyFloat2 RotateDirection(
            JxqyFloat2 direction,
            double angle)
        {
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            return new JxqyFloat2(
                (float)(direction.X * cosine - direction.Y * sine),
                (float)(direction.X * sine + direction.Y * cosine)).Normalized;
        }

        private void SpawnMovingWall(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 forward,
            int level)
        {
            int sideCount = Math.Max(1, level);
            int directionIndex = JxqyDirection.GetIndex(forward, 8);
            JxqyFloat2 direction = Direction32(directionIndex * 4);
            JxqyFloat2 offset = GetLegacyWallOffset(directionIndex);
            float speed = GetLegacyProjectileSpeed(magic, direction);
            for (int index = -sideCount; index <= sideCount; index++)
            {
                JxqyFloat2 start = origin + offset * index;
                SpawnProjectile(
                    source,
                    magic,
                    start,
                    start + direction * 128f,
                    null,
                    speed,
                    0);
            }
        }

        private void SpawnRegion(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 destination,
            JxqyFloat2 forward,
            int level)
        {
            int count = 3;
            if (level > 3)
                count += ((level - 1) / 3) * 2;
            switch (magic.Region)
            {
                case 1:
                    SpawnSquareRegion(
                        source,
                        magic,
                        destination,
                        count);
                    break;
                case 2:
                    SpawnCrossRegion(source, magic, origin, count);
                    break;
                case 3:
                    SpawnRectangleRegion(
                        source,
                        magic,
                        origin,
                        forward,
                        count);
                    break;
                case 4:
                    SpawnTriangleRegion(
                        source,
                        magic,
                        origin,
                        forward,
                        count);
                    break;
                default:
                    SpawnFixedProjectile(
                        source,
                        magic,
                        destination,
                        0);
                    break;
            }
        }

        private void SpawnSquareRegion(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 destination,
            int count)
        {
            var rowOffset = new JxqyFloat2(32f, 16f);
            var columnOffset = new JxqyFloat2(32f, -16f);
            int halfCount = count / 2;
            JxqyFloat2 rowMiddle =
                destination - rowOffset * halfCount;
            for (int row = 0; row < count; row++)
            {
                SpawnFixedLine(
                    source,
                    magic,
                    rowMiddle,
                    columnOffset,
                    count,
                    0);
                rowMiddle += rowOffset;
            }
        }

        private void SpawnCrossRegion(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            int count)
        {
            JxqyFloat2[] offsets =
            {
                new JxqyFloat2(32f, 16f),
                new JxqyFloat2(32f, -16f),
                new JxqyFloat2(-32f, 16f),
                new JxqyFloat2(-32f, -16f),
            };
            for (int index = 0; index < count; index++)
            {
                float delay = index * 0.06f;
                foreach (JxqyFloat2 offset in offsets)
                {
                    SpawnFixedProjectile(
                        source,
                        magic,
                        origin + offset * (index + 1),
                        delay);
                }
            }
        }

        private void SpawnRectangleRegion(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 direction,
            int count)
        {
            const int columnCount = 5;
            int directionIndex = JxqyDirection.GetIndex(direction, 8);
            JxqyFloat2 rowMiddle = origin;
            switch (directionIndex)
            {
                case 1:
                case 3:
                case 5:
                case 7:
                {
                    JxqyFloat2 columnOffset;
                    JxqyFloat2 rowOffset;
                    switch (directionIndex)
                    {
                        case 1:
                            columnOffset = new JxqyFloat2(32f, 16f);
                            rowOffset = new JxqyFloat2(-32f, 16f);
                            break;
                        case 3:
                            columnOffset = new JxqyFloat2(32f, -16f);
                            rowOffset = new JxqyFloat2(-32f, -16f);
                            break;
                        case 5:
                            columnOffset = new JxqyFloat2(32f, 16f);
                            rowOffset = new JxqyFloat2(32f, -16f);
                            break;
                        default:
                            columnOffset = new JxqyFloat2(32f, -16f);
                            rowOffset = new JxqyFloat2(32f, 16f);
                            break;
                    }
                    for (int row = 0; row < count; row++)
                    {
                        rowMiddle += rowOffset;
                        SpawnFixedLine(
                            source,
                            magic,
                            rowMiddle,
                            columnOffset,
                            columnCount,
                            row * 0.06f);
                    }
                    break;
                }
                case 0:
                case 4:
                {
                    JxqyFloat2 rowOffset = directionIndex == 0
                        ? new JxqyFloat2(0f, 32f)
                        : new JxqyFloat2(0f, -32f);
                    for (int row = 0; row < count; row++)
                    {
                        rowMiddle += rowOffset;
                        SpawnHorizontalFixedLine(
                            source,
                            magic,
                            rowMiddle,
                            columnCount,
                            row * 0.06f);
                    }
                    break;
                }
                case 2:
                case 6:
                {
                    var columnOffset = new JxqyFloat2(0f, 32f);
                    for (int row = 0; row < count; row++)
                    {
                        rowMiddle += directionIndex == 2
                            ? row % 2 == 0
                                ? new JxqyFloat2(-32f, -16f)
                                : new JxqyFloat2(-32f, 16f)
                            : row % 2 == 0
                                ? new JxqyFloat2(32f, 16f)
                                : new JxqyFloat2(32f, -16f);
                        SpawnFixedLine(
                            source,
                            magic,
                            rowMiddle,
                            columnOffset,
                            columnCount,
                            row * 0.06f);
                    }
                    break;
                }
            }
        }

        private void SpawnTriangleRegion(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 direction,
            int count)
        {
            JxqyFloat2[] rowOffsets =
            {
                new JxqyFloat2(0f, 32f),
                new JxqyFloat2(-32f, 16f),
                new JxqyFloat2(-64f, 0f),
                new JxqyFloat2(-32f, -16f),
                new JxqyFloat2(0f, -32f),
                new JxqyFloat2(32f, -16f),
                new JxqyFloat2(64f, 0f),
                new JxqyFloat2(32f, 16f),
            };
            JxqyFloat2[] columnOffsets =
            {
                new JxqyFloat2(64f, 0f),
                new JxqyFloat2(-32f, -16f),
                new JxqyFloat2(0f, 32f),
                new JxqyFloat2(-32f, 16f),
                new JxqyFloat2(64f, 0f),
                new JxqyFloat2(32f, 16f),
                new JxqyFloat2(0f, 32f),
                new JxqyFloat2(32f, -16f),
            };
            int directionIndex = JxqyDirection.GetIndex(direction, 8);
            JxqyFloat2 rowMiddle = origin;
            for (int row = 0; row < count; row++)
            {
                rowMiddle += rowOffsets[directionIndex];
                SpawnFixedLine(
                    source,
                    magic,
                    rowMiddle,
                    columnOffsets[directionIndex],
                    1 + row * 2,
                    row * 0.06f);
            }
        }

        private void SpawnHorizontalFixedLine(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 middle,
            int count,
            float delay)
        {
            SpawnFixedProjectile(source, magic, middle, delay);
            JxqyFloat2 left = middle;
            JxqyFloat2 right = middle;
            for (int index = 0; index < count / 2; index++)
            {
                JxqyFloat2 leftOffset = index % 2 == 0
                    ? new JxqyFloat2(-32f, -16f)
                    : new JxqyFloat2(-32f, 16f);
                JxqyFloat2 rightOffset = index % 2 == 0
                    ? new JxqyFloat2(32f, -16f)
                    : new JxqyFloat2(32f, 16f);
                left += leftOffset;
                right += rightOffset;
                SpawnFixedProjectile(source, magic, left, delay);
                SpawnFixedProjectile(source, magic, right, delay);
            }
        }

        private void SpawnFixedLine(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 middle,
            JxqyFloat2 offset,
            int count,
            float delay)
        {
            SpawnFixedLine(
                source,
                magic,
                middle,
                offset,
                count,
                delay,
                JxqyFloat2.Zero);
        }

        private void SpawnFixedLine(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 middle,
            JxqyFloat2 offset,
            int count,
            float delay,
            JxqyFloat2 presentationDirection)
        {
            SpawnFixedProjectile(
                source,
                magic,
                middle,
                delay,
                presentationDirection);
            int sideCount = (count - 1) / 2;
            for (int index = 1; index <= sideCount; index++)
            {
                SpawnFixedProjectile(
                    source,
                    magic,
                    middle + offset * index,
                    delay,
                    presentationDirection);
                SpawnFixedProjectile(
                    source,
                    magic,
                    middle - offset * index,
                    delay,
                    presentationDirection);
            }
        }

        private void SpawnFixedProjectile(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 position,
            float delay)
        {
            SpawnFixedProjectile(
                source,
                magic,
                position,
                delay,
                JxqyFloat2.Zero);
        }

        private void SpawnFixedProjectile(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 position,
            float delay,
            JxqyFloat2 presentationDirection)
        {
            JxqyMagicProjectile projectile = AcquireProjectile(
                source,
                magic,
                position,
                position,
                null,
                0,
                delay);
            projectile.SetPresentationDirection(presentationDirection);
            _projectiles.Add(projectile);
            ProjectileSpawned?.Invoke(projectile);
        }

        private JxqyMagicProjectile SpawnProjectile(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            JxqyFloat2 origin,
            JxqyFloat2 destination,
            JxqyCharacter target,
            float speed,
            float delaySeconds)
        {
            JxqyMagicProjectile projectile = AcquireProjectile(
                source,
                magic,
                origin,
                destination,
                target,
                speed,
                delaySeconds);
            if (magic.MoveKind != 17)
                projectile.ApplyLegacyInitialAdvance();
            _projectiles.Add(projectile);
            ProjectileSpawned?.Invoke(projectile);
            if (speed <= 0 && target != null)
                ResolveProjectile(projectile);
            return projectile;
        }

        private static JxqyCharacter FindCollisionTarget(
            JxqyMagicProjectile projectile,
            IReadOnlyList<JxqyCharacter> targets,
            JxqyFloat2 movementOrigin,
            JxqyFloat2 movementDestination)
        {
            // Original MagicSprite.CollisionDetaction resolves contact by
            // exact TilePosition equality. Sweep the Unity-frame segment so
            // a variable delta time cannot skip a legacy map tile entirely.
            if (targets == null)
                return null;
            JxqyCharacter closest = null;
            float closestEntry = float.PositiveInfinity;
            for (int index = 0; index < targets.Count; index++)
            {
                JxqyCharacter target = targets[index];
                if (target == null || target.IsDead ||
                    !JxqyRelations.AreOpposed(
                        projectile.Source,
                        target))
                {
                    continue;
                }
                if (TryGetLegacyTileEntryFraction(
                        movementOrigin,
                        movementDestination,
                        target.TilePosition,
                        out float entry) &&
                    entry < closestEntry)
                {
                    closest = target;
                    closestEntry = entry;
                }
            }
            return closest;
        }

        private static bool TryGetLegacyTileEntryFraction(
            JxqyFloat2 movementOrigin,
            JxqyFloat2 movementDestination,
            JxqyIntPoint tile,
            out float entry)
        {
            JxqyIntPoint center =
                JxqyIsometricMapMath.TileToWorldPixel(
                    tile.X,
                    tile.Y,
                    boundCheck: false);
            float startX =
                (movementOrigin.X - center.X) /
                JxqyIsometricMapMath.HalfTileWidth;
            float startY =
                (movementOrigin.Y - center.Y) /
                JxqyIsometricMapMath.HalfTileHeight;
            float deltaX =
                (movementDestination.X - movementOrigin.X) /
                JxqyIsometricMapMath.HalfTileWidth;
            float deltaY =
                (movementDestination.Y - movementOrigin.Y) /
                JxqyIsometricMapMath.HalfTileHeight;
            float enter = 0f;
            float exit = 1f;
            if (!ClipLegacyTileHalfPlane(
                    startX + startY,
                    deltaX + deltaY,
                    ref enter,
                    ref exit) ||
                !ClipLegacyTileHalfPlane(
                    startX - startY,
                    deltaX - deltaY,
                    ref enter,
                    ref exit) ||
                !ClipLegacyTileHalfPlane(
                    -startX + startY,
                    -deltaX + deltaY,
                    ref enter,
                    ref exit) ||
                !ClipLegacyTileHalfPlane(
                    -startX - startY,
                    -deltaX - deltaY,
                    ref enter,
                    ref exit))
            {
                entry = 0f;
                return false;
            }
            float middle = (enter + exit) * 0.5f;
            JxqyFloat2 sample = movementOrigin +
                                (movementDestination - movementOrigin) *
                                middle;
            if (!JxqyIsometricMapMath.WorldPixelToTile(
                    (int)sample.X,
                    (int)sample.Y,
                    boundCheck: false).Equals(tile))
            {
                entry = 0f;
                return false;
            }
            entry = enter;
            return enter <= exit;
        }

        private static bool ClipLegacyTileHalfPlane(
            float startValue,
            float deltaValue,
            ref float enter,
            ref float exit)
        {
            const float epsilon = 0.000001f;
            float remaining = 1f - startValue;
            if (Math.Abs(deltaValue) <= epsilon)
                return remaining >= -epsilon;
            float crossing = remaining / deltaValue;
            if (deltaValue > 0f)
                exit = Math.Min(exit, crossing);
            else
                enter = Math.Max(enter, crossing);
            return enter <= exit + epsilon;
        }

        private static JxqyFloat2 DirectionFromIndex(int direction)
        {
            return Direction32(((direction % 8 + 8) % 8) * 4);
        }

        private static JxqyFloat2 Direction32(int direction)
        {
            double angle = Math.PI * 2.0 *
                           ((direction % 32 + 32) % 32) / 32.0;
            return new JxqyFloat2(
                (float)-Math.Sin(angle),
                (float)Math.Cos(angle));
        }

        private static float GetLegacyProjectileSpeed(
            JxqyMagicDefinition magic,
            JxqyFloat2 direction)
        {
            if (direction == JxqyFloat2.Zero)
                return magic.ProjectileSpeed;
            return magic.ProjectileSpeed *
                   (1f - 0.5f * Math.Abs(direction.Normalized.Y));
        }

        private static void UpdateFollowEnemyTarget(
            JxqyMagicProjectile projectile,
            IReadOnlyList<JxqyCharacter> collisionTargets)
        {
            if (projectile.Magic.MoveKind != 16)
                return;
            if (projectile.Target != null &&
                (projectile.Target.IsDead ||
                 !JxqyRelations.AreOpposed(
                     projectile.Source,
                     projectile.Target)))
            {
                projectile.Target = null;
            }
            // Original FollowEnemy magic first travels 200 world pixels,
            // then acquires the closest live opponent. It never binds to the
            // character merely selected when the spell was cast.
            if (projectile.Target != null ||
                projectile.MovedDistance <= 200f ||
                collisionTargets == null)
            {
                return;
            }
            float closestDistance = float.MaxValue;
            for (int index = 0; index < collisionTargets.Count; index++)
            {
                JxqyCharacter candidate = collisionTargets[index];
                if (candidate == null || candidate.IsDead ||
                    !JxqyRelations.AreOpposed(
                        projectile.Source,
                        candidate))
                {
                    continue;
                }
                float distance = JxqyFloat2.Distance(
                    projectile.Position,
                    candidate.PositionInWorld);
                if (distance >= closestDistance)
                    continue;
                closestDistance = distance;
                projectile.Target = candidate;
            }
        }

        private static float GetProjectileStep(
            JxqyMagicProjectile projectile,
            float elapsedSeconds)
        {
            if (projectile.Magic.MoveKind != 16)
                return projectile.Speed * elapsedSeconds;
            JxqyFloat2 direction = projectile.Target == null
                ? projectile.Direction
                : projectile.Target.PositionInWorld - projectile.Position;
            return GetLegacyProjectileSpeed(
                       projectile.Magic,
                       direction) * elapsedSeconds;
        }

        private static void MoveProjectileTo(
            JxqyMagicProjectile projectile,
            JxqyFloat2 position)
        {
            projectile.MovedDistance += JxqyFloat2.Distance(
                projectile.Position,
                position);
            projectile.Position = position;
        }

        private static JxqyFloat2 GetLegacyWallOffset(
            int directionIndex)
        {
            switch ((directionIndex % 8 + 8) % 8)
            {
                case 0:
                case 4:
                    return new JxqyFloat2(64f, 0f);
                case 2:
                case 6:
                    return new JxqyFloat2(0f, 32f);
                case 1:
                case 5:
                    return new JxqyFloat2(32f, 16f);
                default:
                    return new JxqyFloat2(-32f, 16f);
            }
        }

        private void ReleaseProjectile(JxqyMagicProjectile projectile)
        {
            projectile.Clear();
            if (_projectilePool.Count < ProjectilePoolCapacity)
                _projectilePool.Push(projectile);
        }

        private void ResolveProjectile(
            JxqyMagicProjectile projectile,
            JxqyCharacter collisionTarget = null)
        {
            if (projectile.IsComplete)
                return;
            projectile.IsComplete = true;
            try
            {
                JxqyMagicDefinition magic = projectile.Magic;
                if (magic.MoveKind == 13)
                {
                    ResolveFollowCharacterMagic(projectile, magic);
                    return;
                }
                JxqyCharacter target =
                    collisionTarget ?? projectile.Target;
                ApplyMagicToTarget(projectile, target, magic);
            }
            finally
            {
                ProjectileResolved?.Invoke(projectile);
            }
        }

        private void ResolveProjectileImpact(
            JxqyMagicProjectile projectile)
        {
            if (projectile.IsComplete)
                return;
            projectile.WasBlockedByWall = true;
            projectile.IsComplete = true;
            ProjectileResolved?.Invoke(projectile);
        }

        private void ResolveSuperModeProjectile(
            JxqyMagicProjectile projectile,
            IReadOnlyList<JxqyCharacter> collisionTargets)
        {
            if (projectile.IsComplete)
                return;
            projectile.IsComplete = true;
            try
            {
                if (collisionTargets == null)
                    return;
                foreach (JxqyCharacter target in collisionTargets)
                {
                    if (target is JxqyNpc npc && !npc.IsVisible)
                        continue;
                    ApplyMagicToTarget(
                        projectile,
                        target,
                        projectile.Magic);
                }
            }
            finally
            {
                ProjectileResolved?.Invoke(projectile);
            }
        }

        private void ApplyMagicToTarget(
            JxqyMagicProjectile projectile,
            JxqyCharacter target,
            JxqyMagicDefinition magic)
        {
            if (target == null || target.IsDead ||
                !JxqyRelations.AreOpposed(projectile.Source, target))
                return;
            ApplyLegacyContactStatus(
                projectile.Source,
                target,
                JxqyStatusKind.Frozen,
                magic.FrozenSeconds,
                magic.NoSpecialKindEffect == 0);
            ApplyLegacyContactStatus(
                projectile.Source,
                target,
                JxqyStatusKind.Petrified,
                magic.PetrifiedSeconds,
                magic.NoSpecialKindEffect == 0);
            ApplyLegacyContactStatus(
                projectile.Source,
                target,
                JxqyStatusKind.Poisoned,
                magic.PoisonSeconds,
                magic.NoSpecialKindEffect == 0);
            ApplyAdditionalAttackEffect(
                projectile.Source,
                target,
                magic.AdditionalEffect,
                magic.NoSpecialKindEffect == 0);
            ApplyStatus(
                target,
                JxqyStatusKind.MovementDisabled,
                magic.DisableMoveSeconds);
            ApplyStatus(
                target,
                JxqyStatusKind.SkillDisabled,
                magic.DisableSkillSeconds);
            JxqyDamageResult result = JxqyDamageCalculator.Resolve(
                projectile.Source,
                target,
                JxqyDamageCalculator.GetMagicEffectAmount(
                    projectile.Source,
                    magic,
                    1),
                JxqyDamageCalculator.GetMagicEffectAmount(
                    projectile.Source,
                    magic,
                    2),
                JxqyDamageCalculator.GetMagicEffectAmount(
                    projectile.Source,
                    magic,
                    3),
                magic.EffectMana,
                _random);
            MagicContacted?.Invoke(projectile, target, result);
            if (!result.Hit)
                return;
            ApplyRestore(projectile.Source, magic, result.LifeDamage);
            TriggerBeAttackedMagic(projectile, target);
        }

        private static void ApplyAdditionalAttackEffect(
            JxqyCharacter source,
            JxqyCharacter target,
            JxqyMagicAdditionalEffect effect,
            bool hasVisualEffect)
        {
            if (source == null || target == null ||
                effect == JxqyMagicAdditionalEffect.None)
            {
                return;
            }
            float seconds = Math.Max(0, source.Level) / 10 + 1;
            switch (effect)
            {
                case JxqyMagicAdditionalEffect.Frozen:
                    ApplyLegacyContactStatus(
                        source,
                        target,
                        JxqyStatusKind.Frozen,
                        seconds,
                        hasVisualEffect);
                    break;
                case JxqyMagicAdditionalEffect.Poisoned:
                    ApplyLegacyContactStatus(
                        source,
                        target,
                        JxqyStatusKind.Poisoned,
                        seconds,
                        hasVisualEffect);
                    break;
                case JxqyMagicAdditionalEffect.Petrified:
                    ApplyLegacyContactStatus(
                        source,
                        target,
                        JxqyStatusKind.Petrified,
                        seconds,
                        hasVisualEffect);
                    break;
            }
        }

        private static void ApplyLegacyContactStatus(
            JxqyCharacter source,
            JxqyCharacter target,
            JxqyStatusKind kind,
            float seconds,
            bool hasVisualEffect = true)
        {
            if (target == null || seconds <= 0 || target.HasStatus(kind))
                return;
            switch (kind)
            {
                case JxqyStatusKind.Frozen:
                case JxqyStatusKind.Poisoned:
                    if (target.HasStatus(JxqyStatusKind.Petrified))
                        return;
                    break;
                case JxqyStatusKind.Petrified:
                    target.ClearStatus(JxqyStatusKind.Frozen);
                    break;
            }
            target.ApplyStatus(kind, seconds, hasVisualEffect);
            if (kind == JxqyStatusKind.Poisoned &&
                JxqyExperienceRules.CanOwnPoisonExperience(source))
            {
                target.SetPoisonExperienceOwner(source.Name);
            }
        }

        private void ResolveFollowCharacterMagic(
            JxqyMagicProjectile projectile,
            JxqyMagicDefinition magic)
        {
            JxqyCharacter source = projectile.Source;
            JxqyCharacter target = JxqyMagicTargeting.ResolveBodyEffectTarget(
                source,
                projectile.Target);
            int amount = JxqyDamageCalculator.GetMagicEffectAmount(
                source,
                magic,
                1);
            switch (magic.SpecialKind)
            {
                case 1:
                    int lifeBefore = target.Life;
                    target.AddLife(amount);
                    int appliedHealing = target.Life - lifeBefore;
                    if (appliedHealing > 0)
                    {
                        MagicHealed?.Invoke(
                            source,
                            target,
                            magic,
                            appliedHealing);
                    }
                    break;
                case 2:
                    target.Thew += amount;
                    break;
                case 3:
                case 6:
                    target.AddOrRefreshMagicEffect(
                        magic,
                        magic.KeepMilliseconds > 0
                            ? magic.KeepMilliseconds / 1000f
                            : magic.LifeSeconds);
                    break;
                case 8:
                    target.ClearStatus(JxqyStatusKind.Frozen);
                    target.ClearStatus(JxqyStatusKind.Petrified);
                    target.ClearStatus(JxqyStatusKind.Poisoned);
                    target.ClearStatus(JxqyStatusKind.MovementDisabled);
                    target.ClearStatus(JxqyStatusKind.SkillDisabled);
                    break;
            }
        }

        private void ApplyRestore(
            JxqyCharacter source,
            JxqyMagicDefinition magic,
            int lifeDamage)
        {
            if (source == null ||
                lifeDamage <= 0 ||
                magic.RestoreProbability <= 0 ||
                _random.Next(0, 100) >= magic.RestoreProbability)
            {
                return;
            }
            int amount =
                lifeDamage * magic.RestorePercent / 100;
            switch (magic.RestoreType)
            {
                case 1:
                    source.Mana += amount;
                    break;
                case 2:
                    source.Thew += amount;
                    break;
                default:
                    source.AddLife(amount);
                    break;
            }
        }

        private void TriggerBeAttackedMagic(
            JxqyMagicProjectile projectile,
            JxqyCharacter target)
        {
            JxqyMagicDefinition retaliation =
                target.MagicToUseWhenBeAttacked;
            if (retaliation == null || target.IsDead)
                return;
            JxqyFloat2 destination;
            JxqyCharacter retaliationTarget = null;
            switch (target.MagicDirectionWhenBeAttacked)
            {
                case 1:
                    destination = target.PositionInWorld -
                                  projectile.Direction;
                    break;
                case 2:
                    destination = target.PositionInWorld +
                                  DirectionFromIndex(
                                      target.CurrentDirection);
                    break;
                default:
                    destination = projectile.Source.PositionInWorld;
                    retaliationTarget = projectile.Source;
                    break;
            }
            UseMagic(
                target,
                retaliation,
                destination,
                retaliationTarget);
        }

        private static void ApplyStatus(
            JxqyCharacter target,
            JxqyStatusKind kind,
            float seconds)
        {
            if (seconds > 0)
                target.ApplyStatus(kind, seconds);
        }
    }

    public sealed class JxqyAutoAttackController
    {
        private float _cooldownRemaining;
        private float _pursuitCooldownRemaining;

        public float IntervalSeconds { get; set; }
        public float PursuitIntervalSeconds { get; set; } = 0.1f;
        public float Range { get; set; } = 48f;
        public int MaximumTileDistance { get; set; }
        public JxqyCharacter Target { get; set; }

        public bool TryRequestPursuit(
            JxqyCharacter attacker,
            float elapsedSeconds)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));
            _pursuitCooldownRemaining = Math.Max(
                0,
                _pursuitCooldownRemaining - elapsedSeconds);
            if (_pursuitCooldownRemaining > 0 || attacker.IsDead ||
                attacker.HasPath || !attacker.CanPerformAction ||
                Target == null || Target.IsDead ||
                !JxqyRelations.AreOpposed(attacker, Target) ||
                IsTargetInRange(attacker))
            {
                return false;
            }
            _pursuitCooldownRemaining = Math.Max(
                0,
                PursuitIntervalSeconds);
            return true;
        }

        public bool Tick(
            JxqyCharacter attacker,
            float elapsedSeconds,
            Func<JxqyCharacter, JxqyCharacter, bool> attack)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));
            if (attack == null)
                throw new ArgumentNullException(nameof(attack));
            _cooldownRemaining = Math.Max(0, _cooldownRemaining - elapsedSeconds);
            // Locomotion owns the character until its current route ends.
            // Otherwise a retained target can start another attack and erase
            // a new click-to-move route on the following combat tick.
            if (_cooldownRemaining > 0 || attacker.IsDead ||
                attacker.HasPath || !attacker.CanPerformAction ||
                Target == null || Target.IsDead ||
                !JxqyRelations.AreOpposed(attacker, Target) ||
                !IsTargetInRange(attacker))
                return false;
            attacker.SetState(JxqyCharacterState.Attack);
            // An attack attempt still owns the action/cooldown when its hit
            // roll misses. Returning false here used to leave the attacker in
            // Attack forever because the runtime only advanced the action
            // state after a successful damage result.
            attack(attacker, Target);
            _cooldownRemaining = Math.Max(0, IntervalSeconds);
            return true;
        }

        private bool IsTargetInRange(JxqyCharacter attacker)
        {
            return MaximumTileDistance > 0
                ? Target != null &&
                  JxqyPathfinder.GetViewTileDistance(
                      attacker.TilePosition,
                      Target.TilePosition) <= MaximumTileDistance
                : Target != null &&
                  JxqyFloat2.Distance(
                      attacker.PositionInWorld,
                      Target.PositionInWorld) <= Range;
        }
    }
}
