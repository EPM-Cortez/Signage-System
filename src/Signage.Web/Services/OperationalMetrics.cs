namespace Signage.Web.Services;

public sealed class OperationalMetrics
{
    private long assignmentErrors;

    public long AssignmentErrors => Interlocked.Read(ref assignmentErrors);
    public void RecordAssignmentError() => Interlocked.Increment(ref assignmentErrors);
}
