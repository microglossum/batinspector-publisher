using System.Net;
using System.Text.Json;
using BatInspectorPublisher.Adapters.Naturgucker;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Results;
using BatInspectorPublisher.Tests.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerPublisherTests
{
    private const string SpeciesFound = """{"status":0,"meldung":"","naturguckerartid":-77}""";
    private const string NothingOwned = """{"beobachtungen":[],"status":1,"offset":1,"meldung":"0 beobachtungen gefunden","more":false}""";
    private const string Created = """{"status":0,"meldung":"ok","fibeobachtungenkopf":-555}""";
    private const string PhotoTaken = """{"status":0,"meldung":"ok"}""";
    private const string LoginKnown = """{"status":0,"meldung":"nutzer bekannt","id":1}""";

    private static NaturguckerStub Stub() => new NaturguckerStub()
        .On("naturguckerartid", SpeciesFound)
        .On("login", LoginKnown)
        .On("beobachtungen", NothingOwned)
        .On("beobachtungsblock_eintragen2", Created)
        .On("bilder_uebertragen", PhotoTaken);

    private static NaturguckerPublisher Publisher(NaturguckerStub stub, bool lockPlaceAndObserver = false) => new(
        new NaturguckerOptions { ServiceNumber = 4711, Email = "user@example.org", Password = "secret-password", LockPlaceAndObserver = lockPlaceAndObserver },
        new HttpClient(stub),
        baseAddress: new Uri("https://naturgucker.test/mobil/"),
        minRequestInterval: TimeSpan.Zero);

    private static Task<PublishResult> Publish(NaturguckerStub stub, string name = "Pipistrellus nathusii", bool commit = true, bool lockPlaceAndObserver = false, CancellationToken ct = default) =>
        ((IObservationPublisher)Publisher(stub, lockPlaceAndObserver)).PublishAsync(
            TestData.Candidate() with { ScientificName = name }, TestData.Evidence(), new PublishOptions { Commit = commit }, ct);

    private static string OwnObservation(string entryId, bool withPhoto) =>
        $$"""{"beobachtungen":[{"guid":"{{entryId.ToUpperInvariant()}}","kopfid":-321,"belegbild":"{{(withPhoto ? "https://naturgucker.test/?bild=1" : "")}}"}],"status":1,"offset":2,"meldung":"1 beobachtungen gefunden","more":false}""";

    private static string EntryIdOf(string name) => NaturguckerEntryId.For(TestData.Candidate() with { ScientificName = name });

    [Fact]
    public void Constructor_IncompleteOptions_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => new NaturguckerPublisher(new NaturguckerOptions(), new HttpClient(Stub())));
    }

    [Fact]
    public void PlatformId_IsNaturgucker()
    {
        Assert.Equal("naturgucker", Publisher(Stub()).PlatformId);
    }

    [Fact]
    public async Task DryRun_NewEntry_WouldCreateAndWritesNothing()
    {
        var stub = Stub();

        var result = await Publish(stub, commit: false);

        Assert.Equal(PublishStatus.WouldCreate, result.Status);
        Assert.Equal("Pipistrellus nathusii", result.TaxonName);
        Assert.Contains("Uhr", result.Description);
        Assert.Equal(["naturguckerartid", "login", "beobachtungen"], stub.Moduls);
    }

    [Fact]
    public async Task Commit_NewEntry_CreatesTheObservationAndAttachesThePhotoButNotTheAudio()
    {
        var stub = Stub();

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.Created, result.Status);
        Assert.Equal("-555", result.ObservationId);
        Assert.True(result.SpectrogramAttached);
        Assert.False(result.AudioAttached);
        Assert.Equal(["naturguckerartid", "login", "beobachtungen", "beobachtungsblock_eintragen2", "bilder_uebertragen"], stub.Moduls);
        Assert.DoesNotContain(stub.Requests, r => r.Body.Contains("AUDIO-BYTES"));
        Assert.Contains(stub.Requests, r => r.Modul == "bilder_uebertragen" && r.Body.Contains("SPECTROGRAM-BYTES") && r.Body.Contains("-555"));
    }

    [Fact]
    public async Task Commit_CreatesWithTheStableIdentifierTheSpeciesIdAndACertainIdentification()
    {
        var stub = Stub();

        await Publish(stub, "Pipistrellus nathusii");

        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Modul == "beobachtungsblock_eintragen2").Body);
        var observation = body.RootElement.GetProperty("beobachtungen")[0];
        Assert.Equal(EntryIdOf("Pipistrellus nathusii"), observation.GetProperty("guid").GetString());
        Assert.Equal(-77, observation.GetProperty("fiart").GetInt64());
        Assert.Equal(0, observation.GetProperty("bestimmungunsicher").GetInt32());
        Assert.Equal(0, observation.GetProperty("sperrung").GetInt32());
        Assert.Equal("11.06.2026", body.RootElement.GetProperty("datum").GetString());
        Assert.Contains("Ruferkennung", observation.GetProperty("bemerkung").GetString());
    }

    [Fact]
    public async Task Commit_RemarkIsPercentEncoded_BecauseThePlatformDecodesIt()
    {
        var stub = Stub();

        await Publish(stub, "Pipistrellus nathusii");

        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Modul == "beobachtungsblock_eintragen2").Body);
        var remark = body.RootElement.GetProperty("beobachtungen")[0].GetProperty("bemerkung").GetString()!;
        Assert.DoesNotContain(' ', remark);
        Assert.DoesNotContain("°", remark, StringComparison.Ordinal);
        Assert.Contains("Ruferkennung", Uri.UnescapeDataString(remark), StringComparison.Ordinal);
        Assert.Contains("%C2%B0C", remark, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commit_LockPlaceAndObserver_IsPassedOn()
    {
        var stub = Stub();

        await Publish(stub, lockPlaceAndObserver: true);

        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Modul == "beobachtungsblock_eintragen2").Body);
        Assert.Equal(1, body.RootElement.GetProperty("beobachtungen")[0].GetProperty("sperrung").GetInt32());
    }

    [Fact]
    public async Task Commit_GenusOrGroupValue_IsFiledUnderThePlatformsEntryAsUncertainAndSaysSoInTheDescription()
    {
        var stub = Stub();

        var result = await Publish(stub, "Nyctaloid");

        Assert.Equal(PublishStatus.Created, result.Status);
        Assert.Equal("Eptesicus/Nyctalus/Vespertilio indet.", result.TaxonName);
        Assert.Contains("taxon=indet.", stub.Requests[0].Body);
        Assert.Contains("gattung=eptesicus%2Fnyctalus%2Fvespertilio", stub.Requests[0].Body);
        Assert.Contains("\"Nyctaloid\"", result.Description);
        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Modul == "beobachtungsblock_eintragen2").Body);
        Assert.Equal(1, body.RootElement.GetProperty("beobachtungen")[0].GetProperty("bestimmungunsicher").GetInt32());
    }

    [Fact]
    public async Task Commit_NameOfTheInputFileThePlatformSpellsDifferently_AsksForThePlatformsName()
    {
        var stub = Stub();

        await Publish(stub, "Cnephaeus serotinus");

        Assert.Contains("gattung=eptesicus", stub.Requests[0].Body);
        Assert.Contains("taxon=serotinus", stub.Requests[0].Body);
    }

    [Fact]
    public async Task Commit_AlreadyThereWithPhoto_IsSkippedAsDuplicateAndWritesNothing()
    {
        var stub = Stub().On("beobachtungen", OwnObservation(EntryIdOf("Pipistrellus nathusii"), withPhoto: true));

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.SkippedDuplicate, result.Status);
        Assert.Equal("-321", result.ObservationId);
        Assert.True(result.SpectrogramAttached);
        Assert.Equal(["naturguckerartid", "login", "beobachtungen"], stub.Moduls);
    }

    [Fact]
    public async Task DryRun_AlreadyThereWithoutPhoto_WouldResumeAndWritesNothing()
    {
        var stub = Stub().On("beobachtungen", OwnObservation(EntryIdOf("Pipistrellus nathusii"), withPhoto: false));

        var result = await Publish(stub, commit: false);

        Assert.Equal(PublishStatus.WouldResume, result.Status);
        Assert.Equal("-321", result.ObservationId);
        Assert.Equal(["naturguckerartid", "login", "beobachtungen"], stub.Moduls);
    }

    [Fact]
    public async Task Commit_AlreadyThereWithoutPhoto_AttachesThePhotoToTheExistingObservation()
    {
        var stub = Stub().On("beobachtungen", OwnObservation(EntryIdOf("Pipistrellus nathusii"), withPhoto: false));

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.Resumed, result.Status);
        Assert.Equal("-321", result.ObservationId);
        Assert.True(result.SpectrogramAttached);
        Assert.Equal(["naturguckerartid", "login", "beobachtungen", "bilder_uebertragen"], stub.Moduls);
        Assert.Contains("-321", stub.Requests[^1].Body);
    }

    [Fact]
    public async Task Commit_AnotherObservationOfTheSameDay_IsNotADuplicate()
    {
        var stub = Stub().On("beobachtungen", OwnObservation(EntryIdOf("Myotis myotis"), withPhoto: true));

        var result = await Publish(stub, "Pipistrellus nathusii");

        Assert.Equal(PublishStatus.Created, result.Status);
    }

    [Fact]
    public async Task Commit_SpeciesUnknownToThePlatform_IsSkippedWithItsMessageAndAsksNothingElse()
    {
        var stub = Stub().On("naturguckerartid", """{"status":-1,"meldung":"Art nicht gefunden","naturguckerartid":0}""");

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, result.Status);
        Assert.Contains("Art nicht gefunden", result.Message);
        Assert.Contains("Pipistrellus nathusii", result.Message);
        Assert.Equal(["naturguckerartid"], stub.Moduls);
    }

    [Theory]
    [InlineData("Parus major")]
    [InlineData("Vespertilio murinus murinus")]
    public async Task Commit_NotPublishableByThisPlatform_IsSkippedBeforeAnyRequest(string name)
    {
        var stub = Stub();

        var result = await Publish(stub, name);

        Assert.Equal(name == "Parus major" ? PublishStatus.SkippedUnresolvedTaxon : PublishStatus.SkippedInvalidEntry, result.Status);
        Assert.False(result.StopsRun);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Commit_QueryRefused_FailsBeforeAnythingIsWrittenAndStopsTheRun()
    {
        var stub = Stub().On("beobachtungen", """{"status":-1,"meldung":"Anmeldung fehlgeschlagen"}""");

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.Preparation, result.InterruptedStep);
        Assert.True(result.StopsRun);
        Assert.Contains("Anmeldung fehlgeschlagen", result.Message);
        Assert.DoesNotContain("beobachtungsblock_eintragen2", stub.Moduls);
    }

    [Fact]
    public async Task LoginRefused_FailsBeforeTheOwnObservationsAreAskedAndStopsTheRun()
    {
        // The query for the own observations would answer "nothing found" for an unknown login: only the login call tells.
        var stub = Stub().On("login", """{"status":-1,"meldung":"nutzer unbekannt"}""");

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.Preparation, result.InterruptedStep);
        Assert.True(result.StopsRun);
        Assert.Contains("nutzer unbekannt", result.Message);
        Assert.Equal(["naturguckerartid", "login"], stub.Moduls);
    }

    [Fact]
    public async Task LoginAnswer_IsNeverPassedOn()
    {
        // The platform's answer to a known account carries the profile; a refusal's message is passed on, nothing else.
        var stub = Stub().On("login", """{"status":-1,"meldung":"nutzer unbekannt","strasse":"Musterweg 1","telefon":"0123456"}""");

        var result = await Publish(stub);

        Assert.DoesNotContain("Musterweg", result.Message);
        Assert.DoesNotContain("0123456", result.Message);
        Assert.DoesNotContain("0123456", result.Error?.ToString() ?? "");
    }

    [Fact]
    public async Task Login_IsCheckedOncePerPublisher()
    {
        var stub = Stub();
        var publisher = (IObservationPublisher)Publisher(stub);

        await publisher.PublishAsync(TestData.Candidate() with { ScientificName = "Pipistrellus nathusii" }, TestData.Evidence(), new PublishOptions { Commit = false }, default);
        await publisher.PublishAsync(TestData.Candidate() with { ScientificName = "Myotis myotis" }, TestData.Evidence(), new PublishOptions { Commit = false }, default);

        Assert.Equal(1, stub.Moduls.Count(m => m == "login"));
    }

    [Fact]
    public async Task Commit_ServerErrorOnTheLookup_FailsAndStopsTheRun()
    {
        var stub = Stub().On("naturguckerartid", "oops", HttpStatusCode.InternalServerError);

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.True(result.StopsRun);
        Assert.Contains("HTTP 500", result.Message);
    }

    [Fact]
    public async Task Commit_CreateRefused_FailsWithoutRepeatingOrUploadingAndDoesNotStopTheRun()
    {
        var stub = Stub().On("beobachtungsblock_eintragen2", """{"status":-1,"meldung":"Pflichtfeld fehlt"}""");

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.CreateObservation, result.InterruptedStep);
        Assert.False(result.StopsRun);
        Assert.Null(result.ObservationId);
        Assert.Contains("Pflichtfeld fehlt", result.Message);
        Assert.Equal(1, stub.Moduls.Count(m => m == "beobachtungsblock_eintragen2"));
        Assert.DoesNotContain("bilder_uebertragen", stub.Moduls);
    }

    [Fact]
    public async Task Commit_PhotoRefused_FailsNamingTheObservationAndDoesNotCreateASecondOne()
    {
        var stub = Stub().On("bilder_uebertragen", """{"status":-1,"meldung":"Bild zu groß"}""");

        var result = await Publish(stub);

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.AttachSpectrogram, result.InterruptedStep);
        Assert.Equal("-555", result.ObservationId);
        Assert.False(result.SpectrogramAttached);
        Assert.Contains("Bild zu groß", result.Message);
        Assert.Contains("next run completes it", result.Message);
        Assert.Equal(1, stub.Moduls.Count(m => m == "beobachtungsblock_eintragen2"));
        Assert.Equal(1, stub.Moduls.Count(m => m == "bilder_uebertragen"));
    }

    [Fact]
    public async Task Commit_Cancelled_IsReportedAsCancelledWithNothingCreated()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Publish(Stub(), ct: cts.Token);

        Assert.Equal(PublishStatus.Cancelled, result.Status);
        Assert.Equal(PublishStep.Preparation, result.InterruptedStep);
        Assert.Contains("nothing was created", result.Message);
    }

    [Fact]
    public async Task Commit_NothingEverCarriesThePasswordInTheClear()
    {
        var stub = Stub();

        var result = await Publish(stub);

        Assert.All(stub.Requests, r => Assert.DoesNotContain("secret-password", r.Body + r.Uri));
        Assert.DoesNotContain("secret-password", result.Message + result.Description);
    }
}
