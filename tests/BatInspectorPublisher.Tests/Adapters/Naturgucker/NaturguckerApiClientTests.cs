using System.Net;
using BatInspectorPublisher.Adapters.Naturgucker;
using BatInspectorPublisher.Tests.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerApiClientTests
{
    private static readonly Uri BaseAddress = new("https://naturgucker.test/mobil/");

    private static readonly NaturguckerOptions Options = new() { ServiceNumber = 4711, Email = "user@example.org", Password = "secret-password" };

    private static NaturguckerApiClient Client(StubHttpHandler handler) => new(new HttpClient(handler), Options, BaseAddress);

    private static Task<SpeciesLookup> Find(StubHttpHandler handler, string genus = "Pipistrellus", string epithet = "nathusii") =>
        Client(handler).FindSpeciesAsync(new NaturguckerTaxon(genus, epithet, Uncertain: false), CancellationToken.None);

    [Fact]
    public async Task FindSpecies_Found_ReturnsTheId()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """{"status":0,"meldung":"","naturguckerartid":-424242}""");

        var lookup = await Find(handler);

        Assert.Equal(-424242, lookup.Id);
        Assert.Null(lookup.Problem);
    }

    [Fact]
    public async Task FindSpecies_SendsGenusAndEpithetInLowerCaseAndTheServiceNumber()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """{"status":0,"meldung":"","naturguckerartid":5}""");

        await Find(handler, "Pipistrellus", "nathusii");

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("modul=naturguckerartid", body);
        Assert.Contains("gattung=pipistrellus", body);
        Assert.Contains("taxon=nathusii", body);
        Assert.Contains("service=4711", body);
    }

    [Fact]
    public async Task FindSpecies_GenusEntry_AsksForTheUndeterminedEpithet()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """{"status":0,"meldung":"","naturguckerartid":5}""");

        await Client(handler).FindSpeciesAsync(new NaturguckerTaxon("Eptesicus/Nyctalus/Vespertilio", NaturguckerTaxa.Undetermined, Uncertain: true), CancellationToken.None);

        Assert.Contains("taxon=indet.", Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task FindSpecies_NotFound_PassesThePlatformsMessageThrough()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """{"status":-1,"meldung":"Art nicht gefunden","naturguckerartid":0}""");

        var lookup = await Find(handler);

        Assert.Null(lookup.Id);
        Assert.Equal("Art nicht gefunden", lookup.Problem);
    }

    [Fact]
    public async Task FindSpecies_NoIdAndNoMessage_StillSaysSomething()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """{"status":-1}""");

        var lookup = await Find(handler);

        Assert.Null(lookup.Id);
        Assert.False(string.IsNullOrWhiteSpace(lookup.Problem));
    }

    [Fact]
    public async Task FindSpecies_AnIdWithAnErrorStatus_IsNotAnId()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """{"status":-1,"meldung":"Dienst unbekannt","naturguckerartid":42}""");

        var lookup = await Find(handler);

        Assert.Null(lookup.Id);
        Assert.Equal("Dienst unbekannt", lookup.Problem);
    }

    [Fact]
    public async Task FindSpecies_AnswerWrappedInACallback_IsReadLikeAPlainOne()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """jQuery19_123({"status":0,"meldung":"","naturguckerartid":9});""");

        var lookup = await Find(handler);

        Assert.Equal(9, lookup.Id);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    [InlineData(HttpStatusCode.Forbidden, "HTTP 403")]
    public async Task FindSpecies_HttpError_ThrowsWithoutTheRequestOrTheBody(HttpStatusCode status, string expected)
    {
        var handler = new StubHttpHandler().On("POST /mobil/", status, "secret-body");

        var ex = await Assert.ThrowsAsync<NaturguckerApiException>(() => Find(handler));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public async Task FindSpecies_AnswerThatIsNotAJsonObject_Throws(string body)
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<NaturguckerApiException>(() => Find(handler));
    }

    [Fact]
    public async Task FindSpecies_NeverSendsThePasswordOrTheAddress()
    {
        var handler = new StubHttpHandler().On("POST /mobil/", HttpStatusCode.OK, """{"status":0,"meldung":"","naturguckerartid":5}""");

        await Find(handler);

        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("secret-password", request.Body);
        Assert.DoesNotContain("user", request.Body);
        Assert.DoesNotContain("user", request.Uri.ToString());
    }

    [Fact]
    public void Constructor_AddressWithoutHttps_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new NaturguckerApiClient(new HttpClient(), Options, new Uri("http://naturgucker.test/mobil/")));
    }

    [Fact]
    public void DefaultBaseAddress_IsHttps()
    {
        Assert.Equal(Uri.UriSchemeHttps, NaturguckerApiClient.DefaultBaseAddress.Scheme);
    }
}
