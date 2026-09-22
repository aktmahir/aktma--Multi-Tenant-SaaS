using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Infrastructure;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace MultiTenantSaaS.Api;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", async (LoginRequest request, SignInManager<CatalogUser> signInManager) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Email and password are required." });
            }

            var result = await signInManager.PasswordSignInAsync(
                request.Email,
                request.Password,
                isPersistent: request.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                return Results.Ok(new { authenticated = true });
            }

            if (result.IsLockedOut)
            {
                return Results.StatusCode(StatusCodes.Status423Locked);
            }

            return Results.Unauthorized();
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        endpoints.MapPost("/account/logout", async (SignInManager<CatalogUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = IdentityConstants.ApplicationScheme
        });

        endpoints.MapPost("/account/forgot-password", async (ForgotPasswordRequest request, UserManager<CatalogUser> userManager) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.BadRequest(new { error = "Email is required." });
            }

            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is not null)
            {
                _ = await userManager.GeneratePasswordResetTokenAsync(user);
            }

            return Results.Accepted();
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        endpoints.MapMethods("/connect/authorize", ["GET", "POST"], async (
            HttpContext context,
            UserManager<CatalogUser> userManager,
            ConsentStateProtector consentStateProtector,
            ITenantMembershipService membershipService,
            ITenantContext tenantContext) =>
        {
            var request = OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(context)
                ?? throw new InvalidOperationException("The OpenIddict authorization request is unavailable.");

            var cookieAuthentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (!cookieAuthentication.Succeeded || cookieAuthentication.Principal is null)
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = returnUrl },
                    [IdentityConstants.ApplicationScheme]);
            }

            var user = await userManager.GetUserAsync(cookieAuthentication.Principal);
            if (user is null)
            {
                return Results.Forbid();
            }

            if (!string.Equals(context.Request.Query["consent"], "approved", StringComparison.Ordinal))
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                var consentUrl = QueryHelpers.AddQueryString("/account/consent", new Dictionary<string, string?>
                {
                    ["state"] = consentStateProtector.Protect(returnUrl),
                    ["client_id"] = request.ClientId,
                    ["scope"] = string.Join(" ", request.GetScopes())
                });
                return Results.Redirect(consentUrl);
            }

            if (tenantContext.TenantId is null)
            {
                return Results.BadRequest(new { error = "A valid tenant selection is required for authorization." });
            }

            var tenantRole = await membershipService.GetRoleAsync(user.Id, tenantContext.TenantId.Value, context.RequestAborted);
            if (!TenantTokenClaims.TryCreate(tenantContext.TenantId.Value, tenantRole, out var tenantIdValue, out var emittedRole))
            {
                return Results.Forbid();
            }

            var identity = new ClaimsIdentity(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role);
            identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
            identity.SetClaim(OpenIddictConstants.Claims.Email, user.Email ?? string.Empty);
            identity.SetClaim(OpenIddictConstants.Claims.Name, user.DisplayName ?? user.UserName ?? string.Empty);
            identity.SetClaim("tenant_id", tenantIdValue);
            identity.SetClaim(OpenIddictConstants.Claims.Role, emittedRole);
            identity.SetScopes(request.GetScopes());
            identity.SetResources(request.GetResources());

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        endpoints.MapGet("/account/consent", (
            HttpContext context,
            ConsentStateProtector consentStateProtector) =>
        {
            var state = context.Request.Query["state"].ToString();
            if (string.IsNullOrWhiteSpace(state))
            {
                return Results.BadRequest(new { error = "Consent state is required." });
            }

            try
            {
                _ = consentStateProtector.Unprotect(state);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                return Results.BadRequest(new { error = "Consent state is invalid or expired." });
            }

            var clientId = HtmlEncoder.Default.Encode(context.Request.Query["client_id"].ToString());
            var scopes = HtmlEncoder.Default.Encode(context.Request.Query["scope"].ToString());
            var encodedState = HtmlEncoder.Default.Encode(state);
            var html = $"""
                <!doctype html>
                <html lang="en">
                  <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Authorize Northstar</title></head>
                  <body style="font-family:system-ui,sans-serif;max-width:520px;margin:12vh auto;padding:24px;color:#1f2421;background:#f5f3ef">
                    <main style="background:#fbfaf8;border:1px solid #dedbd4;border-radius:12px;padding:28px">
                      <h1 style="margin-top:0">Authorize {clientId}</h1>
                      <p>This application is requesting access to:</p>
                      <p><strong>{scopes}</strong></p>
                      <form method="post" action="/account/consent">
                        <input type="hidden" name="state" value="{encodedState}">
                        <button name="approved" value="true" type="submit" style="background:#e45d42;color:white;border:0;border-radius:8px;padding:10px 16px;font-weight:600">Allow access</button>
                        <button name="approved" value="false" type="submit" style="margin-left:8px;background:transparent;border:1px solid #dedbd4;border-radius:8px;padding:10px 16px">Deny</button>
                      </form>
                    </main>
                  </body>
                </html>
                """;
            return Results.Content(html, "text/html");
        })
        .RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = IdentityConstants.ApplicationScheme
        });

        endpoints.MapPost("/account/consent", async (HttpContext context, ConsentStateProtector consentStateProtector) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var state = form["state"].ToString();
            var approved = string.Equals(form["approved"].ToString(), "true", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(state))
            {
                return Results.BadRequest(new { error = "Consent state is required." });
            }

            string returnUrl;
            try
            {
                returnUrl = consentStateProtector.Unprotect(state);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                return Results.BadRequest(new { error = "Consent state is invalid or expired." });
            }

            if (!approved)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return Results.Redirect(QueryHelpers.AddQueryString(returnUrl, "consent", "approved"));
        })
        .RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = IdentityConstants.ApplicationScheme
        })
        .RequireRateLimiting("auth");

        endpoints.MapPost("/connect/token", async (HttpContext context) =>
        {
            var request = OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(context)
                ?? throw new InvalidOperationException("The OpenIddict token request is unavailable.");

            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            {
                return Results.BadRequest(new { error = OpenIddictConstants.Errors.UnsupportedGrantType });
            }

            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (!result.Succeeded || result.Principal is null)
            {
                return Results.Forbid();
            }

            return Results.SignIn(
                result.Principal,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth");

        return endpoints;
    }
}

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
public sealed record ForgotPasswordRequest(string Email);
