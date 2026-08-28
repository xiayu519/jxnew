using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using JxNewMod.Domain;

namespace JxNewMod.Runtime
{
    /// <summary>
    /// Composes the official catalog, one-Mod-per-process session and the
    /// Unity-facing entry points. Mod-specific branches live only here.
    /// </summary>
    public sealed class ModRuntimeCoordinator : IDisposable
    {
        private readonly IReadOnlyDictionary<ModId, IModEntryPoint>
            _entryPoints;
        private readonly CancellationTokenSource _lifetime = new();
        private bool _disposed;

        public ModRuntimeCoordinator(
            IModCatalog catalog,
            IModSession session,
            IEnumerable<IModEntryPoint> entryPoints)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (entryPoints == null)
                throw new ArgumentNullException(nameof(entryPoints));

            IModEntryPoint[] entries = entryPoints
                .Where(entry => entry != null)
                .ToArray();
            ModId duplicate = entries
                .GroupBy(entry => entry.ModId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicate.IsValid)
                throw new ArgumentException(
                    $"Mod entry point '{duplicate}' is duplicated.",
                    nameof(entryPoints));

            _entryPoints = entries.ToDictionary(entry => entry.ModId);
        }

        public IModCatalog Catalog { get; }
        public IModSession Session { get; }

        public static ModRuntimeCoordinator CreateBuiltIn()
        {
            return new ModRuntimeCoordinator(
                OfficialModCatalog.CreateBuiltIn(),
                new ModSession(),
                new IModEntryPoint[]
                {
                    new JxqyOfficialModEntryPoint(
                        ModId.XinJianXia,
                        Jxqy.UnityAdapters.JxqyRuntimeContentProfile
                            .XinJianXia),
                    new JxqyOfficialModEntryPoint(
                        ModId.LengJianHanMei,
                        Jxqy.UnityAdapters.JxqyRuntimeContentProfile
                            .XinJianXia),
                    new JxqyOfficialModEntryPoint(
                        ModId.MengLiHuiMou,
                        Jxqy.UnityAdapters.JxqyRuntimeContentProfile
                            .XinJianXia)
                });
        }

        public async UniTask<ModActivationResult> ActivateAsync(
            ModId modId,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return ModActivationResult.Failure(
                    "Mod 运行时已经关闭，请重新启动游戏。");
            if (!Catalog.TryGet(modId, out ModDescriptor descriptor))
                return ModActivationResult.Failure(
                    $"未找到官方 Mod：{modId}。");
            if (!_entryPoints.TryGetValue(modId, out IModEntryPoint entryPoint))
                return ModActivationResult.Failure(
                    $"Mod“{descriptor.DisplayName}”尚未注册运行入口。");

            ModActivationTicket ticket;
            try
            {
                ticket = Session.BeginActivation(descriptor);
            }
            catch (Exception exception)
            {
                return ModActivationResult.Failure(exception.Message);
            }

            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetime.Token);
            try
            {
                await entryPoint.ActivateAsync(descriptor, linked.Token);
                IActiveModContext context =
                    Session.CompleteActivation(ticket);
                return ModActivationResult.Success(context);
            }
            catch (OperationCanceledException)
            {
                entryPoint.Shutdown();
                return Rollback(
                    ticket,
                    "Mod 加载已取消，请重新选择或重启游戏。");
            }
            catch (Exception exception)
            {
                entryPoint.Shutdown();
                return Rollback(ticket, exception.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _lifetime.Cancel();

            foreach (IModEntryPoint entryPoint in _entryPoints.Values)
            {
                entryPoint.Shutdown();
                entryPoint.Dispose();
            }
            _lifetime.Dispose();
        }

        private ModActivationResult Rollback(
            ModActivationTicket ticket,
            string failure)
        {
            string message = string.IsNullOrWhiteSpace(failure)
                ? "Mod 加载失败。"
                : failure.Trim();
            try
            {
                Session.RollbackActivation(ticket, message);
            }
            catch (InvalidOperationException)
            {
                // A newer terminal state is authoritative. Never unlock an
                // active process because a stale async attempt ended.
            }
            return ModActivationResult.Failure(message);
        }
    }
}
