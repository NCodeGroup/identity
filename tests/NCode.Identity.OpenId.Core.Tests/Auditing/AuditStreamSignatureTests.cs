// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using NCode.Buffers;
using NCode.Identity.OpenId.Authentication.Auditing.Streams;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public class AuditStreamSignatureTests
{
    #region Sign Tests

    [Fact]
    public void Sign_WithTheStandardWebhooksExample_ProducesItsSignature()
    {
        var secret = Convert.FromBase64String("MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw");
        var body = "{\"test\": 2432232314}"u8;

        var result = AuditStreamSignature.Sign(
            secret,
            "msg_p5jXN8AQM9LWM0D4loKWxJek",
            1614265330,
            body
        );

        Assert.Equal("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=", result);
    }

    [Fact]
    public void Sign_ALongMessageIdentifier_SignsItWhole()
    {
        var secret = Convert.FromBase64String("MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw");
        var body = "{\"test\": 2432232314}"u8.ToArray();
        var messageId = new string('m', 200);
        const long timestamp = 1614265330;

        var result = AuditStreamSignature.Sign(secret, messageId, timestamp, body);

        var message = SecureEncoding
            .UTF8.GetBytes($"{messageId}.{timestamp}.")
            .Concat(body)
            .ToArray();
        var expected = HMACSHA256.HashData(secret, message);
        Assert.Equal($"v1,{Convert.ToBase64String(expected)}", result);
    }

    #endregion
}
