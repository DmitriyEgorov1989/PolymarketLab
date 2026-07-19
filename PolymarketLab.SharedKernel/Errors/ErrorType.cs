namespace PolymarketLab.SharedKernel.Errors;

public enum ErrorType
{
    NotFound,
    Validation,
    ValueIsInvalid,
    ValueIsRequired,
    InvalidLength,
    InvalidSize,
    CollectionIsTooSmall,
    CollectionIsTooLarge,
    InternalServerError,
    IdentityUser,
    Authorization,
    Failure,
    Conflict
}
