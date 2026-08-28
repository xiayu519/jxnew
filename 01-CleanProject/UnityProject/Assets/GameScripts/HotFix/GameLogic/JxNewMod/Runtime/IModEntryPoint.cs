using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using JxNewMod.Domain;

namespace JxNewMod.Runtime
{
    /// <summary>
    /// Owns the Unity-facing activation boundary for one official Mod.
    /// </summary>
    public interface IModEntryPoint : IDisposable
    {
        ModId ModId { get; }
        UniTask ActivateAsync(
            ModDescriptor descriptor,
            CancellationToken cancellationToken);
        void Shutdown();
    }
}
