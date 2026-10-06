using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class MfaApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopiedPendingMfaCookieCannotCreateASecondSession(
        bool useRecoveryCode)
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            enforceMfa: true);
        using var owner = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var begin = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment",
            new { currentPassword = InventoryApiFactory.AdministratorPassword });
        begin.EnsureSuccessStatusCode();
        var enrollment = await begin.Content.ReadFromJsonAsync<MfaEnrollmentResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(enrollment);
        await RefreshCsrfAsync(owner);
        var confirm = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment/confirm",
            new { code = GenerateTotp(enrollment.SharedKey) });
        confirm.EnsureSuccessStatusCode();
        var recovery = await confirm.Content.ReadFromJsonAsync<MfaRecoveryCodesResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(recovery);

        var (cookie, csrf) = await BeginPortableMfaLoginAsync(factory);
        using var first = CreatePortableClient(factory, cookie, csrf);
        using var copied = CreatePortableClient(factory, cookie, csrf);
        var firstCompletion = useRecoveryCode
            ? await first.PostAsJsonAsync(
                "/api/v1/auth/login/mfa/recovery",
                new { recoveryCode = recovery.RecoveryCodes[0], rememberMe = false })
            : await first.PostAsJsonAsync(
                "/api/v1/auth/login/mfa",
                new
                {
                    code = GenerateTotp(enrollment.SharedKey),
                    rememberMe = false,
                    rememberMachine = false,
                });
        Assert.Equal(HttpStatusCode.NoContent, firstCompletion.StatusCode);

        var copiedCompletion = useRecoveryCode
            ? await copied.PostAsJsonAsync(
                "/api/v1/auth/login/mfa/recovery",
                new { recoveryCode = recovery.RecoveryCodes[1], rememberMe = false })
            : await copied.PostAsJsonAsync(
                "/api/v1/auth/login/mfa",
                new
                {
                    code = GenerateTotp(enrollment.SharedKey),
                    rememberMe = false,
                    rememberMachine = false,
                });
        Assert.Equal(HttpStatusCode.Unauthorized, copiedCompletion.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await copied.GetAsync("/api/v1/assets")).StatusCode);

        if (useRecoveryCode)
        {
            using var fresh = await BeginMfaLoginAsync(factory);
            var stillUsable = await fresh.PostAsJsonAsync(
                "/api/v1/auth/login/mfa/recovery",
                new { recoveryCode = recovery.RecoveryCodes[1], rememberMe = false });
            Assert.Equal(HttpStatusCode.NoContent, stillUsable.StatusCode);
        }
    }

    [Fact]
    public async Task PendingMfaCookieExpiresAfterFiveMinutes()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            enforceMfa: true,
            utcNow: new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        using var owner = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var begin = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment",
            new { currentPassword = InventoryApiFactory.AdministratorPassword });
        begin.EnsureSuccessStatusCode();
        var enrollment = await begin.Content.ReadFromJsonAsync<MfaEnrollmentResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(enrollment);
        await RefreshCsrfAsync(owner);
        using var confirm = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment/confirm",
            new { code = GenerateTotp(enrollment.SharedKey) });
        confirm.EnsureSuccessStatusCode();

        var (cookie, csrf) = await BeginPortableMfaLoginAsync(factory);
        using var pending = CreatePortableClient(factory, cookie, csrf);
        factory.AdvanceClock(TimeSpan.FromMinutes(6));
        using var completion = await pending.PostAsJsonAsync(
            "/api/v1/auth/login/mfa",
            new
            {
                code = GenerateTotp(enrollment.SharedKey),
                rememberMe = false,
                rememberMachine = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, completion.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await pending.GetAsync("/api/v1/assets")).StatusCode);
    }

    [Fact]
    public async Task MfaSelfServiceHasSharedPerUserAttemptBudget()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            mfaSelfServicePermitLimit: 2);
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var technician = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await administrator.PostAsJsonAsync(
                "/api/v1/auth/mfa/enrollment",
                new { currentPassword = "wrong" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await administrator.PostAsJsonAsync(
                "/api/v1/auth/mfa/enrollment/confirm",
                new { code = "000000" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await administrator.PostAsJsonAsync(
                "/api/v1/auth/mfa/recovery-codes",
                new { currentPassword = "wrong", code = "000000" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await technician.PostAsJsonAsync(
                "/api/v1/auth/mfa/enrollment",
                new { currentPassword = "wrong" })).StatusCode);
    }

    [Fact]
    public async Task RequiredAdministratorCanEnrollUseRecoveryAndIsChallengedOnNextLogin()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            enforceMfa: true);
        using var enrollmentClient = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await enrollmentClient.GetAsync("/api/v1/assets")).StatusCode);
        var before = await enrollmentClient.GetFromJsonAsync<CurrentUserResponse>(
            "/api/v1/auth/me",
            ApiTestClient.JsonOptions);
        Assert.NotNull(before);
        Assert.True(before.MfaEnrollmentRequired);

        var invalidBegin = await enrollmentClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment",
            new { currentPassword = "Wrong-Administrator-Password!42" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidBegin.StatusCode);

        var beginResponse = await enrollmentClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment",
            new { currentPassword = InventoryApiFactory.AdministratorPassword });
        beginResponse.EnsureSuccessStatusCode();
        Assert.True(beginResponse.Headers.CacheControl?.NoStore);
        var enrollment = await beginResponse.Content.ReadFromJsonAsync<MfaEnrollmentResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(enrollment);
        Assert.NotEmpty(enrollment.SharedKey);
        Assert.StartsWith("otpauth://totp/", enrollment.AuthenticatorUri, StringComparison.Ordinal);

        await RefreshCsrfAsync(enrollmentClient);
        var authenticatorCode = GenerateTotp(enrollment.SharedKey);
        var confirmResponse = await enrollmentClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment/confirm",
            new { code = authenticatorCode });
        Assert.True(
            confirmResponse.IsSuccessStatusCode,
            await confirmResponse.Content.ReadAsStringAsync());
        var confirmed = await confirmResponse.Content
            .ReadFromJsonAsync<MfaRecoveryCodesResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(confirmed);
        Assert.Equal(10, confirmed.RecoveryCodesRemaining);
        Assert.Equal(10, confirmed.RecoveryCodes.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            HttpStatusCode.OK,
            (await enrollmentClient.GetAsync("/api/v1/assets")).StatusCode);
        var status = await enrollmentClient.GetFromJsonAsync<MfaStatusResponse>(
            "/api/v1/auth/mfa",
            ApiTestClient.JsonOptions);
        Assert.NotNull(status);
        Assert.True(status.Enabled);
        Assert.False(status.EnrollmentRequired);

        var storedTokens = await factory.GetUserTokenValuesAsync(
            InventoryApiFactory.AdministratorEmail);
        Assert.DoesNotContain(
            storedTokens,
            stored => confirmed.RecoveryCodes.Any(
                code => stored.Contains(code, StringComparison.Ordinal)));
        var auditDetails = await factory.GetAuditDetailsAsync(
            "user.mfa.enrollment.confirmed");
        Assert.DoesNotContain(
            auditDetails,
            details => confirmed.RecoveryCodes.Any(
                code => details.Contains(code, StringComparison.Ordinal)));

        await RefreshCsrfAsync(enrollmentClient);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await enrollmentClient.PostAsync("/api/v1/auth/logout", content: null)).StatusCode);

        using var mfaClient = await BeginMfaLoginAsync(factory);
        var loginCode = GenerateTotp(enrollment.SharedKey);
        var mfaResponse = await mfaClient.PostAsJsonAsync(
            "/api/v1/auth/login/mfa",
            new
            {
                code = loginCode,
                rememberMe = false,
                rememberMachine = false,
            });
        Assert.Equal(HttpStatusCode.NoContent, mfaResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await mfaClient.GetAsync("/api/v1/assets")).StatusCode);

        await RefreshCsrfAsync(mfaClient);
        var regenerationCode = GenerateTotp(enrollment.SharedKey);
        var regenerateResponse = await mfaClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/recovery-codes",
            new
            {
                currentPassword = InventoryApiFactory.AdministratorPassword,
                code = regenerationCode,
            });
        regenerateResponse.EnsureSuccessStatusCode();
        var regenerated = await regenerateResponse.Content
            .ReadFromJsonAsync<MfaRecoveryCodesResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(regenerated);

        await RefreshCsrfAsync(mfaClient);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await mfaClient.PostAsync("/api/v1/auth/logout", content: null)).StatusCode);
        using var recoveryClient = await BeginMfaLoginAsync(factory);
        var recoveryResponse = await recoveryClient.PostAsJsonAsync(
            "/api/v1/auth/login/mfa/recovery",
            new
            {
                recoveryCode = regenerated.RecoveryCodes[0],
                rememberMe = false,
            });
        Assert.Equal(HttpStatusCode.NoContent, recoveryResponse.StatusCode);
        var afterRecovery = await recoveryClient.GetFromJsonAsync<MfaStatusResponse>(
            "/api/v1/auth/mfa",
            ApiTestClient.JsonOptions);
        Assert.NotNull(afterRecovery);
        Assert.Equal(9, afterRecovery.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task AdministratorResetDisablesMfaInvalidatesTheSessionAndRequiresReenrollment()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            enforceMfa: true);
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        _ = await EnrollAsync(client);

        var users = await client.GetFromJsonAsync<PagedResponse<UserResponse>>(
            "/api/v1/users?page=1&pageSize=200",
            ApiTestClient.JsonOptions);
        var administrator = Assert.Single(
            users!.Items,
            user => user.Email == InventoryApiFactory.AdministratorEmail);
        var reset = await client.PostAsJsonAsync(
            $"/api/v1/users/{administrator.Id}/reset-mfa",
            new { version = administrator.Version });
        reset.EnsureSuccessStatusCode();
        var resetUser = await reset.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(resetUser);
        Assert.False(resetUser.MfaEnabled);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(1, await factory.CountAuditActionsAsync("user.mfa.reset"));

        using var reenrollment = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var me = await reenrollment.GetFromJsonAsync<CurrentUserResponse>(
            "/api/v1/auth/me",
            ApiTestClient.JsonOptions);
        Assert.NotNull(me);
        Assert.True(me.MfaEnrollmentRequired);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reenrollment.GetAsync("/api/v1/users")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordChangeInvalidatesPendingMfaLogin(bool useRecoveryCode)
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            enforceMfa: true);
        using var owner = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var begin = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment",
            new { currentPassword = InventoryApiFactory.AdministratorPassword });
        begin.EnsureSuccessStatusCode();
        var enrollment = await begin.Content.ReadFromJsonAsync<MfaEnrollmentResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(enrollment);
        await RefreshCsrfAsync(owner);
        var confirm = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment/confirm",
            new { code = GenerateTotp(enrollment.SharedKey) });
        confirm.EnsureSuccessStatusCode();
        var recovery = await confirm.Content.ReadFromJsonAsync<MfaRecoveryCodesResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(recovery);

        using var pending = await BeginMfaLoginAsync(factory);
        const string replacementPassword = "Replacement-Administrator-Password!83";
        await RefreshCsrfAsync(owner);
        var changed = await owner.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new
            {
                currentPassword = InventoryApiFactory.AdministratorPassword,
                newPassword = replacementPassword,
            });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        var completion = useRecoveryCode
            ? await pending.PostAsJsonAsync(
                "/api/v1/auth/login/mfa/recovery",
                new
                {
                    recoveryCode = recovery.RecoveryCodes[0],
                    rememberMe = false,
                })
            : await pending.PostAsJsonAsync(
                "/api/v1/auth/login/mfa",
                new
                {
                    code = GenerateTotp(enrollment.SharedKey),
                    rememberMe = false,
                    rememberMachine = false,
                });
        Assert.Equal(HttpStatusCode.Unauthorized, completion.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await pending.GetAsync("/api/v1/assets")).StatusCode);

        using var current = factory.CreateHttpsClient();
        await RefreshCsrfAsync(current);
        var currentLogin = await current.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = replacementPassword,
                rememberMe = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, currentLogin.StatusCode);
        Assert.Contains(
            "mfa_required",
            await currentLogin.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        var currentCompletion = await current.PostAsJsonAsync(
            "/api/v1/auth/login/mfa",
            new
            {
                code = GenerateTotp(enrollment.SharedKey),
                rememberMe = false,
                rememberMachine = false,
            });
        Assert.Equal(HttpStatusCode.NoContent, currentCompletion.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await current.GetAsync("/api/v1/assets")).StatusCode);
    }

    [Fact]
    public async Task RecoveryLoginCannotSpendACodeAfterFailedAttemptsLockTheAccount()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            enforceMfa: true);
        using var owner = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var begin = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment",
            new { currentPassword = InventoryApiFactory.AdministratorPassword });
        begin.EnsureSuccessStatusCode();
        var enrollment = await begin.Content.ReadFromJsonAsync<MfaEnrollmentResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(enrollment);
        await RefreshCsrfAsync(owner);
        var confirm = await owner.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment/confirm",
            new { code = GenerateTotp(enrollment.SharedKey) });
        confirm.EnsureSuccessStatusCode();
        var recovery = await confirm.Content.ReadFromJsonAsync<MfaRecoveryCodesResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(recovery);

        using var pending = await BeginMfaLoginAsync(factory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await pending.PostAsJsonAsync(
                "/api/v1/auth/login/mfa/recovery",
                new { recoveryCode = "invalid-recovery-code", rememberMe = false });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var blocked = await pending.PostAsJsonAsync(
            "/api/v1/auth/login/mfa/recovery",
            new { recoveryCode = recovery.RecoveryCodes[0], rememberMe = false });
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        Assert.Contains(
            "locked_out",
            await blocked.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await pending.GetAsync("/api/v1/assets")).StatusCode);
    }

    [Fact]
    public async Task NonRequiredTechnicianIsNotBlockedByAdministratorMfaPolicy()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            enforceMfa: true);
        using var technician = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);

        var me = await technician.GetFromJsonAsync<CurrentUserResponse>(
            "/api/v1/auth/me",
            ApiTestClient.JsonOptions);
        Assert.NotNull(me);
        Assert.False(me.MfaEnrollmentRequired);
        Assert.Equal(
            HttpStatusCode.OK,
            (await technician.GetAsync("/api/v1/assets")).StatusCode);
    }

    private static async Task<HttpClient> BeginMfaLoginAsync(InventoryApiFactory factory)
    {
        var client = factory.CreateHttpsClient();
        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = InventoryApiFactory.AdministratorPassword,
                rememberMe = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return client;
    }

    private static async Task<(string Cookie, string Csrf)> BeginPortableMfaLoginAsync(
        InventoryApiFactory factory)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        var csrfResponse = await client.GetAsync("/api/v1/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(csrf);
        var csrfCookie = CookiePairs(csrfResponse);

        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = InventoryApiFactory.AdministratorPassword,
                rememberMe = false,
            }),
        };
        loginRequest.Headers.Add("Cookie", csrfCookie);
        loginRequest.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        return ($"{csrfCookie}; {CookiePairs(login)}", csrf.Token);
    }

    private static HttpClient CreatePortableClient(
        InventoryApiFactory factory,
        string cookie,
        string csrf)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        return client;
    }

    private static string CookiePairs(HttpResponseMessage response) =>
        string.Join(
            "; ",
            response.Headers.GetValues("Set-Cookie")
                .Select(value => value.Split(';', 2)[0]));

    private static async Task<string> EnrollAsync(HttpClient client)
    {
        var begin = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment",
            new { currentPassword = InventoryApiFactory.AdministratorPassword });
        begin.EnsureSuccessStatusCode();
        var enrollment = await begin.Content.ReadFromJsonAsync<MfaEnrollmentResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(enrollment);
        await RefreshCsrfAsync(client);
        var code = GenerateTotp(enrollment.SharedKey);
        var confirm = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrollment/confirm",
            new { code });
        Assert.True(
            confirm.IsSuccessStatusCode,
            await confirm.Content.ReadAsStringAsync());
        return enrollment.SharedKey;
    }

    private static async Task RefreshCsrfAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
    }

    private static string GenerateTotp(string sharedKey)
    {
        var key = DecodeBase32(sharedKey);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(
            counterBytes,
            counter);
#pragma warning disable CA5350 // ASP.NET Core Identity authenticator tokens use RFC 6238 HMAC-SHA1.
        using var hmac = new HMACSHA1(key);
#pragma warning restore CA5350
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);
        return (binaryCode % 1_000_000).ToString(
            "D6",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0;
        var bitsInBuffer = 0;
        foreach (var character in value
                     .Replace(" ", string.Empty, StringComparison.Ordinal)
                     .TrimEnd('=')
                     .ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException("Authenticator key is not valid Base32.");
            }

            buffer = (buffer << 5) | index;
            bitsInBuffer += 5;
            if (bitsInBuffer < 8)
            {
                continue;
            }

            bitsInBuffer -= 8;
            output.Add((byte)((buffer >> bitsInBuffer) & 0xFF));
        }

        return output.ToArray();
    }
}
