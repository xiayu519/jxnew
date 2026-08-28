using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Jxqy.Domain.World;
using Jxqy.Ports;
using TEngine;
using UnityEngine;
using FrameworkAudioType = TEngine.AudioType;

namespace Jxqy.UnityAdapters
{
    public sealed class JxqyUnityAudioPort :
        MonoBehaviour,
        IJxqyAudioPort,
        IJxqyWorldAudioPort
    {
        // The original sound engine converts screen-relative pixel offsets
        // into a 3D position using SOUND_FACTOR and the 64x32 map tile size.
        // FMOD then applies inverse rolloff with these min/max distances.
        private const float LegacySoundDistanceFactor = 0.5f;
        private const float LegacySoundVerticalOffset = 1f;
        private const float LegacySoundMinDistance = 0.5f;
        private const float LegacySoundMaxDistance = 5000f;
        private readonly HashSet<AudioAgent> _soundAgents = new();
        private readonly Dictionary<string, WorldSound> _worldSounds =
            new(StringComparer.OrdinalIgnoreCase);
        private IAudioModule _audioModule;
        private JxqyYooAssetPackageResolver _packageResolver;
        private AudioAgent _musicAgent;
        private AudioAgent _ambientAgent;
        private float _musicVolume = 1f;
        private float _soundVolume = 1f;
        private bool _isPaused;
        private Jxqy.Domain.World.JxqyFloat2 _worldListener;
        private Jxqy.Domain.World.JxqyFloat2 _worldViewportSize;
        private bool _worldListenerInitialized;

        public int RegisteredWorldSoundCount => _worldSounds.Count;

        public void Initialize(
            IAudioModule audioModule,
            string packageName = null)
        {
            Initialize(
                audioModule,
                new JxqyYooAssetPackageResolver(
                    new JxqyResourcePackageChain(
                        string.IsNullOrWhiteSpace(packageName)
                            ? JxqyResourceLocations.PackageName
                            : packageName.Trim())));
        }

        public void Initialize(
            IAudioModule audioModule,
            JxqyYooAssetPackageResolver packageResolver)
        {
            _audioModule = audioModule ??
                           throw new ArgumentNullException(
                               nameof(audioModule));
            _packageResolver = packageResolver ??
                throw new ArgumentNullException(nameof(packageResolver));
            _audioModule.Enable = true;
            _audioModule.Volume = 1f;
            _audioModule.SoundEnable = true;
            _audioModule.UISoundEnable = true;
            _audioModule.MusicVolume = _musicVolume;
            SetSoundVolume(1f);
        }

        public async UniTask PlayMusicAsync(
            string address,
            bool loop,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAddress(address);
            StopMusic();
            JxqyResolvedResourceLocation location =
                await _packageResolver.ResolveAsync(
                    address,
                    cancellationToken);
            _musicAgent = _audioModule.Play(
                FrameworkAudioType.Music,
                address,
                loop,
                _musicVolume,
                bAsync: true,
                bInPool: true,
                packageName: location.PackageName);
            if (_isPaused)
                _musicAgent?.Pause();
        }

        public void StopMusic()
        {
            _musicAgent?.Stop();
            _musicAgent = null;
        }

        public void StopSounds()
        {
            foreach (AudioAgent agent in _soundAgents)
                agent?.Stop();
            _soundAgents.Clear();
        }

        public async UniTask PlaySoundAsync(
            string address,
            float volume,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAddress(address);
            JxqyResolvedResourceLocation location =
                await _packageResolver.ResolveAsync(
                    address,
                    cancellationToken);
            _soundAgents.RemoveWhere(agent =>
                agent == null || agent.IsFree);
            // World loops retain Sound agents so their pan/volume can be
            // updated as the listener moves. If a foreground script sound
            // reuses one of those channels, the retained world owner can
            // overwrite its volume on the next update and make effects such
            // as OpenBox appear to fail intermittently. Use the independent
            // non-positional pool for foreground one-shots.
            AudioAgent soundAgent = _audioModule.Play(
                FrameworkAudioType.UISound,
                address,
                volume: Mathf.Clamp01(volume),
                bAsync: true,
                bInPool: true,
                packageName: location.PackageName);
            if (soundAgent != null)
            {
                _soundAgents.Add(soundAgent);
                if (_isPaused)
                    soundAgent.Pause();
            }
        }

        public async UniTask PlayAmbientLoopAsync(
            string address,
            float volume,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAddress(address);
            StopAmbientLoop();
            JxqyResolvedResourceLocation location =
                await _packageResolver.ResolveAsync(
                    address,
                    cancellationToken);
            _ambientAgent = _audioModule.Play(
                FrameworkAudioType.Sound,
                address,
                bLoop: true,
                volume: Mathf.Clamp01(volume),
                bAsync: true,
                bInPool: true,
                packageName: location.PackageName);
            if (_isPaused)
                _ambientAgent?.Pause();
        }

        public void StopAmbientLoop()
        {
            _ambientAgent?.Stop();
            _ambientAgent = null;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            if (paused)
            {
                _musicAgent?.Pause();
                _ambientAgent?.Pause();
                foreach (AudioAgent agent in _soundAgents)
                    agent?.Pause();
            }
            else
            {
                _musicAgent?.UnPause();
                _ambientAgent?.UnPause();
                foreach (AudioAgent agent in _soundAgents)
                    agent?.UnPause();
            }
        }

        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            if (_audioModule != null)
                _audioModule.MusicVolume = _musicVolume;
            if (_musicAgent != null)
                _musicAgent.Volume = _musicVolume;
        }

        public void SetSoundVolume(float volume)
        {
            _soundVolume = Mathf.Clamp01(volume);
            if (_audioModule == null)
                return;
            _audioModule.SoundVolume = _soundVolume;
            _audioModule.UISoundVolume = _soundVolume;
        }

        public async UniTask RegisterWorldSoundAsync(
            string id,
            string address,
            bool loop,
            Jxqy.Domain.World.JxqyFloat2 worldPosition,
            float volume,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAddress(address);
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException(
                    "World sound id must not be empty.",
                    nameof(id));
            RemoveWorldSound(id);
            JxqyResolvedResourceLocation location =
                await _packageResolver.ResolveAsync(
                    address,
                    cancellationToken);
            var sound = new WorldSound
            {
                Address = address,
                PackageName = location.PackageName,
                Position = worldPosition,
                Volume = Mathf.Clamp01(volume),
                Loop = loop,
                NextRandomPlayTime = Time.unscaledTime +
                                     UnityEngine.Random.Range(0.5f, 4f),
            };
            _worldSounds.Add(id, sound);
            if (loop)
                sound.Agent = CreateWorldAgent(sound, true);
            ApplyWorldSoundMix(sound);
        }

        public async UniTask PlayWorldSoundOnceAsync(
            string address,
            Jxqy.Domain.World.JxqyFloat2 worldPosition,
            float volume,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAddress(address);
            JxqyResolvedResourceLocation location =
                await _packageResolver.ResolveAsync(
                    address,
                    cancellationToken);
            _soundAgents.RemoveWhere(agent =>
                agent == null || agent.IsFree);
            var sound = new WorldSound
            {
                Address = address,
                PackageName = location.PackageName,
                Position = worldPosition,
                Volume = Mathf.Clamp01(volume),
            };
            AudioAgent agent = CreateWorldAgent(sound, false);
            sound.Agent = agent;
            if (agent != null)
            {
                _soundAgents.Add(agent);
                ApplyWorldSoundMix(sound);
            }
        }

        public void SetWorldSoundPosition(
            string id,
            Jxqy.Domain.World.JxqyFloat2 worldPosition)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !_worldSounds.TryGetValue(id, out WorldSound sound))
            {
                return;
            }
            sound.Position = worldPosition;
            ApplyWorldSoundMix(sound);
        }

        public void RemoveWorldSound(string id)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !_worldSounds.TryGetValue(id, out WorldSound sound))
            {
                return;
            }
            sound.Agent?.Stop();
            _worldSounds.Remove(id);
        }

        public void ClearWorldSounds()
        {
            foreach (WorldSound sound in _worldSounds.Values)
                sound.Agent?.Stop();
            _worldSounds.Clear();
            _worldListenerInitialized = false;
        }

        public void SetWorldSoundListener(
            Jxqy.Domain.World.JxqyFloat2 worldPosition,
            Jxqy.Domain.World.JxqyFloat2 viewportSize)
        {
            _worldListener = worldPosition;
            _worldViewportSize = viewportSize;
            _worldListenerInitialized =
                viewportSize.X > 0f && viewportSize.Y > 0f;
            foreach (WorldSound sound in _worldSounds.Values)
                ApplyWorldSoundMix(sound);
        }

        private void Update()
        {
            if (_isPaused || _audioModule == null)
                return;
            float now = Time.unscaledTime;
            foreach (WorldSound sound in _worldSounds.Values)
            {
                if (sound.Loop || now < sound.NextRandomPlayTime)
                    continue;
                sound.Agent = CreateWorldAgent(sound, false);
                sound.NextRandomPlayTime =
                    now + UnityEngine.Random.Range(0.5f, 6f);
                ApplyWorldSoundMix(sound);
            }
        }

        private void OnDestroy()
        {
            StopMusic();
            StopAmbientLoop();
            ClearWorldSounds();
            StopSounds();
        }

        private void EnsureInitialized()
        {
            if (_audioModule == null)
                throw new InvalidOperationException(
                    "Jxqy audio port has not been initialized.");
        }

        private static void EnsureAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException(
                    "Audio address must not be empty.",
                    nameof(address));
        }

        private AudioAgent CreateWorldAgent(WorldSound sound, bool loop)
        {
            AudioAgent agent = _audioModule.Play(
                FrameworkAudioType.Sound,
                sound.Address,
                bLoop: loop,
                volume: 0f,
                bAsync: true,
                bInPool: true,
                packageName: sound.PackageName);
            if (_isPaused)
                agent?.Pause();
            return agent;
        }

        private void ApplyWorldSoundMix(WorldSound sound)
        {
            if (sound.Agent == null)
                return;
            if (!_worldListenerInitialized)
            {
                // Original looping sounds start at SOUND_FAREST and remain
                // effectively silent until the first camera-relative update.
                sound.Agent.Volume = 0f;
                return;
            }
            float deltaX = sound.Position.X - _worldListener.X;
            float deltaY = sound.Position.Y - _worldListener.Y;
            if (Mathf.Abs(deltaX) > _worldViewportSize.X * 0.5f ||
                Mathf.Abs(deltaY) > _worldViewportSize.Y * 0.5f)
            {
                // Object::draw moves looping sounds to SOUND_FAREST whenever
                // their source point is outside the current game viewport.
                sound.Agent.Volume = 0f;
                return;
            }
            CalculateLegacyWorldSoundMix(
                deltaX,
                deltaY,
                out float attenuation,
                out float pan);
            sound.Agent.Volume = sound.Volume * attenuation;
            AudioSource source = sound.Agent.AudioResource();
            if (source == null)
                return;
            source.spatialBlend = 0f;
            source.panStereo = pan;
        }

        private static void CalculateLegacyWorldSoundMix(
            float deltaX,
            float deltaY,
            out float attenuation,
            out float pan)
        {
            float soundX = LegacySoundDistanceFactor * deltaX /
                           JxqyIsometricMapMath.TileWidth;
            float soundZ = LegacySoundDistanceFactor * deltaY /
                           JxqyIsometricMapMath.TileHeight;
            float distance = Mathf.Sqrt(
                soundX * soundX +
                LegacySoundVerticalOffset * LegacySoundVerticalOffset +
                soundZ * soundZ);
            float rolloffDistance = Mathf.Min(
                distance,
                LegacySoundMaxDistance);
            attenuation = Mathf.Clamp01(
                LegacySoundMinDistance / rolloffDistance);

            float horizontalDistance = Mathf.Sqrt(
                soundX * soundX + soundZ * soundZ);
            pan = horizontalDistance > Mathf.Epsilon
                ? Mathf.Clamp(soundX / horizontalDistance, -1f, 1f)
                : 0f;
        }

        private sealed class WorldSound
        {
            public string Address;
            public string PackageName;
            public Jxqy.Domain.World.JxqyFloat2 Position;
            public float Volume;
            public bool Loop;
            public float NextRandomPlayTime;
            public AudioAgent Agent;
        }
    }
}
