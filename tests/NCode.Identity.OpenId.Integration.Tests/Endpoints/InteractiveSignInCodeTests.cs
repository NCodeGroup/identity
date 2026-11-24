// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Host;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

// A tenant that requires the email second factor signs an end user in only after the code mailed to the account's
// address is right (ADR-1533).
public class InteractiveSignInCodeTests
{
    private const string UserName = "code-user";
    private const string Password = "code-user-password-1";
    private const string Email = "code-user@example.test";
    private const string ClientId = "it-sign-in-code-client";
    private const string ClientSecret = "sign-in-code-client-secret";
    private const string ReturnUrl = "/foo";
    private const string LoginPath = "/account/login";
    private const string LoginCodePath = "/account/login/code";
    private const string MePath = "/account/me";
    private const string PendingCookieName = "__Host-NCode.Identity.Interactive.PendingSignIn";
    private const string AntiforgeryRequestCookieName = "__Host-XSRF-TOKEN";
    private const string AntiforgeryFieldName = "__RequestVerificationToken";
    private const string AntiforgeryCookiePrefix = "__Host-NCode.Identity.Antiforgery";

    private static TimeSpan MailTimeout { get; } = TimeSpan.FromSeconds(15);

    #region Scaffolding

    // The tenant reads its settings at its first request, so this is always the first call on a factory.
    private static ValueTask SeedSecondFactorRequiredAsync(
        HostApplicationFactory factory,
        CancellationToken cancellationToken
    ) =>
        factory.SeedTenantSettingsAsync(
            new Dictionary<string, object> { [OpenIdSettingNames.EmailMfaRequired] = true },
            cancellationToken
        );

    private static Action<IWebHostBuilder> UseTransport(CapturingMailTransport transport) =>
        builder =>
            builder.ConfigureTestServices(serviceCollection =>
                serviceCollection.Replace(ServiceDescriptor.Singleton<IMailTransport>(transport))
            );

    private static HttpClient CreateClient(WebApplicationFactory<HostApiMarker> factory) =>
        factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            }
        );

    private static Uri ReadLocation(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return new Uri(new Uri("http://localhost"), response.Headers.Location.OriginalString);
    }

    // Reads the cookies a response sets as name and raw value pairs.
    private static List<(string Name, string Value)> ReadCookies(HttpResponseMessage response)
    {
        List<(string Name, string Value)> cookies = [];
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            return cookies;

        foreach (var setCookie in setCookies)
        {
            var pair = setCookie.Split(';')[0];
            var separator = pair.IndexOf('=');
            if (separator > 0)
                cookies.Add((pair[..separator], pair[(separator + 1)..]));
        }

        return cookies;
    }

    // Reads the raw value of a cookie a response sets, or null when it sets none or clears it.
    private static string? ReadCookie(HttpResponseMessage response, string name) =>
        ReadCookies(response)
            .Where(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal))
            .Select(cookie => cookie.Value)
            .FirstOrDefault(value => value.Length > 0);

    // Posts the password form, and returns where it redirects with the pending sign-in and session cookies it sets.
    private static async ValueTask<(
        Uri Location,
        string? PendingCookie,
        string? SessionCookie
    )> SubmitPasswordAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await InteractiveLogin.SubmitLoginAsync(
            client,
            UserName,
            Password,
            ReturnUrl,
            cancellationToken
        );
        return (
            ReadLocation(response),
            ReadCookie(response, PendingCookieName),
            ReadCookie(response, InteractiveLogin.SessionCookieName)
        );
    }

    // Posts a form of the sign-in code page the way a browser does: the login page supplies the antiforgery cookies,
    // and the pending sign-in cookie travels when there is one.
    private static async ValueTask<HttpResponseMessage> PostCodeFormAsync(
        HttpClient client,
        string? pendingCookie,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken
    )
    {
        using var page = await client.GetAsync(LoginPath, cancellationToken);
        var pageCookies = ReadCookies(page);
        var requestToken = pageCookies.Single(cookie =>
            cookie.Name == AntiforgeryRequestCookieName
        );
        var cookieToken = pageCookies.Single(cookie =>
            cookie.Name.StartsWith(AntiforgeryCookiePrefix, StringComparison.Ordinal)
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, LoginCodePath);
        List<string> cookies =
        [
            $"{cookieToken.Name}={cookieToken.Value}",
            $"{requestToken.Name}={requestToken.Value}",
        ];
        if (pendingCookie is not null)
            cookies.Add($"{PendingCookieName}={pendingCookie}");
        request.Headers.Add("Cookie", string.Join("; ", cookies));

        var form = fields.ToDictionary(field => field.Key, field => field.Value);
        form[AntiforgeryFieldName] = Uri.UnescapeDataString(requestToken.Value);
        request.Content = new FormUrlEncodedContent(form);
        return await client.SendAsync(request, cancellationToken);
    }

    private static ValueTask<HttpResponseMessage> PostCodeAsync(
        HttpClient client,
        string? pendingCookie,
        string code,
        CancellationToken cancellationToken
    ) =>
        PostCodeFormAsync(
            client,
            pendingCookie,
            new Dictionary<string, string> { ["code"] = code, ["action"] = "verify" },
            cancellationToken
        );

    private static async ValueTask<string> ReadMailedCodeAsync(
        CapturingMailTransport transport,
        CancellationToken cancellationToken
    )
    {
        var message = await transport.WaitForMessageToAsync(Email, MailTimeout, cancellationToken);
        return CapturingMailTransport.ReadCode(message);
    }

    private static async ValueTask<JsonElement> GetMeAsync(
        HttpClient client,
        string sessionCookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        request.Headers.Add("Cookie", $"{InteractiveLogin.SessionCookieName}={sessionCookie}");
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonElement.Parse(body);
    }

    private static async ValueTask<bool> IsEmailVerifiedAsync(
        HostApplicationFactory factory,
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        using var scope = factory.CreateTenantScope();
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var store = await HostApplicationFactory.GetLocalAccountStoreAsync(
            scope.ServiceProvider,
            storeManager,
            cancellationToken
        );
        var account = await store.GetByIdOrDefaultAsync(localAccountId, cancellationToken);
        Assert.NotNull(account);
        return account.EmailVerified;
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task PostLogin_WhenTheSecondFactorIsRequired_RedirectsToTheCodePageWithoutASession()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);

        var (location, pendingCookie, sessionCookie) = await SubmitPasswordAsync(
            client,
            CancellationToken.None
        );

        Assert.Equal(LoginCodePath, location.AbsolutePath);
        Assert.NotNull(pendingCookie);
        Assert.Null(sessionCookie);
        var code = await ReadMailedCodeAsync(transport, CancellationToken.None);
        Assert.Equal(6, code.Length);

        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, LoginCodePath);
        pageRequest.Headers.Add("Cookie", $"{PendingCookieName}={pendingCookie}");
        using var pageResponse = await client.SendAsync(pageRequest, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var body = await pageResponse.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Contains("id=\"verify\"", body);
    }

    [Fact]
    public async Task PostLogin_WhenTheConnectionRequiresTheSecondFactor_RedirectsToTheCodePageWithoutASession()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await factory.SeedConnectionSettingsAsync(
            ConnectionDefaults.Name,
            new Dictionary<string, object> { [OpenIdSettingNames.EmailMfaRequired] = true },
            CancellationToken.None
        );
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);

        var (location, pendingCookie, sessionCookie) = await SubmitPasswordAsync(
            client,
            CancellationToken.None
        );

        Assert.Equal(LoginCodePath, location.AbsolutePath);
        Assert.NotNull(pendingCookie);
        Assert.Null(sessionCookie);
    }

    [Fact]
    public async Task PostLogin_WhenNeitherTheTenantNorTheConnectionRequiresTheSecondFactor_SignsIn()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await factory.SeedConnectionSettingsAsync(
            ConnectionDefaults.Name,
            new Dictionary<string, object> { [OpenIdSettingNames.EmailMfaRequired] = false },
            CancellationToken.None
        );
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);

        var (location, pendingCookie, sessionCookie) = await SubmitPasswordAsync(
            client,
            CancellationToken.None
        );

        Assert.Equal(ReturnUrl, location.AbsolutePath);
        Assert.Null(pendingCookie);
        Assert.NotNull(sessionCookie);
        Assert.Empty(transport.Delivered);
    }

    [Fact]
    public async Task PostLogin_WhenTheSecondFactorIsRequiredAndTheAccountHasNoAddress_RefusesTheSignIn()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(UserName, Password, CancellationToken.None);
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);

        var (location, pendingCookie, sessionCookie) = await SubmitPasswordAsync(
            client,
            CancellationToken.None
        );

        Assert.Equal(LoginPath, location.AbsolutePath);
        Assert.Equal("access_denied", HttpUtility.ParseQueryString(location.Query)["error"]);
        Assert.Null(pendingCookie);
        Assert.Null(sessionCookie);
        Assert.Empty(transport.Delivered);
    }

    [Fact]
    public async Task PostLogin_WhenTheTenantAllowsNoMoreCodes_RedirectsToTheLoginPageWithTooManyCodes()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await factory.SeedTenantSettingsAsync(
            new Dictionary<string, object>
            {
                [OpenIdSettingNames.EmailMfaRequired] = true,
                [OpenIdSettingNames.EmailCodeSendsPerHour] = 1,
            },
            CancellationToken.None
        );
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (firstLocation, _, _) = await SubmitPasswordAsync(client, CancellationToken.None);
        Assert.Equal(LoginCodePath, firstLocation.AbsolutePath);
        await ReadMailedCodeAsync(transport, CancellationToken.None);

        var (location, pendingCookie, sessionCookie) = await SubmitPasswordAsync(
            client,
            CancellationToken.None
        );

        Assert.Equal(LoginPath, location.AbsolutePath);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("too_many_codes", query["error"]);
        Assert.Null(pendingCookie);
        Assert.Null(sessionCookie);
        Assert.Single(transport.Delivered);
    }

    #endregion

    #region Login Code Tests

    [Fact]
    public async Task PostLoginCode_WithTheMailedCode_SignsInWithOtpAndMfa()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (_, pendingCookie, _) = await SubmitPasswordAsync(client, CancellationToken.None);
        var code = await ReadMailedCodeAsync(transport, CancellationToken.None);

        using var response = await PostCodeAsync(
            client,
            pendingCookie,
            code,
            CancellationToken.None
        );

        var location = ReadLocation(response);
        Assert.Equal(ReturnUrl, location.AbsolutePath);
        var sessionCookie = ReadCookie(response, InteractiveLogin.SessionCookieName);
        Assert.NotNull(sessionCookie);
        var me = await GetMeAsync(client, sessionCookie, CancellationToken.None);
        var amr = me.GetProperty("amr").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("pwd", amr);
        Assert.Contains("otp", amr);
        Assert.Contains("mfa", amr);
    }

    [Fact]
    public async Task PostLoginCode_WithTheMailedCode_MarksAnUnverifiedAddressVerified()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        var localAccountId = await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: false,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (_, pendingCookie, _) = await SubmitPasswordAsync(client, CancellationToken.None);
        var code = await ReadMailedCodeAsync(transport, CancellationToken.None);
        Assert.False(await IsEmailVerifiedAsync(factory, localAccountId, CancellationToken.None));

        using var response = await PostCodeAsync(
            client,
            pendingCookie,
            code,
            CancellationToken.None
        );

        Assert.NotNull(ReadCookie(response, InteractiveLogin.SessionCookieName));
        Assert.True(await IsEmailVerifiedAsync(factory, localAccountId, CancellationToken.None));
    }

    [Fact]
    public async Task PostLoginCode_WithAWrongCode_RedirectsWithInvalidCode()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (_, pendingCookie, _) = await SubmitPasswordAsync(client, CancellationToken.None);
        var code = await ReadMailedCodeAsync(transport, CancellationToken.None);
        var wrongCode = code == "000000" ? "000001" : "000000";

        using var response = await PostCodeAsync(
            client,
            pendingCookie,
            wrongCode,
            CancellationToken.None
        );

        var location = ReadLocation(response);
        Assert.Equal(LoginCodePath, location.AbsolutePath);
        Assert.Equal("invalid_code", HttpUtility.ParseQueryString(location.Query)["error"]);
        Assert.Null(ReadCookie(response, InteractiveLogin.SessionCookieName));
    }

    [Fact]
    public async Task PostSignInCode_WithoutTheAntiforgeryToken_IsRefusedAndCreatesNoSession()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (_, pendingCookie, _) = await SubmitPasswordAsync(client, CancellationToken.None);
        var code = await ReadMailedCodeAsync(transport, CancellationToken.None);
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginCodePath);
        request.Headers.Add("Cookie", $"{PendingCookieName}={pendingCookie}");
        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string> { ["code"] = code, ["action"] = "verify" }
        );

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(ReadCookie(response, InteractiveLogin.SessionCookieName));
    }

    [Fact]
    public async Task PostLoginCode_WithoutAPendingSignIn_RedirectsToTheLoginPage()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);

        using var response = await PostCodeAsync(
            client,
            pendingCookie: null,
            "123456",
            CancellationToken.None
        );

        var location = ReadLocation(response);
        Assert.Equal(LoginPath, location.AbsolutePath);
        Assert.Null(ReadCookie(response, InteractiveLogin.SessionCookieName));
    }

    [Fact]
    public async Task PostLoginCode_Resend_MailsANewCode()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (_, pendingCookie, _) = await SubmitPasswordAsync(client, CancellationToken.None);
        await ReadMailedCodeAsync(transport, CancellationToken.None);

        using var resendResponse = await PostCodeFormAsync(
            client,
            pendingCookie,
            new Dictionary<string, string> { ["action"] = "resend" },
            CancellationToken.None
        );

        var location = ReadLocation(resendResponse);
        Assert.Equal(LoginCodePath, location.AbsolutePath);
        Assert.Equal("sent", HttpUtility.ParseQueryString(location.Query)["status"]);
        var renewedPendingCookie = ReadCookie(resendResponse, PendingCookieName);
        Assert.NotNull(renewedPendingCookie);

        // The renewed pending sign-in carries the new flow, so only the new code completes it.
        var newCode = await ReadNewestMailedCodeAsync(transport, 2, CancellationToken.None);
        using var verifyResponse = await PostCodeAsync(
            client,
            renewedPendingCookie,
            newCode,
            CancellationToken.None
        );
        Assert.NotNull(ReadCookie(verifyResponse, InteractiveLogin.SessionCookieName));
    }

    // Waits until the transport has delivered the given number of messages to the account, and reads the code of the
    // latest one.
    private static async ValueTask<string> ReadNewestMailedCodeAsync(
        CapturingMailTransport transport,
        int count,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTimeOffset.UtcNow + MailTimeout;
        while (true)
        {
            var messages = transport
                .Delivered.Where(message =>
                    string.Equals(message.To, Email, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (messages.Count >= count)
                return CapturingMailTransport.ReadCode(messages[^1]);

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException($"Fewer than {count} messages were delivered.");

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    #endregion

    #region Password Grant Tests

    [Fact]
    public async Task PostToken_PasswordGrant_WhenTheSecondFactorIsRequired_IsRefused()
    {
        using var factory = new HostApplicationFactory();
        await SeedSecondFactorRequiredAsync(factory, CancellationToken.None);
        await factory.SeedConfidentialClientAsync(
            ClientId,
            ClientSecret,
            [OpenIdProtocol.GrantTypes.Password],
            CancellationToken.None
        );
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        var client = CreateClient(factory);
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = OpenIdProtocol.GrantTypes.Password,
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["username"] = UserName,
                ["password"] = Password,
                ["scope"] = OpenIdProtocol.ScopeTypes.OpenId,
            }
        );

        using var response = await client.PostAsync(
            "/oauth2/token",
            content,
            CancellationToken.None
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.True(JsonElement.Parse(body).TryGetProperty("error", out _), body);
    }

    #endregion
}
