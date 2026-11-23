// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Host;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

// An end user who forgot a password resets it with a code mailed to the verified address (ADR-1533).
public class InteractivePasswordResetTests
{
    private const string UserName = "reset-user";
    private const string Password = "reset-old-password-1";
    private const string NewPassword = "reset-new-password-Strong-42";
    private const string Email = "reset-user@example.test";
    private const string OtherUserName = "reset-other-user";
    private const string OtherEmail = "reset-other-user@example.test";
    private const string UnverifiedUserName = "reset-unverified-user";
    private const string UnverifiedEmail = "reset-unverified-user@example.test";
    private const string ReturnUrl = "/foo";
    private const string LoginPath = "/account/login";
    private const string MePath = "/account/me";
    private const string ForgotPath = "/account/password/forgot";
    private const string ResetPath = "/account/password/reset";

    private static TimeSpan MailTimeout { get; } = TimeSpan.FromSeconds(15);

    #region Scaffolding

    // The tenant reads its settings at its first request, so this is always the first call on a factory.
    private static ValueTask SeedResetEnabledAsync(
        HostApplicationFactory factory,
        CancellationToken cancellationToken
    ) =>
        factory.SeedTenantSettingsAsync(
            new Dictionary<string, object> { [OpenIdSettingNames.PasswordResetEnabled] = true },
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

    private static async ValueTask<(HttpStatusCode StatusCode, string Body)> GetPageAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.StatusCode, body);
    }

    // Posts the forgot-password form and returns where it redirects.
    private static async ValueTask<Uri> RequestResetAsync(
        HttpClient client,
        string userName,
        CancellationToken cancellationToken
    )
    {
        using var response = await InteractiveLogin.PostFormAsync(
            client,
            ForgotPath,
            ForgotPath,
            sessionCookie: null,
            new Dictionary<string, string> { ["username"] = userName },
            cancellationToken
        );
        return ReadLocation(response);
    }

    // Posts the reset-password form and returns where it redirects.
    private static async ValueTask<Uri> SubmitResetAsync(
        HttpClient client,
        string flowId,
        string code,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken
    )
    {
        using var response = await InteractiveLogin.PostFormAsync(
            client,
            $"{ResetPath}?flow={Uri.EscapeDataString(flowId)}",
            ResetPath,
            sessionCookie: null,
            new Dictionary<string, string>
            {
                ["flow"] = flowId,
                ["code"] = code,
                ["new_password"] = newPassword,
                ["confirm_password"] = confirmPassword,
                ["ReturnUrl"] = ReturnUrl,
            },
            cancellationToken
        );
        return ReadLocation(response);
    }

    private static string ReadFlowId(Uri location)
    {
        Assert.Equal(ResetPath, location.AbsolutePath);
        var flowId = HttpUtility.ParseQueryString(location.Query)["flow"];
        Assert.False(string.IsNullOrEmpty(flowId), location.ToString());
        return flowId;
    }

    // Requests a reset for the user name and returns the flow and the code the mail carries.
    private static async ValueTask<(string FlowId, string Code)> RequestAndReadCodeAsync(
        HttpClient client,
        CapturingMailTransport transport,
        string userName,
        string address,
        CancellationToken cancellationToken
    )
    {
        var location = await RequestResetAsync(client, userName, cancellationToken);
        var flowId = ReadFlowId(location);
        var message = await transport.WaitForMessageToAsync(
            address,
            MailTimeout,
            cancellationToken
        );
        return (flowId, CapturingMailTransport.ReadCode(message));
    }

    private static async ValueTask<HttpStatusCode> GetMeStatusAsync(
        HttpClient client,
        string sessionCookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        request.Headers.Add("Cookie", $"{InteractiveLogin.SessionCookieName}={sessionCookie}");
        using var response = await client.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }

    #endregion

    #region Login Page Tests

    [Fact]
    public async Task GetLogin_WhenResetIsEnabled_LinksTheForgotPasswordPage()
    {
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        var client = CreateClient(factory);

        var (statusCode, body) = await GetPageAsync(
            client,
            $"{LoginPath}?ReturnUrl={Uri.EscapeDataString(ReturnUrl)}",
            CancellationToken.None
        );

        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.Contains("id=\"forgot-password-link\"", body);
        Assert.Contains($"password/forgot?ReturnUrl={Uri.EscapeDataString(ReturnUrl)}", body);
    }

    [Fact]
    public async Task GetLogin_WhenResetIsDisabled_LinksNothing()
    {
        using var factory = new HostApplicationFactory();
        var client = CreateClient(factory);

        var (statusCode, body) = await GetPageAsync(client, LoginPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.DoesNotContain("forgot-password-link", body);
    }

    #endregion

    #region Forgot Password Tests

    [Fact]
    public async Task GetForgotPassword_WhenResetIsDisabled_ReturnsNotFound()
    {
        using var factory = new HostApplicationFactory();
        var client = CreateClient(factory);

        var (statusCode, _) = await GetPageAsync(client, ForgotPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, statusCode);
    }

    [Fact]
    public async Task PostForgotPassword_ForAnUnknownUserName_RedirectsToTheResetPageAndSendsNothing()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            OtherUserName,
            Password,
            OtherEmail,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);

        var location = await RequestResetAsync(
            client,
            "reset-unknown-user",
            CancellationToken.None
        );
        ReadFlowId(location);

        // The mail of a known account follows the request, so a mail the unknown one sent would have arrived first.
        await RequestAndReadCodeAsync(
            client,
            transport,
            OtherUserName,
            OtherEmail,
            CancellationToken.None
        );
        var message = Assert.Single(transport.Delivered);
        Assert.Equal(OtherEmail, message.To);
    }

    [Fact]
    public async Task PostForgotPassword_ForAnUnverifiedAddress_SendsNothing()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UnverifiedUserName,
            Password,
            UnverifiedEmail,
            emailVerified: false,
            CancellationToken.None
        );
        await factory.SeedLocalAccountAsync(
            OtherUserName,
            Password,
            OtherEmail,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);

        var location = await RequestResetAsync(client, UnverifiedUserName, CancellationToken.None);
        ReadFlowId(location);

        // The mail of a verified account follows the request, so a mail the unverified one sent would have arrived
        // first.
        await RequestAndReadCodeAsync(
            client,
            transport,
            OtherUserName,
            OtherEmail,
            CancellationToken.None
        );
        var message = Assert.Single(transport.Delivered);
        Assert.Equal(OtherEmail, message.To);
    }

    [Fact]
    public async Task PostForgotPassword_WithoutTheAntiforgeryToken_IsRefusedAndMailsNothing()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        await factory.SeedLocalAccountAsync(
            OtherUserName,
            Password,
            OtherEmail,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, ForgotPath);
        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = UserName }
        );

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The mail of another account follows the refused post, so a mail the refused one sent would have arrived
        // first.
        await RequestAndReadCodeAsync(
            client,
            transport,
            OtherUserName,
            OtherEmail,
            CancellationToken.None
        );
        var message = Assert.Single(transport.Delivered);
        Assert.Equal(OtherEmail, message.To);
    }

    #endregion

    #region Reset Password Tests

    [Fact]
    public async Task PostResetPassword_WithoutTheAntiforgeryToken_IsRefusedAndChangesNothing()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (flowId, code) = await RequestAndReadCodeAsync(
            client,
            transport,
            UserName,
            Email,
            CancellationToken.None
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, ResetPath);
        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["flow"] = flowId,
                ["code"] = code,
                ["new_password"] = NewPassword,
                ["confirm_password"] = NewPassword,
                ["ReturnUrl"] = ReturnUrl,
            }
        );

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The password is unchanged.
        using var oldPasswordResponse = await InteractiveLogin.SubmitLoginAsync(
            client,
            UserName,
            Password,
            ReturnUrl,
            CancellationToken.None
        );
        Assert.NotNull(InteractiveLogin.ReadSessionCookie(oldPasswordResponse));

        // The flow still verifies, so the refused post did not use the code.
        var location = await SubmitResetAsync(
            client,
            flowId,
            code,
            NewPassword,
            NewPassword,
            CancellationToken.None
        );
        Assert.Equal(LoginPath, location.AbsolutePath);
        Assert.Equal("password_reset", HttpUtility.ParseQueryString(location.Query)["status"]);
    }

    [Fact]
    public async Task PostResetPassword_WithTheMailedCode_ChangesThePasswordAndEndsTheSessions()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var earlierCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            Password,
            CancellationToken.None
        );
        Assert.Equal(
            HttpStatusCode.OK,
            await GetMeStatusAsync(client, earlierCookie, CancellationToken.None)
        );

        var (flowId, code) = await RequestAndReadCodeAsync(
            client,
            transport,
            UserName,
            Email,
            CancellationToken.None
        );
        var location = await SubmitResetAsync(
            client,
            flowId,
            code,
            NewPassword,
            NewPassword,
            CancellationToken.None
        );

        Assert.Equal(LoginPath, location.AbsolutePath);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("password_reset", query["status"]);
        Assert.Equal(ReturnUrl, query["ReturnUrl"]);

        using var oldPasswordResponse = await InteractiveLogin.SubmitLoginAsync(
            client,
            UserName,
            Password,
            ReturnUrl,
            CancellationToken.None
        );
        var oldPasswordLocation = ReadLocation(oldPasswordResponse);
        Assert.Equal(
            "invalid_credentials",
            HttpUtility.ParseQueryString(oldPasswordLocation.Query)["error"]
        );
        Assert.Null(InteractiveLogin.ReadSessionCookie(oldPasswordResponse));

        var newCookie = await InteractiveLogin.SignInAsync(
            client,
            UserName,
            NewPassword,
            CancellationToken.None
        );
        Assert.Equal(
            HttpStatusCode.OK,
            await GetMeStatusAsync(client, newCookie, CancellationToken.None)
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await GetMeStatusAsync(client, earlierCookie, CancellationToken.None)
        );
    }

    [Fact]
    public async Task PostResetPassword_WithAWrongCode_RedirectsWithInvalidCode()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (flowId, code) = await RequestAndReadCodeAsync(
            client,
            transport,
            UserName,
            Email,
            CancellationToken.None
        );
        var wrongCode = code == "000000" ? "000001" : "000000";

        var location = await SubmitResetAsync(
            client,
            flowId,
            wrongCode,
            NewPassword,
            NewPassword,
            CancellationToken.None
        );

        Assert.Equal(ResetPath, location.AbsolutePath);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("invalid_code", query["error"]);
        Assert.Equal(flowId, query["flow"]);
    }

    [Fact]
    public async Task PostResetPassword_WhenTheConfirmationDiffers_RedirectsWithPasswordMismatch()
    {
        var transport = new CapturingMailTransport();
        using var factory = new HostApplicationFactory();
        await SeedResetEnabledAsync(factory, CancellationToken.None);
        await factory.SeedLocalAccountAsync(
            UserName,
            Password,
            Email,
            emailVerified: true,
            CancellationToken.None
        );
        using var host = factory.WithWebHostBuilder(UseTransport(transport));
        var client = CreateClient(host);
        var (flowId, code) = await RequestAndReadCodeAsync(
            client,
            transport,
            UserName,
            Email,
            CancellationToken.None
        );

        var location = await SubmitResetAsync(
            client,
            flowId,
            code,
            NewPassword,
            NewPassword + "-different",
            CancellationToken.None
        );

        Assert.Equal(ResetPath, location.AbsolutePath);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("password_mismatch", query["error"]);
        Assert.Equal(flowId, query["flow"]);
    }

    #endregion
}
