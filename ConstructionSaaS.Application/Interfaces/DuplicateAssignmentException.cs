namespace ConstructionSaaS.Application.Interfaces;

public sealed class DuplicateAssignmentException : Exception
{
    public DuplicateAssignmentException(Exception innerException)
        : base("Employee is already assigned to this project.", innerException) { }
}
