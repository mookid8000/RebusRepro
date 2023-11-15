using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Handlers;
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
                .Logging(l => l.Console())
                .Transport(t => t.UseInMemoryTransport(new(), "whateer"))
                .Options(o => o.SimpleRetryStrategy(secondLevelRetriesEnabled: true, maxDeliveryAttempts: 2))
        );

        services.AddRebusHandler<MyMessageHandler>();

        await using var provider = services.BuildServiceProvider();

        provider.StartRebus();

        var bus = provider.GetRequiredService<IBus>();

        await bus.SendLocal(new MyMessage());

        Assert.IsTrue(flags.MyMessageFlags.GotTheMessage.WaitOne(TimeSpan.FromSeconds(3)));
        Assert.IsTrue(flags.FailedMyMessageFlags.GotTheMessage.WaitOne(TimeSpan.FromSeconds(3)));

        Assert.That(flags.MyMessageFlags.BusProperlyInjected, Is.True);
        Assert.That(flags.FailedMyMessageFlags.BusProperlyInjected, Is.True);
    }

    class FlagsAndResetEvents
    {
        public readonly ManualResetEvent GotTheMessage = new(initialState: false);

        public bool BusProperlyInjected { get; set; }
    }

    class AllTheFlags
    {
        public readonly FlagsAndResetEvents MyMessageFlags = new();
        public readonly FlagsAndResetEvents FailedMyMessageFlags = new();
    }

    record MyMessage();

    class MyMessageHandler : IHandleMessages<MyMessage>, IHandleMessages<IFailed<MyMessage>>
    {
        readonly AllTheFlags _flags;
        readonly IBus _bus;

        public MyMessageHandler(IBus bus, AllTheFlags flags)
        {
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _bus = bus;
        }

        public async Task Handle(MyMessage message)
        {
            var flags = _flags.MyMessageFlags;
            flags.BusProperlyInjected = _bus != null;
            flags.GotTheMessage.Set();

            throw new ApplicationException("Trigger 2nd level delivery");
        }

        public async Task Handle(IFailed<MyMessage> message)
        {
            var flags = _flags.FailedMyMessageFlags;
            flags.BusProperlyInjected = _bus != null;
            flags.GotTheMessage.Set();
        }
    }
}