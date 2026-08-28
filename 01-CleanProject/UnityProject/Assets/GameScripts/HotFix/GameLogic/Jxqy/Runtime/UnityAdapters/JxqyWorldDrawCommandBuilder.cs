using System;
using System.Collections.Generic;
using Jxqy.Domain.Animation;
using Jxqy.Domain.Simulation;
using Jxqy.Domain.World;
using Jxqy.Ports;
using UnityEngine;

namespace Jxqy.UnityAdapters
{
    public static class JxqyMagicOcclusionOutlinePolicy
    {
        private static readonly HashSet<string> EnabledMagicIds =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "magic002_南岳支天.ini",
            };

        public static bool IsEnabled(string magicId)
        {
            return !string.IsNullOrWhiteSpace(magicId) &&
                   EnabledMagicIds.Contains(magicId);
        }
    }

    public static class JxqyCharacterStatusPresentation
    {
        public static Color ResolveColor(
            JxqyCharacter character,
            Color baseColor)
        {
            if (character == null)
                return baseColor;
            Color color = baseColor;
            if (character.HasStatus(JxqyStatusKind.Frozen) &&
                character.IsFrozenVisualEffect)
            {
                color *= new Color(
                    80f / 255f,
                    80f / 255f,
                    1f,
                    1f);
            }
            if (character.HasStatus(JxqyStatusKind.Poisoned) &&
                character.IsPoisonVisualEffect)
            {
                color *= new Color(
                    50f / 255f,
                    1f,
                    50f / 255f,
                    1f);
            }
            return color;
        }

        public static string ResolveMaterialKey(JxqyCharacter character)
        {
            return character != null &&
                   character.HasStatus(JxqyStatusKind.Petrified) &&
                   character.IsPetrifiedVisualEffect
                ? "grayscale"
                : "default";
        }

        public static bool HasSpecialDeathVisual(
            JxqyCharacter character)
        {
            return character != null && character.IsDead &&
                   (character.HasStatus(JxqyStatusKind.Frozen) &&
                    character.IsFrozenVisualEffect ||
                    character.HasStatus(JxqyStatusKind.Poisoned) &&
                    character.IsPoisonVisualEffect ||
                    character.HasStatus(JxqyStatusKind.Petrified) &&
                    character.IsPetrifiedVisualEffect);
        }
    }

    public static class JxqyCharacterOcclusionPolicy
    {
        public const int MaximumTargetCount = 4;
        public const int MagicStencilBit = 2;

        public static int GetTargetStencilBit(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= MaximumTargetCount)
                throw new ArgumentOutOfRangeException(nameof(targetIndex));
            return targetIndex == 0 ? 1 : 1 << (targetIndex + 1);
        }

        public static int ResolveAllTargetMask(
            int playerTileRow,
            IReadOnlyList<int> targetRows)
        {
            if (targetRows == null || targetRows.Count == 0)
                return 1;
            int mask = 0;
            int count = Math.Min(targetRows.Count, MaximumTargetCount);
            for (int index = 0; index < count; index++)
                mask |= GetTargetStencilBit(index);
            return mask;
        }

        public static int ResolveOccluderMask(
            int occluderTileRow,
            int playerTileRow,
            IReadOnlyList<int> targetRows,
            bool inclusive)
        {
            if (targetRows == null || targetRows.Count == 0)
            {
                if (playerTileRow == int.MaxValue)
                    return 0;
                return inclusive
                    ? occluderTileRow >= playerTileRow ? 1 : 0
                    : occluderTileRow > playerTileRow ? 1 : 0;
            }
            int mask = 0;
            int count = Math.Min(targetRows.Count, MaximumTargetCount);
            for (int index = 0; index < count; index++)
            {
                bool occludes = inclusive
                    ? occluderTileRow >= targetRows[index]
                    : occluderTileRow > targetRows[index];
                if (occludes)
                    mask |= GetTargetStencilBit(index);
            }
            return mask;
        }
    }

    public static class JxqyWorldDepth
    {
        public const int BodyObjectBase = 1_000_000;
        public const int RowInterleavedBase = 2_000_000;
        public const int UpperMapBase = 3_000_000;
        public const int FlyingNpcBase = 4_000_000;
        public const int PlayerBase = 4_500_000;
        public const int MagicOccludedOutlineBase = 4_800_000;
        public const int PointerOutlineBase = 4_900_000;
    }

    public enum JxqyWorldVisualKind
    {
        Npc = 0,
        Object = 1,
        Magic = 2,
        Projectile = 3,
        BodyObject = 4,
        FlyingNpc = 5,
        CharacterEffect = 6,
        Player = 7
    }

    public sealed class JxqyWorldVisual
    {
        public string Id = string.Empty;
        public JxqyWorldVisualKind Kind;
        public int TileColumn;
        public int TileRow;
        public Vector2 WorldPosition;
        public Color Color = Color.white;
        public Color OutlineColor = Color.clear;
        public string MaterialKey = "default";
        public int CharacterOcclusionStencilBit;
        public bool UsesMagicOcclusionOutline;
        public bool IsVisible = true;
        public JxqyAnimationPlayer Animation;
    }

    public sealed class JxqyWorldDrawCommandBuilder
    {
        private readonly int _mapColumns;

        public JxqyWorldDrawCommandBuilder(int mapColumns)
        {
            if (mapColumns <= 0)
                throw new ArgumentOutOfRangeException(nameof(mapColumns));
            _mapColumns = mapColumns;
        }

        public List<JxqyDrawCommand> Build(
            IEnumerable<JxqyWorldVisual> visuals,
            JxqyIntRect camera,
            int playerTileRow = int.MaxValue,
            IReadOnlyList<int> occlusionTargetRows = null)
        {
            var result = new List<JxqyDrawCommand>();
            Build(
                visuals,
                camera,
                result,
                playerTileRow,
                occlusionTargetRows);
            return result;
        }

        public void Build(
            IEnumerable<JxqyWorldVisual> visuals,
            JxqyIntRect camera,
            List<JxqyDrawCommand> result,
            int playerTileRow = int.MaxValue,
            IReadOnlyList<int> occlusionTargetRows = null)
        {
            if (visuals == null)
                throw new ArgumentNullException(nameof(visuals));
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            result.Clear();
            foreach (JxqyWorldVisual visual in visuals)
            {
                if (visual == null || !visual.IsVisible ||
                    visual.Animation == null)
                    continue;
                JxqyAnimationPose pose =
                    visual.Animation.GetPose();
                if (visual.CharacterOcclusionStencilBit != 0)
                {
                    bool grayscale = string.Equals(
                        visual.MaterialKey,
                        "grayscale",
                        StringComparison.Ordinal);
                    AddCommand(
                        result,
                        visual,
                        pose,
                        ResolveCharacterDepth(visual, playerTileRow),
                        grayscale
                            ? "player-opaque-grayscale"
                            : "player-opaque",
                        stencilMask:
                            visual.CharacterOcclusionStencilBit);
                    AddCommand(
                        result,
                        visual,
                        pose,
                        ResolveCharacterDepth(visual, playerTileRow) + 1,
                        grayscale
                            ? "player-occluded-grayscale"
                            : "player-occluded",
                        stencilMask:
                            visual.CharacterOcclusionStencilBit);
                    continue;
                }
                int occlusionMask = ResolveOccluderMask(
                    visual,
                    playerTileRow,
                    occlusionTargetRows);
                AddCommand(
                    result,
                    visual,
                    pose,
                    CalculateDepth(visual),
                    occlusionMask != 0
                        ? "occluder"
                        : visual.MaterialKey,
                    stencilMask: occlusionMask);
                AddMagicStencilClearCommand(result, visual, pose);
                AddMagicOccludedOutlineCommand(
                    result,
                    visual,
                    pose);
                if (visual.OutlineColor.a > 0f)
                {
                    AddCommand(
                        result,
                        visual,
                        pose,
                        JxqyWorldDepth.PointerOutlineBase,
                        "outedge",
                        visual.OutlineColor);
                }
            }
        }

        private void AddMagicStencilClearCommand(
            ICollection<JxqyDrawCommand> result,
            JxqyWorldVisual visual,
            JxqyAnimationPose pose)
        {
            if (!IsMagicPresentation(visual))
                return;
            AddCommand(
                result,
                visual,
                pose,
                CalculateDepth(visual) + 1,
                "magic-stencil-clear",
                Color.white);
        }

        private void AddMagicOccludedOutlineCommand(
            ICollection<JxqyDrawCommand> result,
            JxqyWorldVisual visual,
            JxqyAnimationPose pose)
        {
            if (!IsMagicPresentation(visual))
                return;
            int tileKey = visual.TileRow * _mapColumns +
                          visual.TileColumn;
            AddCommand(
                result,
                visual,
                pose,
                JxqyWorldDepth.MagicOccludedOutlineBase + tileKey,
                "magic-occluded-outline",
                Color.white);
        }

        private static bool IsMagicPresentation(JxqyWorldVisual visual)
        {
            return visual.UsesMagicOcclusionOutline &&
                   (visual.Kind == JxqyWorldVisualKind.Magic ||
                    visual.Kind == JxqyWorldVisualKind.Projectile ||
                    visual.Kind == JxqyWorldVisualKind.CharacterEffect);
        }

        private static void AddCommand(
            ICollection<JxqyDrawCommand> result,
            JxqyWorldVisual visual,
            JxqyAnimationPose pose,
            int depth,
            string materialKey,
            Color? commandColor = null,
            int stencilMask = 0)
        {
            result.Add(new JxqyDrawCommand(
                    pose.AtlasAddress,
                    new Rect(
                        pose.AtlasX,
                        pose.AtlasY,
                        pose.Width,
                        pose.Height),
                    visual.WorldPosition,
                    new Vector2(pose.AnchorX, pose.AnchorY),
                    commandColor ?? visual.Color,
                    depth,
                    materialKey,
                    stencilMask));
        }

        private static int ResolveOccluderMask(
            JxqyWorldVisual visual,
            int playerTileRow,
            IReadOnlyList<int> occlusionTargetRows)
        {
            if (!string.Equals(
                    visual.MaterialKey,
                    "default",
                    StringComparison.Ordinal))
            {
                return 0;
            }
            return visual.Kind switch
            {
                JxqyWorldVisualKind.Npc =>
                    JxqyCharacterOcclusionPolicy.ResolveOccluderMask(
                        visual.TileRow,
                        playerTileRow,
                        occlusionTargetRows,
                        inclusive: false),
                JxqyWorldVisualKind.Magic =>
                    JxqyCharacterOcclusionPolicy.ResolveOccluderMask(
                        visual.TileRow,
                        playerTileRow,
                        occlusionTargetRows,
                        inclusive: true),
                JxqyWorldVisualKind.Projectile =>
                    JxqyCharacterOcclusionPolicy.ResolveOccluderMask(
                        visual.TileRow,
                        playerTileRow,
                        occlusionTargetRows,
                        inclusive: true),
                JxqyWorldVisualKind.CharacterEffect =>
                    JxqyCharacterOcclusionPolicy.ResolveOccluderMask(
                        visual.TileRow,
                        playerTileRow,
                        occlusionTargetRows,
                        inclusive: true),
                _ => 0
            };
        }

        private static int ResolveCharacterDepth(
            JxqyWorldVisual visual,
            int playerTileRow)
        {
            if (visual.Kind == JxqyWorldVisualKind.Player ||
                playerTileRow == int.MaxValue)
            {
                return JxqyWorldDepth.PlayerBase;
            }
            return JxqyWorldDepth.PlayerBase +
                   (visual.TileRow - playerTileRow) * 2;
        }

        private int CalculateDepth(JxqyWorldVisual visual)
        {
            int tileOrder =
                visual.TileRow * _mapColumns +
                visual.TileColumn;
            return visual.Kind switch
            {
                JxqyWorldVisualKind.BodyObject =>
                    JxqyWorldDepth.BodyObjectBase + tileOrder,
                JxqyWorldVisualKind.FlyingNpc =>
                    JxqyWorldDepth.FlyingNpcBase + tileOrder,
                JxqyWorldVisualKind.Npc =>
                    JxqyWorldDepth.RowInterleavedBase +
                    tileOrder * 10 + 1,
                JxqyWorldVisualKind.Object =>
                    JxqyWorldDepth.RowInterleavedBase +
                    tileOrder * 10 + 2,
                JxqyWorldVisualKind.Magic =>
                    JxqyWorldDepth.RowInterleavedBase +
                    tileOrder * 10 + 3,
                JxqyWorldVisualKind.Projectile =>
                    JxqyWorldDepth.RowInterleavedBase +
                    tileOrder * 10 + 3,
                JxqyWorldVisualKind.CharacterEffect =>
                    JxqyWorldDepth.RowInterleavedBase +
                    tileOrder * 10 + 4,
                _ => JxqyWorldDepth.RowInterleavedBase +
                     tileOrder * 10 + 5
            };
        }
    }
}
