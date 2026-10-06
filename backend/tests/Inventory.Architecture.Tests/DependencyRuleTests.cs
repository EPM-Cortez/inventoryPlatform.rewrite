using System.Reflection;
using Inventory.Api.Controllers;
using Inventory.Application.Assets;
using Inventory.Contracts.V1;
using Inventory.Domain.Assets;
using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Architecture.Tests;

public sealed class DependencyRuleTests
{
    [Fact]
    public void DomainDoesNotReferenceFrameworkOrOuterProjects()
    {
        var references = ReferencedAssemblyNames(typeof(Asset).Assembly);

        Assert.DoesNotContain(
            references,
            name =>
                name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                name.StartsWith("Inventory.", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationDoesNotReferenceApiContractsOrInfrastructure()
    {
        var references = ReferencedAssemblyNames(typeof(AssetService).Assembly);

        Assert.DoesNotContain("Inventory.Api", references);
        Assert.DoesNotContain("Inventory.Contracts", references);
        Assert.DoesNotContain("Inventory.Infrastructure", references);
        Assert.DoesNotContain(
            references,
            name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractsAreIndependentOfDomainAndPersistence()
    {
        var references = ReferencedAssemblyNames(typeof(AssetResponse).Assembly);

        Assert.DoesNotContain("Inventory.Domain", references);
        Assert.DoesNotContain(
            references,
            name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void ControllersDoNotAcceptDbContextDependencies()
    {
        var controllerTypes = typeof(AssetsController).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);

        foreach (var controllerType in controllerTypes)
        {
            var constructorParameters = controllerType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            Assert.DoesNotContain(typeof(InventoryDbContext), constructorParameters);
        }
    }

    [Fact]
    public void EveryControllerEndpointHasAnExplicitAuthorizationDecision()
    {
        var endpointMethods = typeof(AssetsController).Assembly
            .GetTypes()
            .Where(
                type =>
                    typeof(ControllerBase).IsAssignableFrom(type) &&
                    !type.IsAbstract)
            .SelectMany(
                type => type
                    .GetMethods(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.DeclaredOnly)
                    .Where(
                        method => method
                            .GetCustomAttributes<HttpMethodAttribute>(
                                inherit: true)
                            .Any())
                    .Select(method => (Controller: type, Method: method)))
            .ToArray();
        var missingDecisions = endpointMethods
            .Where(
                endpoint =>
                {
                    var allowsAnonymous = endpoint.Method
                        .GetCustomAttributes<AllowAnonymousAttribute>(
                            inherit: true)
                        .Any();
                    var authorizes = endpoint.Method
                            .GetCustomAttributes<AuthorizeAttribute>(
                                inherit: true)
                            .Any() ||
                        endpoint.Controller
                            .GetCustomAttributes<AuthorizeAttribute>(
                                inherit: true)
                            .Any();
                    return !allowsAnonymous && !authorizes;
                })
            .Select(
                endpoint =>
                    $"{endpoint.Controller.Name}.{endpoint.Method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingDecisions.Length == 0,
            "Endpoints relying only on the fallback policy: " +
            string.Join(", ", missingDecisions));
    }

    [Fact]
    public void UnsafeAntiforgeryExemptionsAreAnonymousAndRateLimited()
    {
        var unsafeMethods = new HashSet<string>(
            [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete],
            StringComparer.OrdinalIgnoreCase);
        var offenders = typeof(AssetsController).Assembly
            .GetTypes()
            .Where(
                type =>
                    typeof(ControllerBase).IsAssignableFrom(type) &&
                    !type.IsAbstract)
            .SelectMany(
                type => type
                    .GetMethods(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.DeclaredOnly)
                    .Select(
                        method => new
                        {
                            Controller = type,
                            Method = method,
                            Http = method
                                .GetCustomAttributes<HttpMethodAttribute>(
                                    inherit: true)
                                .ToArray(),
                        }))
            .Where(endpoint =>
                endpoint.Http.SelectMany(attribute => attribute.HttpMethods)
                    .Any(unsafeMethods.Contains))
            .Where(
                endpoint =>
                    endpoint.Method
                        .GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(
                            inherit: true)
                        .Any() ||
                    endpoint.Controller
                        .GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(
                            inherit: true)
                        .Any())
            .Where(
                endpoint =>
                {
                    var anonymous =
                        endpoint.Method
                            .GetCustomAttributes<AllowAnonymousAttribute>(
                                inherit: true)
                            .Any() ||
                        endpoint.Controller
                            .GetCustomAttributes<AllowAnonymousAttribute>(
                                inherit: true)
                            .Any();
                    var rateLimited = endpoint.Method
                            .GetCustomAttributes<EnableRateLimitingAttribute>(
                                inherit: true)
                            .Any() ||
                        endpoint.Controller
                            .GetCustomAttributes<EnableRateLimitingAttribute>(
                                inherit: true)
                            .Any();
                    return !anonymous || !rateLimited;
                })
            .Select(
                endpoint =>
                    $"{endpoint.Controller.Name}.{endpoint.Method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Unsafe antiforgery exemptions must be anonymous signed-machine " +
            "endpoints with rate limiting: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void DomainDoesNotReintroduceTenantOrSchoolKeysOnNormalRows()
    {
        var prohibitedPropertyNames = new HashSet<string>(
            ["TenantId", "SchoolId"],
            StringComparer.Ordinal);
        var offenders = typeof(Asset).Assembly
            .GetTypes()
            .Where(type => type.IsClass && type.Namespace?.StartsWith("Inventory.Domain", StringComparison.Ordinal) == true)
            .SelectMany(
                type => type
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property => prohibitedPropertyNames.Contains(property.Name))
                    .Select(property => $"{type.FullName}.{property.Name}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static HashSet<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
}
