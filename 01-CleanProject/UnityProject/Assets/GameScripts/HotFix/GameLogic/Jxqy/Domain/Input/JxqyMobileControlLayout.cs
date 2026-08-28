using System;
using System.Collections.Generic;
using System.Linq;
using Jxqy.Domain.Presentation;
using Jxqy.Domain.World;

namespace Jxqy.Domain.Input
{
    public enum JxqyMobileControlKind
    {
        Movement,
        Attack,
        Skill1,
        Skill2,
        Skill3,
        Item,
        Interact,
        Menu,
        Confirm,
        Cancel,
    }

    [Serializable]
    public readonly struct JxqyMobileControlPlacement
    {
        public JxqyMobileControlPlacement(
            JxqyMobileControlKind kind,
            JxqyFloat2 center,
            JxqyFloat2 size,
            float opacity)
        {
            if (size.X <= 0 || size.Y <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            Kind = kind;
            Center = center;
            Size = size;
            Opacity = Math.Max(0, Math.Min(1, opacity));
        }

        public JxqyMobileControlKind Kind { get; }
        public JxqyFloat2 Center { get; }
        public JxqyFloat2 Size { get; }
        public float Opacity { get; }

        public bool IsInsideLogicalViewport()
        {
            float halfWidth = Size.X * 0.5f;
            float halfHeight = Size.Y * 0.5f;
            return Center.X - halfWidth >= 0 &&
                   Center.X + halfWidth <=
                   JxqyLogicalViewport.OriginalWidth &&
                   Center.Y - halfHeight >= 0 &&
                   Center.Y + halfHeight <=
                   JxqyLogicalViewport.OriginalHeight;
        }
    }

    public sealed class JxqyMobileControlLayout
    {
        private const float LegacyDesignScale = 0.8f;

        private static readonly JxqyMobileControlKind[] RequiredKinds =
        {
            JxqyMobileControlKind.Movement,
            JxqyMobileControlKind.Attack,
            JxqyMobileControlKind.Skill1,
            JxqyMobileControlKind.Skill2,
            JxqyMobileControlKind.Skill3,
            JxqyMobileControlKind.Item,
            JxqyMobileControlKind.Interact,
        };

        private readonly IReadOnlyDictionary<
            JxqyMobileControlKind,
            JxqyMobileControlPlacement> _placements;
        private readonly IReadOnlyCollection<
            JxqyMobileControlPlacement> _placementValues;

        public JxqyMobileControlLayout(
            IEnumerable<JxqyMobileControlPlacement> placements)
        {
            if (placements == null)
                throw new ArgumentNullException(nameof(placements));
            JxqyMobileControlPlacement[] values = placements.ToArray();
            if (values.Any(value => !value.IsInsideLogicalViewport()))
                throw new ArgumentException(
                    "A mobile control extends outside the logical viewport.",
                    nameof(placements));
            _placements = values.ToDictionary(
                value => value.Kind,
                value => value);
            _placementValues = values;
            if (RequiredKinds.Any(kind => !_placements.ContainsKey(kind)))
                throw new ArgumentException(
                    "The mobile control layout is missing a mainline action.",
                    nameof(placements));
        }

        public IReadOnlyCollection<JxqyMobileControlPlacement> Placements =>
            _placementValues;

        public JxqyMobileControlPlacement this[JxqyMobileControlKind kind] =>
            _placements[kind];

        public static JxqyMobileControlLayout CreateDefaultLandscape()
        {
            return new JxqyMobileControlLayout(new[]
            {
                PlaceLegacy(JxqyMobileControlKind.Movement, 115, 455, 170, 0.55f),
                PlaceLegacy(JxqyMobileControlKind.Attack, 690, 470, 100, 0.7f),
                PlaceLegacy(JxqyMobileControlKind.Skill1, 575, 485, 76, 0.68f),
                PlaceLegacy(JxqyMobileControlKind.Skill2, 615, 395, 76, 0.68f),
                PlaceLegacy(JxqyMobileControlKind.Skill3, 710, 360, 76, 0.68f),
                PlaceLegacy(JxqyMobileControlKind.Item, 500, 505, 66, 0.62f),
                PlaceLegacy(JxqyMobileControlKind.Interact, 730, 260, 72, 0.65f),
                PlaceLegacy(JxqyMobileControlKind.Menu, 755, 45, 54, 0.55f),
                PlaceLegacy(JxqyMobileControlKind.Confirm, 655, 555, 54, 0.6f),
                PlaceLegacy(JxqyMobileControlKind.Cancel, 735, 555, 54, 0.6f),
            });
        }

        private static JxqyMobileControlPlacement PlaceLegacy(
            JxqyMobileControlKind kind,
            float x,
            float y,
            float size,
            float opacity)
        {
            return new JxqyMobileControlPlacement(
                kind,
                new JxqyFloat2(
                    x * LegacyDesignScale,
                    y * LegacyDesignScale),
                new JxqyFloat2(
                    size * LegacyDesignScale,
                    size * LegacyDesignScale),
                opacity);
        }
    }
}
