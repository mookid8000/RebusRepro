using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Persistence.InMem;
using Rebus.Retry.Simple;
using Rebus.Transport.InMem;
using Testy;

namespace RebusRepro;

[TestFixture]
public class ReproduceIssueWithFailedMessageAndBusInjection : FixtureBase
{
    [Test]
    public async Task DoIt()
    {
        var flags = new AllTheFlags();

        var services = new ServiceCollection();

        services.AddSingleton(flags);
        services.AddRebus(
            configure => configure
                .Logging(l => l.None())
                .Transport(t => t.UseInMemoryTransport(new(), "whateer"))
                .Timeouts(t => t.StoreInMemory())
                .Options(o => o.SimpleRetryStrategy(secondLevelRetriesEnabled: true, maxDeliveryAttempts: 2))
        );

        services.AddRebusHandler<MyMessageHandler>();

        await using var provider = services.BuildServiceProvider();

        provider.StartRebus();

        var bus = provider.GetRequiredService<IBus>();

        await bus.SendLocal(new MyMessage());

        var timeout = TimeSpan.FromSeconds(23);

        Assert.IsTrue(flags.MyMessageFlags.GotTheMessage.WaitOne(timeout));
        Assert.IsTrue(flags.MyMessageFlags.GotTheMessage.WaitOne(timeout));
        Assert.IsTrue(flags.FailedMyMessageFlags.GotTheMessage.WaitOne(timeout));

        Assert.That(flags.MyMessageFlags.BusProperlyInjectedCount, Is.EqualTo(2));
        Assert.That(flags.MyMessageFlags.FailedRuns, Is.EqualTo(2));
        Assert.That(flags.FailedMyMessageFlags.BusProperlyInjectedCount, Is.EqualTo(1));
        Assert.That(flags.FailedMyMessageFlags.FailedRuns == 1, Is.True);

        Assert.IsTrue(flags.MyMessageFlags.GotTheMessage.WaitOne(timeout));
        Assert.IsTrue(flags.MyMessageFlags.GotTheMessage.WaitOne(timeout));
        Assert.IsTrue(flags.FailedMyMessageFlags.GotTheMessage.WaitOne(timeout));

        Assert.That(flags.MyMessageFlags.BusProperlyInjectedCount, Is.EqualTo(4));
        Assert.That(flags.MyMessageFlags.FailedRuns, Is.EqualTo(4));
        Assert.That(flags.FailedMyMessageFlags.BusProperlyInjectedCount, Is.EqualTo(2));
        Assert.That(flags.FailedMyMessageFlags.FailedRuns == 2, Is.True);
    }

    class FlagsAndResetEvents
    {
        public readonly AutoResetEvent GotTheMessage = new(initialState: false);
        public int BusProperlyInjectedCount { get; set; }
        public int FailedRuns { get; set; }
    }

    class AllTheFlags
    {
        public readonly FlagsAndResetEvents MyMessageFlags = new();
        public readonly FlagsAndResetEvents FailedMyMessageFlags = new();
    }

    record MyMessage;

    class MyMessageHandler : IHandleMessages<MyMessage>, IHandleMessages<IFailed<MyMessage>>
    {
        readonly AllTheFlags _flags;
        readonly IBus _bus;

        public MyMessageHandler(IBus bus, AllTheFlags flags)
        {
            Console.WriteLine($"MyMessageHandler created with {bus}, {flags}");
            _flags = flags;
            _bus = bus;
        }

        public async Task Handle(MyMessage message)
        {
            var flags = _flags.MyMessageFlags;

            flags.BusProperlyInjectedCount += _bus != null ? 1 : 0;
            flags.FailedRuns++;
            flags.GotTheMessage.Set();

            Console.WriteLine($"MyMessage handled - flags: {new { flags.BusProperlyInjectedCount, flags.FailedRuns }}");

            throw new ApplicationException("Trigger 2nd level delivery");
        }

        public async Task Handle(IFailed<MyMessage> message)
        {
            var flags = _flags.FailedMyMessageFlags;

            flags.BusProperlyInjectedCount += _bus != null ? 1 : 0;
            flags.FailedRuns++;
            flags.GotTheMessage.Set();

            if (flags.FailedRuns == 1)
            {
                await _bus.DeferLocal(TimeSpan.FromSeconds(1), message.Message);
            }

            Console.WriteLine($"IFailed<MyMessage> handled - flags: {new { flags.BusProperlyInjectedCount, flags.FailedRuns }}");
        }
    }
}