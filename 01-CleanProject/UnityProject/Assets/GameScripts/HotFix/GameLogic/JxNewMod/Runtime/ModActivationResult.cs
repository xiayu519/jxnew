using JxNewMod.Domain;

namespace JxNewMod.Runtime
{
    public readonly struct ModActivationResult
    {
        private ModActivationResult(
            bool succeeded,
            string message,
            IActiveModContext activeContext)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            ActiveContext = activeContext;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public IActiveModContext ActiveContext { get; }

        public static ModActivationResult Success(
            IActiveModContext activeContext) =>
            new(true, string.Empty, activeContext);

        public static ModActivationResult Failure(string message) =>
            new(false, message, null);
    }
}
