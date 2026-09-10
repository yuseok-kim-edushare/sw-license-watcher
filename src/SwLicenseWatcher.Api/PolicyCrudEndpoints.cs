using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class PolicyCrudEndpoints
{
    internal static IEndpointRouteBuilder MapPolicyCrudEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/policies/{id:long}", async (
            long id,
            IPolicyStore repository,
            CancellationToken cancellationToken) =>
        {
            var policy = await repository.GetPolicyAsync(id, cancellationToken);
            return policy is null ? Results.NotFound() : Results.Ok(policy);
        });
        endpoints.MapPost("/api/policies", async (
            SoftwarePolicyWriteRequest request,
            IPolicyStore repository,
            CancellationToken cancellationToken) =>
        {
            if (!SoftwarePolicyValidator.TryValidate(request, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            var created = await repository.CreatePolicyAsync(request, cancellationToken);
            return Results.Created($"/api/policies/{created.Id}", created);
        });
        endpoints.MapPut("/api/policies/{id:long}", async (
            long id,
            SoftwarePolicyWriteRequest request,
            IPolicyStore repository,
            CancellationToken cancellationToken) =>
        {
            if (!SoftwarePolicyValidator.TryValidate(request, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            var updated = await repository.UpdatePolicyAsync(id, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });
        endpoints.MapDelete("/api/policies/{id:long}", async (
            long id,
            IPolicyStore repository,
            CancellationToken cancellationToken) =>
            await repository.DeletePolicyAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        return endpoints;
    }
}
