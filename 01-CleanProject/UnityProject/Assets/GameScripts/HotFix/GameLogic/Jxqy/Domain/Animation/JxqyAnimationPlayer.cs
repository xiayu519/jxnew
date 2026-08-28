using System;
using System.Linq;
using Jxqy.Domain.Content;

namespace Jxqy.Domain.Animation
{
    public sealed class JxqyAnimationPlayer
    {
        // The reference engine runs Sprite.Update at MonoGame's fixed 60 Hz.
        // Each update truncates ElapsedGameTime to 16 milliseconds and can
        // advance at most one texture frame, even when an ASF declares a
        // shorter interval. Keep that authored stepping rule independent of
        // Unity's 120 Hz presentation rate; otherwise short enemy attacks
        // (for example 10 ms bee frames) finish substantially too early.
        private const double ReferenceUpdateSeconds = 1.0 / 60.0;
        private const int ReferenceElapsedMilliseconds = 16;

        private readonly JxqyAnimationMetadata _metadata;
        private int _direction;
        private int _frameWithinDirection;
        private double _frameElapsedMilliseconds;
        private double _referenceUpdateElapsedSeconds;

        public JxqyAnimationPlayer(JxqyAnimationMetadata metadata)
        {
            _metadata = metadata ??
                        throw new ArgumentNullException(nameof(metadata));
            if (metadata.Frames == null || metadata.Frames.Count == 0)
                throw new ArgumentException(
                    "Animation contains no frames.",
                    nameof(metadata));
            if (metadata.Directions == null ||
                metadata.Directions.Count == 0)
                throw new ArgumentException(
                    "Animation contains no directions.",
                    nameof(metadata));
        }

        public bool IsLooping { get; set; } = true;
        public bool IsReversed { get; set; }
        public bool IsFinished { get; private set; }
        public int Direction => _direction;
        public int FrameWithinDirection => _frameWithinDirection;
        public JxqyAnimationMetadata Metadata => _metadata;

        public void SetDirection(int direction)
        {
            int count = Math.Max(1, _metadata.DirectionCount);
            int normalized = direction % count;
            if (normalized < 0)
                normalized += count;
            _direction = normalized;
            JxqyAnimationDirectionMetadata info = GetDirection();
            if (_frameWithinDirection >= info.FrameCount)
                _frameWithinDirection = 0;
        }

        public void Restart()
        {
            _frameWithinDirection = IsReversed
                ? Math.Max(0, GetDirection().FrameCount - 1)
                : 0;
            _frameElapsedMilliseconds = 0;
            _referenceUpdateElapsedSeconds = 0;
            IsFinished = false;
        }

        public void SeekFrame(int frameWithinDirection)
        {
            JxqyAnimationDirectionMetadata direction = GetDirection();
            _frameWithinDirection = Math.Max(
                0,
                Math.Min(
                    frameWithinDirection,
                    direction.FrameCount - 1));
            _frameElapsedMilliseconds = 0;
            _referenceUpdateElapsedSeconds = 0;
            IsFinished = false;
        }

        public void PlayForward()
        {
            IsReversed = false;
            Restart();
        }

        public void PlayReverse()
        {
            IsReversed = true;
            Restart();
        }

        public void Advance(double elapsedSeconds)
        {
            if (elapsedSeconds < 0 || double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (IsFinished)
                return;
            _referenceUpdateElapsedSeconds += elapsedSeconds;
            int referenceUpdates = (int)Math.Floor(
                (_referenceUpdateElapsedSeconds + 1e-12) /
                ReferenceUpdateSeconds);
            if (referenceUpdates <= 0)
                return;
            _referenceUpdateElapsedSeconds -=
                referenceUpdates * ReferenceUpdateSeconds;
            JxqyAnimationDirectionMetadata direction = GetDirection();
            for (int update = 0;
                 update < referenceUpdates && !IsFinished;
                 update++)
            {
                _frameElapsedMilliseconds +=
                    ReferenceElapsedMilliseconds;
                JxqyAnimationFrameMetadata frame = CurrentFrame;
                int duration = Math.Max(1, frame.DurationMilliseconds);
                // Reference Sprite.Update uses a strict greater-than check.
                if (_frameElapsedMilliseconds <= duration)
                    continue;
                _frameElapsedMilliseconds -= duration;
                _frameWithinDirection += IsReversed ? -1 : 1;
                if (_frameWithinDirection >= 0 &&
                    _frameWithinDirection < direction.FrameCount)
                    continue;
                if (IsLooping)
                {
                    _frameWithinDirection = IsReversed
                        ? Math.Max(0, direction.FrameCount - 1)
                        : 0;
                }
                else
                {
                    _frameWithinDirection = IsReversed
                        ? 0
                        : Math.Max(0, direction.FrameCount - 1);
                    _frameElapsedMilliseconds = 0;
                    IsFinished = true;
                }
            }
        }

        public JxqyAnimationFrameMetadata CurrentFrame
        {
            get
            {
                JxqyAnimationDirectionMetadata direction = GetDirection();
                int sourceIndex = direction.FirstFrameIndex +
                                  Math.Min(
                                      _frameWithinDirection,
                                      Math.Max(0, direction.FrameCount - 1));
                JxqyAnimationFrameMetadata exact = _metadata.Frames
                    .FirstOrDefault(frame =>
                        frame.SourceFrameIndex == sourceIndex);
                if (exact == null)
                    throw new InvalidOperationException(
                        $"Animation frame {sourceIndex} is missing.");
                return exact;
            }
        }

        public JxqyAnimationPose GetPose()
        {
            JxqyAnimationFrameMetadata frame = CurrentFrame;
            if (frame.AtlasPage < 0 ||
                frame.AtlasPage >= _metadata.AtlasAddresses.Count)
                throw new InvalidOperationException(
                    $"Animation frame {frame.SourceFrameIndex} has invalid atlas page {frame.AtlasPage}.");
            int anchorX = _metadata.GlobalWidth > 0 &&
                          frame.PixelWidth > 0
                ? frame.GetAtlasAnchorX(_metadata.AnchorLeft)
                : frame.AnchorX;
            int anchorY = _metadata.GlobalHeight > 0 &&
                          frame.PixelHeight > 0
                ? frame.GetAtlasAnchorY(_metadata.AnchorBottom)
                : frame.AnchorY;
            return new JxqyAnimationPose(
                _metadata.AtlasAddresses[frame.AtlasPage],
                frame.AtlasX,
                frame.AtlasY,
                frame.AtlasWidth,
                frame.AtlasHeight,
                anchorX,
                anchorY,
                frame.HasShadow,
                frame.ShadowFrameIndex);
        }

        private JxqyAnimationDirectionMetadata GetDirection()
        {
            JxqyAnimationDirectionMetadata info = _metadata.Directions
                .FirstOrDefault(direction =>
                    direction.DirectionIndex == _direction);
            if (info == null || info.FrameCount <= 0)
                throw new InvalidOperationException(
                    $"Animation direction {_direction} is missing or empty.");
            return info;
        }
    }

    public readonly struct JxqyAnimationPose
    {
        public JxqyAnimationPose(
            string atlasAddress,
            int atlasX,
            int atlasY,
            int width,
            int height,
            int anchorX,
            int anchorY,
            bool hasShadow,
            int shadowFrameIndex)
        {
            AtlasAddress = atlasAddress;
            AtlasX = atlasX;
            AtlasY = atlasY;
            Width = width;
            Height = height;
            AnchorX = anchorX;
            AnchorY = anchorY;
            HasShadow = hasShadow;
            ShadowFrameIndex = shadowFrameIndex;
        }

        public string AtlasAddress { get; }
        public int AtlasX { get; }
        public int AtlasY { get; }
        public int Width { get; }
        public int Height { get; }
        public int AnchorX { get; }
        public int AnchorY { get; }
        public bool HasShadow { get; }
        public int ShadowFrameIndex { get; }
    }
}
