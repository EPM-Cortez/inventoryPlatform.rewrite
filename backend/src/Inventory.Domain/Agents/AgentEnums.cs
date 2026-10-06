namespace Inventory.Domain.Agents;

public enum AgentKeyProtection
{
    Tpm,
    Software,
}

public enum ManagedAgentMatchState
{
    Unmatched,
    Matched,
    Conflict,
    Dismissed,
}

public enum AgentRequestNonceScope
{
    Enrollment,
    KeyRecovery,
    CheckIn,
}
