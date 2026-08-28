using System;

namespace JxNewMod.Domain
{
    public enum ModSessionState
    {
        NotSelected = 0,
        Activating = 1,
        Active = 2
    }

    public interface IActiveModContext
    {
        ModDescriptor Descriptor { get; }
        ModId ModId { get; }
        string PackageName { get; }
        string SaveNamespace { get; }
        ModContentAddresses Content { get; }
        string ScriptDialectId { get; }
        System.Collections.Generic.IReadOnlyList<ModResourcePackage>
            ResourcePackages { get; }
    }

    public sealed class ActiveModContext : IActiveModContext
    {
        public ActiveModContext(ModDescriptor descriptor)
        {
            Descriptor = descriptor ??
                throw new ArgumentNullException(nameof(descriptor));
        }

        public ModDescriptor Descriptor { get; }
        public ModId ModId => Descriptor.Id;
        public string PackageName => Descriptor.PackageName;
        public string SaveNamespace => Descriptor.SaveNamespace;
        public ModContentAddresses Content => Descriptor.Content;
        public string ScriptDialectId => Descriptor.ScriptDialectId;
        public System.Collections.Generic.IReadOnlyList<ModResourcePackage>
            ResourcePackages => Descriptor.ResourcePackages;
    }

    public readonly struct ModActivationTicket : IEquatable<ModActivationTicket>
    {
        internal ModActivationTicket(long sequence, ModId modId)
        {
            Sequence = sequence;
            ModId = modId;
        }

        internal long Sequence { get; }
        public ModId ModId { get; }
        public bool IsValid => Sequence > 0 && ModId.IsValid;

        public bool Equals(ModActivationTicket other) =>
            Sequence == other.Sequence && ModId == other.ModId;

        public override bool Equals(object obj) =>
            obj is ModActivationTicket other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Sequence.GetHashCode() * 397) ^ ModId.GetHashCode();
            }
        }
    }

    public interface IModSession
    {
        ModSessionState State { get; }
        ModDescriptor PendingMod { get; }
        IActiveModContext ActiveContext { get; }
        string LastActivationFailure { get; }
        ModActivationTicket BeginActivation(ModDescriptor descriptor);
        IActiveModContext CompleteActivation(ModActivationTicket ticket);
        void RollbackActivation(ModActivationTicket ticket, string failure);
    }

    public sealed class ModSession : IModSession
    {
        private readonly object _gate = new();
        private long _sequence;
        private ModActivationTicket _pendingTicket;
        private ModDescriptor _pendingMod;
        private IActiveModContext _activeContext;
        private string _lastActivationFailure;

        public ModSessionState State { get; private set; } =
            ModSessionState.NotSelected;

        public ModDescriptor PendingMod
        {
            get
            {
                lock (_gate)
                    return _pendingMod;
            }
        }

        public IActiveModContext ActiveContext
        {
            get
            {
                lock (_gate)
                    return _activeContext;
            }
        }

        public string LastActivationFailure
        {
            get
            {
                lock (_gate)
                    return _lastActivationFailure;
            }
        }

        public ModActivationTicket BeginActivation(ModDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (!descriptor.IsEnabled)
                throw new InvalidOperationException(
                    $"Mod '{descriptor.Id}' is disabled.");

            lock (_gate)
            {
                if (State == ModSessionState.Active)
                    throw new InvalidOperationException(
                        $"Mod '{_activeContext.ModId}' is already active for this process.");
                if (State == ModSessionState.Activating)
                    throw new InvalidOperationException(
                        $"Mod '{_pendingMod.Id}' is already activating.");

                _sequence++;
                _pendingTicket = new ModActivationTicket(
                    _sequence,
                    descriptor.Id);
                _pendingMod = descriptor;
                _lastActivationFailure = null;
                State = ModSessionState.Activating;
                return _pendingTicket;
            }
        }

        public IActiveModContext CompleteActivation(ModActivationTicket ticket)
        {
            lock (_gate)
            {
                EnsureCurrentTicket(ticket);
                _activeContext = new ActiveModContext(_pendingMod);
                _pendingMod = null;
                _pendingTicket = default;
                State = ModSessionState.Active;
                return _activeContext;
            }
        }

        public void RollbackActivation(
            ModActivationTicket ticket,
            string failure)
        {
            lock (_gate)
            {
                EnsureCurrentTicket(ticket);
                _lastActivationFailure = string.IsNullOrWhiteSpace(failure)
                    ? "Mod activation failed."
                    : failure.Trim();
                _pendingMod = null;
                _pendingTicket = default;
                State = ModSessionState.NotSelected;
            }
        }

        private void EnsureCurrentTicket(ModActivationTicket ticket)
        {
            if (State != ModSessionState.Activating ||
                !ticket.IsValid ||
                !_pendingTicket.Equals(ticket))
            {
                throw new InvalidOperationException(
                    "The Mod activation ticket is stale or does not belong to the active attempt.");
            }
        }
    }
}
