
namespace ConstructionSaaS.Application.DTOs.ProjectAssignments;

public enum AssignmentError
{
    None,
    InvalidInput,
    ProjectNotFound,
    UserNotFound,
    WrongCompany,
    InactiveUser,
    InvalidRole,
    AlreadyAssigned,
    AssignmentNotFound
}

public class AssignmentResult<T>
{
    public bool Success => Error == AssignmentError.None;

    public T? Data { get; init; }

    public AssignmentError Error { get; init; }

    public static AssignmentResult<T> Ok(T data)
        => new() { Data = data };

    public static AssignmentResult<T> Fail(AssignmentError error)
        => new() { Error = error };
}
