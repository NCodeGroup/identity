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
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Host;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

// An end user who is signed in verifies the address of the account with a code mailed to it (ADR-1533).
public class InteractiveEmailVerificationTests
{
    private const string UserName = "verify-user";
    private const string Password = "verify-user-password-1";
    private const string Email = "verify-user@example.test";
    private const string MePath = "/account/me";
    private const string EmailPagePath = "/account/email";
    private const string EmailStatusPath = "/account/email/status";
    private const string EmailSendPath = "/account/email/send";
    private const string EmailVerifyPath = "/account/email/verify";

    private static TimeSpan MailTimeout { get; } = TimeSpan.FromSeconds(15);

    #region Scaffolding

    // The tenant reads its settings at its first request, so this is always the first call on a factory.
    private static ValueTask SeedVerificationEnabledAsync(
        HostApplicationFactory factory,
        CancellationToken cancellationToken
    ) =>
        factory.SeedTenantSettingsAsync(
            new Dictionary<string, object> { [OpenIdSettingNames.EmailVerificationEnabled] = true },
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

    private static async ValueTask<HttpResponseMessage> GetWithSessionAsync(
        HttpClient client,
        string path,
        string sessionCookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{InteractiveLogin.SessionCookieName}={sessionCookie}");
        return await client.SendAsync(request, cancellationToken);
    }

    private static async ValueTask<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonElement.Parse(body);
    }

    // Posts a form of the email page the way a browser does.
    private static ValueTask<HttpResponseMessage> PostEmailFormAsync(
        HttpClient client,
        string sessionCookie,
        string actionPath,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken
    ) =>
        InteractiveLogin.PostFormAsync(
            client,
            EmailPagePath,
            actionPath,
            sessionCookie,
            fields,
            cancellationToken
        );

    // Asks for a verification code and returns the handle of its flow.
    private static async ValueTask<string> RequestCodeAsync(
        HttpClient client,
        string sessionCookie,
        CancellationToken cancellationToken
    )
    {
        using var response = await PostEmailFormAsync(
            client,
            sessionCookie,
            EmailSendPath,
            new Dictionary<string, string>(),
            cancellationToken
        );
        var location = ReadLocation(response);
        Assert.Equal(EmailPagePath, location.AbsolutePath);
        var flowId = HttpUtility.ParseQueryString(location.Query)["flow"];
        Assert.False(string.IsNullOrEmpty(flowId), location.ToString());
        return flowId;
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
        var account = await storeManager
            .GetStore<ILocalAccountStore>()
            .GetByIdOrDefaultAsync(localAccountId, cancellationToken);
        Assert.NotNull(account);
        return account.EmailVerified;
    }

    #endregion

    #region Status Tests

    [Fact]
    public async Task GetEmailStatus_WhenVerificationIsDisabled_ReturnsNotFound()
    {
        using var factory = new HostApplicationFactory();
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: false,
            CancellationToken.None
        );
        var client = CreateClient(factory);
        var sessionCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            Password,
            CancellationToken.None
        );

        using var response = await GetWithSessionAsync(
            client,
            EmailStatusPath,
            sessionCookie,
            CancellationToken.None
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEmailStatus_ForAnUnverifiedAddress_ReportsIt()
    {
        using var factory = new HostApplicationFactory();
        await SeedVerificationEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: false,
            CancellationToken.None
        );
        var client = CreateClient(factory);
        var sessionCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            Password,
            CancellationToken.None
        );

        using var response = await GetWithSessionAsync(
            client,
            EmailStatusPath,
            sessionCookie,
            CancellationToken.None
        );

        var status = await ReadJsonAsync(response, CancellationToken.None);
        Assert.Equal(Email, status.GetProperty("email").GetString());
        Assert.False(status.GetProperty("verified").GetBoolean());
    }

    #endregion

    #region Verify Tests

    [Fact]
    public async Task PostEmailVerify_WithTheMailedCode_MarksTheAddressVerified()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedVerificationEnabledAsync(factory, CancellationToken.None);
        var localAccountId = await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: false,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var sessionCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            Password,
            CancellationToken.None
        );
        var flowId = await RequestCodeAsync(client, sessionCookie, CancellationToken.None);
        var message = await transport.WaitForMessageToAsync(
            Email,
            MailTimeout,
            CancellationToken.None
        );
        var code = CapturingMailTransport.ReadCode(message);
        Assert.False(await IsEmailVerifiedAsync(factory, localAccountId, CancellationToken.None));

        using var response = await PostEmailFormAsync(
            client,
            sessionCookie,
            EmailVerifyPath,
            new Dictionary<string, string> { ["flow"] = flowId, ["code"] = code },
            CancellationToken.None
        );

        var location = ReadLocation(response);
        Assert.Equal(EmailPagePath, location.AbsolutePath);
        Assert.Equal("verified", HttpUtility.ParseQueryString(location.Query)["status"]);
        Assert.True(await IsEmailVerifiedAsync(factory, localAccountId, CancellationToken.None));
    }

    #endregion

    #region Send Tests

    [Fact]
    public async Task PostEmailSend_ForAVerifiedAddress_RedirectsWithAlreadyVerified()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedVerificationEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var sessionCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            Password,
            CancellationToken.None
        );

        using var response = await PostEmailFormAsync(
            client,
            sessionCookie,
            EmailSendPath,
            new Dictionary<string, string>(),
            CancellationToken.None
        );

        var location = ReadLocation(response);
        Assert.Equal(EmailPagePath, location.AbsolutePath);
        Assert.Equal("already_verified", HttpUtility.ParseQueryString(location.Query)["error"]);
        Assert.Empty(transport.Delivered);
    }

    [Fact]
    public async Task PostEmailSend_WithoutTheAntiforgeryToken_IsRefused()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedVerificationEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: false,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var sessionCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            Password,
            CancellationToken.None
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, EmailSendPath);
        request.Headers.Add("Cookie", $"{InteractiveLogin.SessionCookieName}={sessionCookie}");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(transport.Delivered);
    }

    #endregion

    #region Me Tests

    [Fact]
    public async Task GetMe_WhenVerificationIsEnabled_ReportsIt()
    {
        using var factory = new HostApplicationFactory();
        await SeedVerificationEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: false,
            CancellationToken.None
        );
        var client = CreateClient(factory);
        var sessionCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            Password,
            CancellationToken.None
        );

        using var response = await GetWithSessionAsync(
            client,
            MePath,
            sessionCookie,
            CancellationToken.None
        );

        var me = await ReadJsonAsync(response, CancellationToken.None);
        Assert.True(me.GetProperty("emailVerification").GetBoolean());
    }

    #endregion
}
