namespace Racing.Contracts
{
    /// <summary>Lifecycle status of a session (spec A5).</summary>
    public enum SessionStatus
    {
        NotStarted,
        InProgress,
        Completed,
        EndedEarly,
        Skipped
    }
}
