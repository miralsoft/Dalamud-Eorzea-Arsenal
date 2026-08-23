using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The mapping is written by a push and read by the drawing thread, and those are different threads.
/// </summary>
/// <remarks>
/// Written because the fix for that was otherwise only asserted by reading the code. A dictionary read
/// during a write does not fail politely: it throws, or it returns nonsense, and on the framework thread
/// that breaks the window rather than logging a line. These do not prove the absence of a race — no test
/// can — but they exercise the exact pairings that used to be unguarded. Checked by taking the lock back
/// out: the forget-while-reading one fails reliably that way, the other two only sometimes, which is the
/// nature of the thing and the reason the fix is not left to a test to notice.
/// </remarks>
public sealed class GearsetMappingConcurrencyTests
{
    private const string Cid = "c775e7b757ede630cd0aa1113bd102661ab38829ca52a6422ab782862f268646";

    [Fact]
    public async Task ReadingWhileAPushWritesDoesNotThrow()
    {
        var (service, _) = Build();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writer = Task.Run(() =>
        {
            var round = 0;
            while (!stop.IsCancellationRequested)
            {
                service.RecordPush(Cid, Sent(round), Answered(round, held: round % 3 == 0));
                round++;
            }
        });

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                // Exactly what the diagnostics panel and the BiS window ask for, every frame.
                _ = service.UncertainMatches.Count;
                _ = service.CachedCount(Cid);
                _ = service.IsHeld("uid-0");
                _ = service.MappingStatus;
                _ = service.ServerMintsUids;
            }
        });

        await Task.WhenAll(writer, reader);
    }

    [Fact]
    public async Task ForgettingWhileReadingDoesNotThrow()
    {
        var (service, _) = Build();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writer = Task.Run(() =>
        {
            var round = 0;
            while (!stop.IsCancellationRequested)
            {
                service.RecordPush(Cid, Sent(round), Answered(round, held: false));
                if (round % 5 == 0)
                {
                    service.Forget(Cid);
                }

                round++;
            }
        });

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = service.CachedCount(Cid);
                _ = service.UncertainMatches.Count;
            }
        });

        await Task.WhenAll(writer, reader);
    }

    /// <summary>
    /// Two background threads, which is the other half: a mapping read and a push can land together, and
    /// resolving copies the rows under the lock before it starts hashing.
    /// </summary>
    [Fact]
    public async Task ResolvingWhileAPushWritesDoesNotThrow()
    {
        var (service, _) = Build();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writer = Task.Run(() =>
        {
            var round = 0;
            while (!stop.IsCancellationRequested)
            {
                service.RecordPush(Cid, Sent(round), Answered(round, held: false));
                round++;
            }
        });

        var resolver = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = service.Resolve(Cid, Set(0, "DRK", "set 0", 100));
            }
        });

        await Task.WhenAll(writer, resolver);
    }

    private static (GearsetMappingService Service, InMemoryGearsetIdentityStore Store) Build()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        var store = new InMemoryGearsetIdentityStore();
        return (new GearsetMappingService(api, tokens, store, new TestClock(), new CapturingLog()), store);
    }

    private static GearsetDto[] Sent(int round) =>
        [.. Enumerable.Range(0, 8).Select(i => Set(i, "DRK", $"set {i} r{round % 2}", 100 + i))];

    private static GearsetAssignment[] Answered(int round, bool held) =>
        [.. Enumerable.Range(0, 8).Select(i => new GearsetAssignment
        {
            GearIndex = i,
            SetUid = $"uid-{i}",
            MatchedBy = i % 2 == 0 ? MatchedBy.Index : MatchedBy.Exact,
            State = held && i == 0 ? PushState.Held : PushState.Resolved,
        })];

    private static GearsetDto Set(int index, string job, string name, int weapon) => new()
    {
        GearIndex = index,
        Job = job,
        Name = name,
        Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = weapon } },
    };
}
