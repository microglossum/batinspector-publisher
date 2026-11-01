using System.Net;
using BatInspectorPublisher.Adapters.Naturgucker;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerApiClientTransportTests
{
    private static readonly Uri BaseAddress = new("https://naturgucker.test/mobil/");
    private static readonly NaturguckerOptions Options = new() { ServiceNumber = 4711, Email = "user@example.org", Password = "secret-password" };
    private const string EntryId = "{5b0e3a7c-92d4-4f18-a6c1-0d7e8f2b4a93}";

    private static NaturguckerApiClient Client(NaturguckerStub stub, TimeSpan? interval = null, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(new HttpClient(stub), Options, BaseAddress, interval ?? TimeSpan.Zero, delay);

    private static string Page(bool more, int offset, string items) =>
        $$"""{"beobachtungen":[{{items}}],"status":1,"offset":{{offset}},"meldung":"","more":{{(more ? "true" : "false")}}}""";

    private static string Item(string guid, long head) => $$"""{"guid":"{{guid}}","kopfid":{{head}},"belegbild":""}""";

    [Fact]
    public async Task FindOwnObservation_FollowsThePagesUntilTheEntryIsFound()
    {
        var pages = new Queue<string>([Page(true, 2, Item("{00000000-0000-0000-0000-000000000001}", 1) + "," + Item("{00000000-0000-0000-0000-000000000002}", 2)), Page(false, 3, Item(EntryId, 33))]);
        var stub = new NaturguckerStub().On("beobachtungen", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(pages.Dequeue()) });

        var found = await Client(stub).FindOwnObservationAsync(new DateOnly(2026, 6, 11), EntryId, CancellationToken.None);

        Assert.Equal(33, found?.HeadId);
        Assert.Contains("offset=0", stub.Requests[0].Uri.Query);
        Assert.Contains("offset=2", stub.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task FindOwnObservation_NoMatch_ReturnsNull()
    {
        var stub = new NaturguckerStub().On("beobachtungen", Page(false, 2, Item("{00000000-0000-0000-0000-000000000001}", 1)));

        Assert.Null(await Client(stub).FindOwnObservationAsync(new DateOnly(2026, 6, 11), EntryId, CancellationToken.None));
    }

    [Fact]
    public async Task FindOwnObservation_AsksForOneDayAndOnlyThisAppsObservations()
    {
        var stub = new NaturguckerStub().On("beobachtungen", Page(false, 1, ""));

        await Client(stub).FindOwnObservationAsync(new DateOnly(2026, 6, 11), EntryId, CancellationToken.None);

        var query = Assert.Single(stub.Requests).Uri.Query;
        Assert.Contains("datumvon=11.06.2026", query);
        Assert.Contains("datumbis=11.06.2026", query);
        Assert.Contains("nurservicenr=4711", query);
    }

    [Fact]
    public async Task FindOwnObservation_AnswerThatNeverEnds_IsAnErrorNotASilentMiss()
    {
        var stub = new NaturguckerStub().On("beobachtungen", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page(true, int.MaxValue, Item("{00000000-0000-0000-0000-000000000001}", 1))),
        });

        await Assert.ThrowsAsync<NaturguckerApiException>(() => Client(stub).FindOwnObservationAsync(new DateOnly(2026, 6, 11), EntryId, CancellationToken.None));
    }

    [Fact]
    public async Task NetworkFailure_ThrowsWithoutTheAddressWhichCarriesTheLogin()
    {
        var stub = new NaturguckerStub().On("beobachtungen", _ => throw new HttpRequestException("failed for https://naturgucker.test/mobil/?email1=user@example.org&md5=abc"));

        var ex = await Assert.ThrowsAsync<NaturguckerApiException>(() => Client(stub).FindOwnObservationAsync(new DateOnly(2026, 6, 11), EntryId, CancellationToken.None));

        Assert.DoesNotContain("user@example.org", ex.Message);
        Assert.DoesNotContain("md5", ex.Message);
    }

    [Fact]
    public async Task Requests_AreSpacedByTheMinimumInterval()
    {
        var waits = new List<TimeSpan>();
        var stub = new NaturguckerStub().On("naturguckerartid", """{"status":0,"meldung":"","naturguckerartid":5}""");
        var client = Client(stub, TimeSpan.FromSeconds(30), (wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        });
        var taxon = new NaturguckerTaxon("Pipistrellus", "nathusii", Uncertain: false);

        await client.FindSpeciesAsync(taxon, CancellationToken.None);
        await client.FindSpeciesAsync(taxon, CancellationToken.None);

        var wait = Assert.Single(waits);
        Assert.InRange(wait, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DefaultMinRequestInterval_IsNotZero()
    {
        Assert.True(NaturguckerApiClient.DefaultMinRequestInterval > TimeSpan.Zero);
    }
}
