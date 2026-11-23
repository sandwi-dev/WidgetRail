using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.RemoteMediaTransport.Tests;

[TestClass]
public sealed class PolicyTests
{
    [TestMethod]
    [DataRow("0.0.0.0")][DataRow("0.255.1.2")][DataRow("10.1.2.3")][DataRow("127.0.0.1")]
    [DataRow("169.254.169.254")][DataRow("172.16.0.1")][DataRow("172.31.255.255")][DataRow("192.168.1.1")]
    [DataRow("168.63.129.16")]
    [DataRow("100.64.0.1")][DataRow("100.127.255.255")][DataRow("192.0.0.1")][DataRow("192.0.2.1")]
    [DataRow("192.88.99.1")][DataRow("198.18.0.1")][DataRow("198.19.255.255")][DataRow("198.51.100.1")]
    [DataRow("203.0.113.1")][DataRow("224.0.0.1")][DataRow("240.0.0.1")][DataRow("255.255.255.255")]
    [DataRow("::")][DataRow("::1")][DataRow("::ffff:127.0.0.1")][DataRow("fc00::1")][DataRow("fe80::1")]
    [DataRow("ff02::1")][DataRow("64:ff9b::7f00:1")][DataRow("2002:7f00:1::1")][DataRow("2001:db8::1")]
    [DataRow("2001::1")][DataRow("2001:2::1")][DataRow("2001:20::1")][DataRow("3fff::1")]
    public void NonPublicAndTransitionAddressesAreRejected(string value) => Assert.IsFalse(PublicMediaAddress.IsPublic(IPAddress.Parse(value)));

    [TestMethod]
    [DataRow("1.1.1.1")][DataRow("8.8.8.8")][DataRow("100.63.255.255")][DataRow("100.128.0.1")]
    [DataRow("172.15.255.255")][DataRow("172.32.0.1")][DataRow("198.17.255.255")][DataRow("198.20.0.1")]
    [DataRow("2606:4700:4700::1111")][DataRow("2001:4860:4860::8888")][DataRow("::ffff:1.1.1.1")]
    public void PublicAddressesRemainEligible(string value) => Assert.IsTrue(PublicMediaAddress.IsPublic(IPAddress.Parse(value)));

    [TestMethod]
    [DataRow("http://media.example/a")][DataRow("https://user:password@media.example/a")]
    [DataRow("https://media.example:444/a")][DataRow("https://media.example/a#fragment")]
    [DataRow("https://localhost/a")][DataRow("https://test.localhost/a")][DataRow("https://printer.local/a")]
    [DataRow("https://127.1/a")][DataRow("https://2130706433/a")][DataRow("https://[::ffff:127.0.0.1]/a")]
    [DataRow("https://media.example./a")][DataRow("file:///C:/private.png")]
    public void UnsafeUrlsAreRejectedBeforeAnyRequest(string value) => Assert.IsFalse(PublicMediaPolicy.PublicHttps.Allows(new(value)));

    [TestMethod]
    public void ExactOriginsAreImmutableAndDoNotExpandToSuffixes()
    {
        string[] origins = ["https://cdn.example", "https://media.example"];
        var policy = new PublicMediaPolicy(origins); origins[0] = "https://evil.example";
        Assert.IsTrue(policy.Allows(new("https://cdn.example/image?signature=secret")));
        Assert.IsFalse(policy.Allows(new("https://cdn.example.evil.example/image")));
        Assert.IsFalse(policy.Allows(new("https://sub.cdn.example/image")));
        Assert.IsFalse(policy.Allows(new("https://evil.example/image")));
        Assert.Throws<ArgumentException>(() => new PublicMediaPolicy(["https://cdn.example/"]));
        Assert.Throws<ArgumentException>(() => new PublicMediaPolicy(["https://cdn.example/path"]));
        Assert.Throws<ArgumentException>(() => new PublicMediaPolicy(["https://cdn.example", "https://cdn.example"]));
        Assert.Throws<ArgumentException>(() => new PublicMediaPolicy([]));
    }

    [TestMethod]
    public async Task MixedDnsIsRejectedBeforeDialingEvenWhenPublicAnswerIsFirst()
    {
        var dials = 0;
        var error = await Assert.ThrowsAsync<PublicMediaException>(async () => await PublicMediaConnector.ConnectAsync(new("cdn.example",443),
            (_,_) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Loopback }),
            (_,_) => { ++dials; return ValueTask.FromResult<Stream>(new MemoryStream()); }, CancellationToken.None));
        Assert.AreEqual(PublicMediaFailure.DestinationRejected, error.Failure); Assert.AreEqual(0,dials);
    }

    [TestMethod]
    public async Task ConnectionPinsValidatedAddressesWithoutAnotherDnsLookup()
    {
        var resolutions = 0; var destinations = new List<IPEndPoint>();
        using var stream = await PublicMediaConnector.ConnectAsync(new("cdn.example",443),
            (_,_) => Task.FromResult(++resolutions == 1 ? new[] { IPAddress.Parse("1.1.1.1"),IPAddress.Parse("8.8.8.8") } : [IPAddress.Loopback]),
            (endpoint,_) =>
            {
                destinations.Add(endpoint);
                if(destinations.Count==1)throw new SocketException((int)SocketError.NetworkUnreachable);
                return ValueTask.FromResult<Stream>(new MemoryStream());
            },CancellationToken.None);
        Assert.AreEqual(1,resolutions); Assert.AreEqual(2,destinations.Count);
        Assert.AreEqual(IPAddress.Parse("8.8.8.8"),destinations[1].Address); Assert.AreEqual(443,destinations[1].Port);
    }

    [TestMethod]
    public void ProductionHandlerHasNoCredentialProxyRedirectOrCertificateBypass()
    {
        using var handler=PublicMediaClient.CreateHandler(new());
        Assert.IsFalse(handler.UseCookies); Assert.IsFalse(handler.UseProxy); Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.PreAuthenticate); Assert.IsNull(handler.Credentials); Assert.IsNull(handler.DefaultProxyCredentials);
        Assert.IsNull(handler.SslOptions.RemoteCertificateValidationCallback); Assert.IsNotNull(handler.ConnectCallback);
        Assert.AreEqual(DecompressionMethods.None,handler.AutomaticDecompression); Assert.AreEqual(0,handler.MaxResponseDrainSize);
    }

    [TestMethod]
    public void SameOriginPolicyDoesNotTrustAnotherPublicCdn()
    {
        var policy = PublicMediaPolicy.SameOrigin(new("https://CDN.example:443/a?signature=opaque"));
        Assert.IsTrue(policy.Allows(new("https://cdn.example/redirected")));
        Assert.IsFalse(policy.Allows(new("https://other.example/media")));
        Assert.IsFalse(policy.Allows(new("https://sub.cdn.example/media")));
        Assert.Throws<PublicMediaException>(() => PublicMediaPolicy.SameOrigin(new("https://127.0.0.1/media")));
        Assert.Throws<PublicMediaException>(() => PublicMediaPolicy.SameOrigin(new("https://cdn.example:444/media")));
    }
}
