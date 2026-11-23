using MDel.Domain;
using MDel.Domain.Entities;
using MDel.Domain.Enums;

namespace MDel.Tests;

public class StateMachineTests
{
    [Theory]
    [InlineData(DelegationStatus.PendingApproval, DelegationStatus.Scheduled, true)]
    [InlineData(DelegationStatus.PendingApproval, DelegationStatus.Active, true)]
    [InlineData(DelegationStatus.PendingApproval, DelegationStatus.Rejected, true)]
    [InlineData(DelegationStatus.PendingApproval, DelegationStatus.Expired, true)] // neaprobată până la sfârșit
    [InlineData(DelegationStatus.PendingApproval, DelegationStatus.Failed, false)]
    [InlineData(DelegationStatus.Scheduled, DelegationStatus.Active, true)]
    [InlineData(DelegationStatus.Scheduled, DelegationStatus.PendingApproval, true)]
    [InlineData(DelegationStatus.Scheduled, DelegationStatus.Rejected, false)]
    [InlineData(DelegationStatus.Active, DelegationStatus.Expired, true)]
    [InlineData(DelegationStatus.Active, DelegationStatus.Stopped, true)]
    [InlineData(DelegationStatus.Active, DelegationStatus.Scheduled, false)]
    [InlineData(DelegationStatus.Failed, DelegationStatus.Scheduled, true)]
    [InlineData(DelegationStatus.Expired, DelegationStatus.Active, false)]
    [InlineData(DelegationStatus.Stopped, DelegationStatus.Active, false)]
    [InlineData(DelegationStatus.Rejected, DelegationStatus.Scheduled, false)]
    public void Transitions(DelegationStatus from, DelegationStatus to, bool allowed) =>
        Assert.Equal(allowed, DelegationStateMachine.CanTransition(from, to));

    [Fact]
    public void Final_statuses()
    {
        Assert.True(DelegationStateMachine.IsFinal(DelegationStatus.Expired));
        Assert.True(DelegationStateMachine.IsFinal(DelegationStatus.Rejected));
        Assert.True(DelegationStateMachine.IsFinal(DelegationStatus.Stopped));
        Assert.False(DelegationStateMachine.IsFinal(DelegationStatus.Active));
    }

    [Fact]
    public void Transition_records_history_and_rejects_invalid_moves()
    {
        var d = new Delegation { Status = DelegationStatus.PendingApproval };
        var now = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

        d.TransitionTo(DelegationStatus.Scheduled, 7, now, "approved");

        Assert.Equal(DelegationStatus.Scheduled, d.Status);
        var h = Assert.Single(d.StatusHistory);
        Assert.Equal(DelegationStatus.PendingApproval, h.FromStatus);
        Assert.Equal(7, h.ChangedById);
        Assert.Throws<InvalidStatusTransitionException>(() => d.TransitionTo(DelegationStatus.Rejected, 7, now));
    }

    [Fact]
    public void Status_after_approval_depends_on_start()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(DelegationStatus.Active,
            DelegationStateMachine.StatusAfterApproval(new Delegation { StartUtc = now.AddMinutes(-1) }, now));
        Assert.Equal(DelegationStatus.Scheduled,
            DelegationStateMachine.StatusAfterApproval(new Delegation { StartUtc = now.AddMinutes(1) }, now));
    }
}
