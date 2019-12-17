using Akka.Actor;
using Akka.Event;
using Akka.Actor.Dsl;
using Akka.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace WithWatch
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var defaultConfig = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"));

            // bind to port 11000
            var system1 = ActorSystem.Create("System1", ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port = 11000")
                .WithFallback(defaultConfig));

            system1.ActorOf(dsl =>
            {
                dsl.ReceiveAny((m, ctx) =>
                {
                    ctx.Sender.Forward(m);
                });
            }, "pinger");

            // bind to port 11001
            var port = 11002;
            
            for(var i = 0; i < 30; i++)
            {
                var sys2Config = ConfigurationFactory.ParseString($"akka.remote.dot-netty.tcp.port = {port + i}")
                .WithFallback(defaultConfig);
                var system2 = ActorSystem.Create("System2", sys2Config);

                var selection = system2.ActorSelection(new RootActorPath(Address.Parse($"akka.tcp://System1@127.0.0.1:11000")) / "user" / "pinger");
                IActorRef pinger = ActorRefs.NoSender;
                while (pinger.IsNobody())
                {
                    try
                    {
                        pinger = await selection.ResolveOne(TimeSpan.FromSeconds(1));
                    }
                    catch { }
                }

                var watcher = system2.ActorOf(dsl =>
                {
                    var sent = false;
                    dsl.OnPreStart = ctx =>
                    {
                        // death watch pinger
                        ctx.Watch(pinger);
                    };

                    dsl.Receive<Terminated>((t, ctx) =>
                    {
                        ctx.GetLogger().Info("{0} terminated", t);
                    });

                    dsl.Receive<string>((str, ctx) =>
                    {
                        pinger.Forward(str);
                    });
                }, "watcher");

                await watcher.Ask<string>("foo", TimeSpan.FromSeconds(1));
                system2.Log.Info("Received ping back from {0}", system1);

                await system2.Terminate();
                system2 = null;
            }

            Console.ReadLine();
            await system1.Terminate();
            Console.ReadLine();
        }
    }
}
