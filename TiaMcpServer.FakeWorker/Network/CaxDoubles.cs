// Stand-ins for the CAx result types of Siemens.Engineering.Step7.xml (V21): TransferResult (State, ErrorCount, WarningCount,
// Messages), TransferResultMessage (Message, State, DateTime, Messages) and TransferResultState. Member shapes only; no evidence of
// Siemens behavior.
namespace Siemens.Engineering.Cax
{
    public enum TransferResultState { Success, Information, Warning, Error }

    public sealed class TransferResultMessage(string message, TransferResultState state)
    {
        public string Message { get; } = message;
        public TransferResultState State { get; } = state;
        public System.DateTime DateTime { get; } = new(2026, 11, 2, 12, 0, 0, System.DateTimeKind.Utc);
        public List<TransferResultMessage> Messages { get; } = new();
    }

    public sealed class TransferResult(TransferResultState state, params TransferResultMessage[] messages)
    {
        public TransferResultState State { get; } = state;
        public int ErrorCount => Messages.Count(message => message.State == TransferResultState.Error);
        public int WarningCount => Messages.Count(message => message.State == TransferResultState.Warning);
        public List<TransferResultMessage> Messages { get; } = messages.ToList();
    }
}
