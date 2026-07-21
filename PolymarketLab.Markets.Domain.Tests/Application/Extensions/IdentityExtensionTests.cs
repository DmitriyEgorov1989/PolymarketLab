using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using PolymarketLab.Markets.Core.Application.Extensions;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.Extensions;

public sealed class IdentityExtensionTests
{
    [Fact]
    public void ToErrorList_WithIdentityErrors_ShouldMapAllErrors()
    {
        var identityResult = IdentityResult.Failed(
            new IdentityError { Code = "duplicate", Description = "Duplicate value." },
            new IdentityError { Code = "invalid", Description = "Invalid value." });

        var errors = identityResult.ToErrorList().ToList();

        errors.Should().HaveCount(2);
        errors[0].Should().Be(new Error("duplicate", "Duplicate value.", ErrorType.IdentityUser));
        errors[1].Should().Be(new Error("invalid", "Invalid value.", ErrorType.IdentityUser));
    }

    [Fact]
    public void ToErrorList_WithSuccessfulResult_ShouldReturnEmptyList()
    {
        var errors = IdentityResult.Success.ToErrorList();

        errors.Should().BeEmpty();
    }
}
