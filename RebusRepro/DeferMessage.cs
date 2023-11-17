using NUnit.Framework;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Rebus.Transport.InMem;
using Testy;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace RebusRepro;

[TestFixture]
public class DeferMessage : FixtureBase
{
    [Test]
    public async Task DeferMessageForLongTime()
    {
        using var activator = new BuiltinHandlerActivator();

        activator.Handle<MessageForTheFuture>(async msg => Console.WriteLine($"******* RECEIVED MESSAGE SENT AT {msg.SendTime} *******"));

        var bus = Configure.With(activator)
            .Transport(t => t.UseInMemoryTransport(new(), "whatever"))
            //.Timeouts(t => t.StoreInMemory())
            .Timeouts(t => t.StoreInPostgres("server=localhost; database=rebus2_test; user id=postgres; password=postgres", "Timeouts"))
            .Start();

        await bus.DeferLocal(TimeSpan.FromHours(2), new MessageForTheFuture(DateTimeOffset.Now));

        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    record MessageForTheFuture(DateTimeOffset SendTime);
}