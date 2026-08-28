using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Jxqy.Domain.Presentation;
using Jxqy.Ports;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Jxqy.UnityAdapters
{
    public sealed class JxqyUnityVideoPort : MonoBehaviour, IJxqyVideoPort
    {
        private const string VideoOverlayResourcePath =
            "Jxqy/UI/JxqyVideoOverlay";
        private IJxqyResourcePort _resources;
        private VideoPlayer _player;
        private JxqyResourceScope _scope;
        private IDisposable _lease;
        private UniTaskCompletionSource _playbackCompletion;
        private Canvas _overlayCanvas;
        private RawImage _overlayImage;
        private RenderTexture _targetTexture;
        private bool _ownsTargetTexture;
        private bool _isPaused;
        private bool _isPlaying;

        public event Action PlaybackStarted;
        public bool IsPlaying => _isPlaying;
        public bool IsPresentationActive =>
            _overlayCanvas != null &&
            _overlayCanvas.gameObject.activeInHierarchy;
#if UNITY_EDITOR
        public string LastRequestedAddress { get; private set; } =
            string.Empty;
#endif
        public bool IsOverlayTopmost =>
            _overlayCanvas != null &&
            _overlayCanvas.gameObject.activeInHierarchy &&
            _overlayCanvas.renderMode == RenderMode.ScreenSpaceOverlay &&
            _overlayCanvas.sortingOrder == short.MaxValue;

        public void Initialize(
            IJxqyResourcePort resources,
            RenderTexture targetTexture = null)
        {
            _resources = resources ??
                         throw new ArgumentNullException(nameof(resources));
            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.VideoClip;
            _player.audioOutputMode =
                VideoAudioOutputMode.Direct;
            _targetTexture = targetTexture ??
                new RenderTexture(
                    JxqyLogicalViewport.OriginalWidth,
                    JxqyLogicalViewport.OriginalHeight,
                    0,
                    RenderTextureFormat.ARGB32)
                {
                    name = "Jxqy Video Overlay Texture",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            _ownsTargetTexture = targetTexture == null;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _targetTexture;
            CreateOverlay();
        }

        public void BindCamera(Camera targetCamera)
        {
            EnsureInitialized();
            if (targetCamera == null)
                throw new ArgumentNullException(nameof(targetCamera));
        }

        public void RequestSkip()
        {
            _playbackCompletion?.TrySetResult();
        }

        public void ShowBlackTransition()
        {
            EnsureInitialized();
            ClearTargetTexture();
            SetOverlayVisible(true);
        }

        public async UniTask PlayAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
#if UNITY_EDITOR
            LastRequestedAddress = address ?? string.Empty;
#endif
            if (Application.isBatchMode)
            {
                PlaybackStarted?.Invoke();
                Stop();
                return;
            }
            ReleasePlayback(hideOverlay: false);
            _scope = new JxqyResourceScope(
                $"video:{Guid.NewGuid():N}");
            JxqyAssetLease<VideoClip> lease =
                await _resources.LoadAsync<VideoClip>(
                    address,
                    _scope,
                    cancellationToken);
            _lease = lease;
            _player.clip = lease.Asset;
            var prepared =
                new UniTaskCompletionSource();
            var completed =
                new UniTaskCompletionSource();
            _playbackCompletion = completed;
            void OnPrepared(VideoPlayer _) => prepared.TrySetResult();
            void OnError(VideoPlayer _, string message) =>
                completed.TrySetException(
                    new InvalidOperationException(message));
            void OnCompleted(VideoPlayer _) =>
                completed.TrySetResult();
            _player.prepareCompleted += OnPrepared;
            _player.errorReceived += OnError;
            _player.loopPointReached += OnCompleted;
            try
            {
                ClearTargetTexture();
                SetOverlayVisible(true);
                _player.Prepare();
                await UniTask.WhenAny(
                    prepared.Task,
                    completed.Task);
                cancellationToken.ThrowIfCancellationRequested();
                if (completed.Task.Status ==
                    UniTaskStatus.Faulted)
                {
                    await completed.Task;
                }
                if (completed.Task.Status ==
                    UniTaskStatus.Succeeded)
                {
                    return;
                }
                _player.Play();
                _isPlaying = true;
                PlaybackStarted?.Invoke();
                if (_isPaused)
                    _player.Pause();
                await completed.Task.AttachExternalCancellation(
                    cancellationToken);
            }
            catch
            {
                Stop();
                throw;
            }
            finally
            {
                _isPlaying = false;
                _playbackCompletion = null;
                _player.prepareCompleted -= OnPrepared;
                _player.errorReceived -= OnError;
                _player.loopPointReached -= OnCompleted;
                Stop();
            }
        }

        public void Stop()
        {
            ReleasePlayback(hideOverlay: true);
        }

        private void ReleasePlayback(bool hideOverlay)
        {
            if (hideOverlay)
                SetOverlayVisible(false);
            if (_player != null)
            {
                _player.Stop();
                _player.clip = null;
            }
            _lease?.Dispose();
            _lease = null;
            if (_scope != null && _resources != null)
                _resources.ReleaseScopeAsync(
                    _scope,
                    CancellationToken.None).Forget();
            _scope = null;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            if (_player == null || !_player.isPrepared)
                return;
            if (paused)
                _player.Pause();
            else
                _player.Play();
        }

        private void OnDestroy()
        {
            Stop();
            if (_ownsTargetTexture && _targetTexture != null)
            {
                _targetTexture.Release();
                if (Application.isPlaying)
                    Destroy(_targetTexture);
                else
                    DestroyImmediate(_targetTexture);
            }
            _targetTexture = null;
            _ownsTargetTexture = false;
        }

        private void CreateOverlay()
        {
            GameObject prefab = Resources.Load<GameObject>(
                VideoOverlayResourcePath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Static video overlay prefab is missing: " +
                    $"Resources/{VideoOverlayResourcePath}.prefab");
            }
            GameObject overlay = Instantiate(prefab, transform, false);
            overlay.name = prefab.name;
            _overlayCanvas = overlay.GetComponent<Canvas>();
            Transform imageTransform = overlay.transform.Find("Video");
            _overlayImage = imageTransform?.GetComponent<RawImage>();
            Button skipButton = imageTransform?.GetComponent<Button>();
            if (_overlayCanvas == null || _overlayImage == null ||
                skipButton == null)
            {
                throw new InvalidOperationException(
                    "Static video overlay prefab hierarchy is incomplete.");
            }
            _overlayImage.texture = _targetTexture;
            skipButton.onClick.AddListener(RequestSkip);
            overlay.SetActive(false);
        }

        private void SetOverlayVisible(bool visible)
        {
            if (_overlayCanvas != null)
                _overlayCanvas.gameObject.SetActive(visible);
        }

        private void ClearTargetTexture()
        {
            if (_targetTexture == null)
                return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _targetTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }

        private void EnsureInitialized()
        {
            if (_resources == null || _player == null)
                throw new InvalidOperationException(
                    "Jxqy video port has not been initialized.");
        }
    }
}
