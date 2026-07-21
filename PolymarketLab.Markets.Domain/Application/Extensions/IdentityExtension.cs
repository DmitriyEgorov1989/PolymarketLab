using Microsoft.AspNetCore.Identity;
using PolymarketLab.SharedKernel.Errors;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.Markets.Core.Application.Extensions;

public static class IdentityExtension
{
    public static ErrorList ToErrorList(this IdentityResult identityResult)
    {
        ArgumentNullException.ThrowIfNull(identityResult);

        var errors = identityResult.Errors
            .Select(error => GeneralErrors.IdentityUser(error.Code, error.Description))
            .ToList();

        return new ErrorList(errors);
    }
}
