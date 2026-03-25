// Rules/SEC011_InsecureIpcTests.cs
// TDD tests for rule SEC011 – "不安全的IPC/远程调用"
//
// RED phase: These tests define expected behavior for detecting insecure IPC patterns.
// The analyzer should detect:
// 1. BasicHttpBinding (no encryption)
// 2. HTTP (non-HTTPS) endpoint addresses
// 3. gRPC channels without TLS
// 4. WCF bindings without security mode

using FluentAssertions;
using LenovoAnalyzer.Tests.Infrastructure;
using Xunit;

namespace LenovoAnalyzer.Tests.Rules;

public sealed class SEC011_InsecureIpcTests
{
    private const string RuleId = "SEC011";

    // =========================================================================
    // Positive cases – must trigger SEC011
    // =========================================================================

    [Fact(DisplayName = "SEC011: BasicHttpBinding creation triggers")]
    public async Task BasicHttpBinding_Creation_Triggers()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var binding = new BasicHttpBinding();
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "BasicHttpBinding uses no encryption by default");
    }

    [Fact(DisplayName = "SEC011: EndpointAddress with HTTP triggers")]
    public async Task EndpointAddress_WithHttp_Triggers()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var endpoint = new EndpointAddress("http://example.com/service");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "HTTP endpoint is not encrypted");
    }

    [Fact(DisplayName = "SEC011: GrpcChannel.ForAddress with HTTP triggers")]
    public async Task GrpcChannel_WithHttp_Triggers()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var channel = GrpcChannel.ForAddress("http://example.com");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "gRPC over HTTP is not encrypted");
    }

    [Fact(DisplayName = "SEC011: HttpClient with HTTP URL triggers")]
    public async Task HttpClient_WithHttpUrl_Triggers()
    {
        var source = """
            using System.Net.Http;
            public class C
            {
                public void M()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new System.Uri("http://api.example.com");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "HTTP base address exposes traffic to interception");
    }

    [Fact(DisplayName = "SEC011: Uri with HTTP scheme literal triggers")]
    public async Task Uri_WithHttpLiteral_Triggers()
    {
        var source = """
            using System;
            public class C
            {
                public void M()
                {
                    var uri = new Uri("http://insecure-api.com/data");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "HTTP URI is not secure");
    }

    [Fact(DisplayName = "SEC011: NetTcpBinding without security triggers")]
    public async Task NetTcpBinding_WithoutSecurity_Triggers()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var binding = new NetTcpBinding(SecurityMode.None);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "SecurityMode.None disables all security");
    }

    [Fact(DisplayName = "SEC011: WebClient with HTTP URL triggers")]
    public async Task WebClient_WithHttpUrl_Triggers()
    {
        var source = """
            using System.Net;
            public class C
            {
                public void M()
                {
                    var client = new WebClient();
                    client.DownloadString("http://example.com/data");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().BeGreaterThanOrEqualTo(1,
            because: "WebClient with HTTP URL is insecure");
    }

    // =========================================================================
    // Negative cases – must NOT trigger SEC011
    // =========================================================================

    [Fact(DisplayName = "SEC011: WSHttpBinding with Transport security does not trigger")]
    public async Task WSHttpBinding_WithTransportSecurity_NoViolation()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var binding = new WSHttpBinding(SecurityMode.Transport);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "WSHttpBinding with Transport security is encrypted");
    }

    [Fact(DisplayName = "SEC011: WSHttpBinding with TransportWithMessageCredential does not trigger")]
    public async Task WSHttpBinding_WithTransportAndMessage_NoViolation()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var binding = new WSHttpBinding(SecurityMode.TransportWithMessageCredential);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "TransportWithMessageCredential provides strong security");
    }

    [Fact(DisplayName = "SEC011: EndpointAddress with HTTPS does not trigger")]
    public async Task EndpointAddress_WithHttps_NoViolation()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var endpoint = new EndpointAddress("https://secure.example.com/service");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "HTTPS endpoint is encrypted");
    }

    [Fact(DisplayName = "SEC011: GrpcChannel.ForAddress with HTTPS does not trigger")]
    public async Task GrpcChannel_WithHttps_NoViolation()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var channel = GrpcChannel.ForAddress("https://secure.example.com");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "gRPC over HTTPS is encrypted");
    }

    [Fact(DisplayName = "SEC011: HttpClient with HTTPS URL does not trigger")]
    public async Task HttpClient_WithHttpsUrl_NoViolation()
    {
        var source = """
            using System.Net.Http;
            public class C
            {
                public void M()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new System.Uri("https://api.example.com");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "HTTPS base address is secure");
    }

    [Fact(DisplayName = "SEC011: localhost HTTP URL does not trigger")]
    public async Task HttpClient_WithLocalhostHttp_NoViolation()
    {
        var source = """
            using System.Net.Http;
            public class C
            {
                public void M()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new System.Uri("http://localhost:5000");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "localhost traffic doesn't leave the machine");
    }

    [Fact(DisplayName = "SEC011: 127.0.0.1 HTTP URL does not trigger")]
    public async Task HttpClient_With127001Http_NoViolation()
    {
        var source = """
            using System.Net.Http;
            public class C
            {
                public void M()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new System.Uri("http://127.0.0.1:8080");
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "loopback address traffic is local only");
    }

    [Fact(DisplayName = "SEC011: NetTcpBinding with Transport security does not trigger")]
    public async Task NetTcpBinding_WithTransportSecurity_NoViolation()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var binding = new NetTcpBinding(SecurityMode.Transport);
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.CountForRule(RuleId).Should().Be(0,
            because: "Transport security mode encrypts traffic");
    }

    [Fact(DisplayName = "SEC011: empty source does not trigger")]
    public async Task EmptySource_NoViolation()
    {
        var result = await AnalyzerTestHarness.AnalyzeAsync(string.Empty);

        result.CountForRule(RuleId).Should().Be(0);
    }

    // =========================================================================
    // Diagnostic metadata
    // =========================================================================

    [Fact(DisplayName = "SEC011: diagnostic has Warning severity")]
    public async Task Diagnostic_HasWarningSeverity()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var binding = new BasicHttpBinding();
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.ForRule(RuleId)
              .Should().HaveCountGreaterThanOrEqualTo(1)
              .And.AllSatisfy(d => d.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning));
    }

    [Fact(DisplayName = "SEC011: diagnostic message indicates insecure communication")]
    public async Task Diagnostic_MessageIndicatesInsecure()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    var binding = new BasicHttpBinding();
                }
            }
            """;

        var result = await AnalyzerTestHarness.AnalyzeAsync(source);

        result.ForRule(RuleId)
              .Should().HaveCountGreaterThanOrEqualTo(1)
              .And.AllSatisfy(d => d.GetMessage().ToLower().Should().ContainAny("insecure", "不安全", "加密", "encrypt"));
    }
}
