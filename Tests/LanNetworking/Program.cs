using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

static class Program
{
    static int assertions;
    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    static LanTransport.Event Wait(LanTransport transport, string kind, int timeout = 10000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeout)
        {
            if (transport.Poll(out var e))
            {
                if (e.kind == "error") throw new Exception(e.data);
                if (e.kind == kind) return e;
            }
            Thread.Sleep(5);
        }
        throw new Exception("Timed out waiting for " + kind);
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static void TestRoom()
    {
        var room = new LanRoom();
        for (int peer = 0; peer < 4; peer++) Check(room.Add(peer), "Four players admitted");
        Check(!room.Add(4), "Fifth player rejected");
        Check(!room.Add(0), "Duplicate identity rejected");
        Check(!room.Ready(0, true), "Cannot ready without vehicle");
        Check(!room.Select(0, -1) && !room.Select(0, 4), "Invalid vehicle rejected");
        Check(room.Select(0, 0), "Host selects red hero");
        Check(!room.Select(1, 0), "Duplicate vehicle rejected");
        for (int peer = 1; peer < 4; peer++) Check(room.Select(peer, peer), "Exclusive seats");
        Check(!room.Start(0), "Unready match cannot start");
        for (int peer = 0; peer < 4; peer++) Check(room.Ready(peer, true), "Ready accepted");
        Check(room.CanStart, "Four distinct vehicles ready");
        Check(!room.Start(2), "Only host can start");
        Check(room.Start(0), "Host starts 2v2");
        Check(!room.Select(0, 1) && !room.Add(9), "No vehicle swap or late join");
        Check(!room.AllLoaded, "Wait for every scene");
        for (int peer = 0; peer < 3; peer++) room.Loaded(peer);
        room.Loaded(99);
        Check(!room.AllLoaded, "Unknown player cannot unlock scene barrier");
        room.Loaded(3);
        Check(room.AllLoaded, "Four-scene barrier opens");
        Check(room.Remove(3) && !room.AllLoaded, "Disconnect breaks complete match");

        room = new LanRoom(); room.Add(0); room.Add(1);
        room.Select(1, 2); room.Ready(1, true); room.Select(1, 3);
        Check(!room.Members[1].ready, "Vehicle change resets readiness");
        Check(room.Select(0, 2), "Old seat released after vehicle change");
        room.Remove(1);
        Check(room.Select(0, 3), "Disconnected seat released");
        Check(!new LanRoom().CanStart, "Empty room cannot start");
        for (int count = 1; count <= 4; count++)
        {
            room = new LanRoom();
            for (int peer = 0; peer < count; peer++)
            { room.Add(peer); room.Select(peer, peer); room.Ready(peer, true); }
            Check(room.CanStart && room.Start(0), count + " player match starts");
            for (int peer = 0; peer < count - 1; peer++) room.Loaded(peer);
            Check(!room.AllLoaded, "Partial match still waits for each actual member");
            room.Loaded(count - 1);
            Check(room.AllLoaded, "Unused seats do not block loading");
            room.Remove(count - 1);
            Check(!room.AllLoaded, "Removing a started member cannot shorten the barrier");
        }
    }

    static void TestTransport()
    {
        using var server = new LanTransport();
        var clients = new List<LanTransport>();
        try
        {
            int port = FreePort(); server.Host(port);
            int[] ids = new int[3];
            for (int i = 0; i < 3; i++)
            {
                var client = new LanTransport(); clients.Add(client); client.Join("127.0.0.1", port);
                Wait(client, "connected"); ids[i] = Wait(server, "connected").peer;
            }
            Check(ids[0] != ids[1] && ids[1] != ids[2], "Independent transport identities");
            for (int i = 0; i < 3; i++)
            {
                string payload = "玩家" + i + "：" + new string('x', 60000 + i);
                clients[i].Send(0, payload);
                var received = Wait(server, "data");
                Check(received.receivedAt > 0, "Receive timestamp independent of Unity frame time");
                Check(received.peer == ids[i] && received.data == payload, "Length framing and UTF-8 round trip");
                server.Send(ids[i], "exclusive:" + i);
                Check(Wait(clients[i], "data").data == "exclusive:" + i, "Directed response");
            }
            for (int frame = 0; frame < 20; frame++) server.Broadcast("snapshot:" + frame);
            foreach (var client in clients)
                for (int frame = 0; frame < 20; frame++)
                    Check(Wait(client, "data").data == "snapshot:" + frame, "Ordered state broadcast");
            clients[1].Dispose();
            Check(Wait(server, "disconnected").peer == ids[1], "Disconnect identifies correct owner");
            using var replacement = new LanTransport();
            replacement.Join("127.0.0.1", port);
            Wait(replacement, "connected");
            Check(Wait(server, "connected").peer > ids[2], "Replacement cannot reuse old identity");
            bool rejected = false;
            try { server.Send(ids[0], new string('x', LanTransport.MaxFrameBytes + 1)); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Oversize outgoing frame rejected");
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    static void TestMalformedFrame()
    {
        using var server = new LanTransport();
        int port = FreePort(); server.Host(port);
        using var raw = new TcpClient("127.0.0.1", port);
        int peer = Wait(server, "connected").peer;
        byte[] bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(LanTransport.MaxFrameBytes + 1));
        raw.GetStream().Write(bytes, 0, 4);
        Check(Wait(server, "disconnected").peer == peer, "Invalid incoming frame disconnected");
    }

    static void TestRules()
    {
        TestHero42mmShield();
        var coins = new TeamCoinLedger();
        Check(coins.Red == 0 && coins.Blue == 0, "Initial team balances are zero");
        Check(coins.Grant(400, 500), "Host can grant different amounts");
        Check(coins.Spend(false, 15) && coins.Spend(false, 10), "Teammates spend shared balance");
        Check(coins.Red == 375 && coins.Blue == 500, "Spending does not affect opposing team");
        Check(!coins.Spend(false, 376) && coins.Red == 375, "Overdraft rejected atomically");
        Check(!coins.Spend(false, -10), "Negative payment rejected");
        Check(!coins.Grant(1, -1) && coins.Red == 375 && coins.Blue == 500, "Invalid grant affects neither team");
        Check(!coins.Grant(int.MaxValue, 0) && coins.Red == 375, "Overflow grant rejected");
        Check(coins.Spend(true, 500) && !coins.Spend(true, 1), "Exact balance spend then empty balance");
        Check(coins.Grant(0, 0), "Zero grant allowed");
        Check(Math.Abs(RuneRotationRules.Delta(false, 0, 3, .8, 1.9) - Math.PI) < 1e-8, "Small/idle rune constant pi/3 speed");
        double angle = RuneRotationRules.Delta(true, 0, 20, .9, 1.95);
        double partitioned = 0;
        for (int i = 0; i < 1200; i++) partitioned += RuneRotationRules.Delta(true, i / 60.0, 1 / 60.0, .9, 1.95);
        Check(Math.Abs(angle - partitioned) < 1e-8, "Large rune angle independent of frame rate");
        Check(Math.Abs(RuneRotationRules.Speed(true, 0, .9, 1.95) - 1.19) < 1e-8, "Large rune starts at b=2.09-a");
        Check(Math.Abs(RuneRotationRules.Speed(true, Math.PI / (2 * 1.95), .9, 1.95) - 2.09) < 1e-8, "Large rune maximum speed is 2.09");
        Check(Math.Abs(RuneRotationRules.Speed(true, 3 * Math.PI / (2 * 2), 1.045, 2)) < 1e-8, "Large rune minimum never reverses");
        Check(RuneRotationRules.Delta(true, 10, 0, .9, 1.95) == 0, "Paused frame does not rotate");
        Check(Math.Abs(OutpostRules.AngleAt(5) - 2 * Math.PI) < 1e-8, "Outpost five second acceleration");
        Check(Math.Abs(OutpostRules.AngleAt(6) - OutpostRules.AngleAt(5) - .8 * Math.PI) < 1e-8, "Outpost cruise speed");
        Check(!OutpostRules.Stop(179.9, false, false) && OutpostRules.Stop(180, false, false), "Outpost stops at three minutes");
        Check(OutpostRules.Stop(1, true, false) && OutpostRules.Stop(1, false, true), "Outpost stops after destruction or enemy armor opens");
        var rebuild = new OutpostRebuildLedger();
        rebuild.RecordDamage(999); Check(rebuild.Available == 0, "Sub-threshold base loss has no chance");
        rebuild.RecordDamage(1001); Check(rebuild.Available == 2, "Base loss accumulates opportunities");
        Check(!rebuild.Consume(299, false) && !rebuild.Consume(300, true) && rebuild.Available == 2, "No rebuild alive or past deadline");
        Check(rebuild.Consume(299, true) && rebuild.Available == 1, "One reconstruction spends one opportunity");
        Check(AmmoExchangeRules.Count(false, false) == 10 && AmmoExchangeRules.Count(true, false) == 1, "Local ammo exchange units");
        Check(AmmoExchangeRules.Count(false, true) == 100 && AmmoExchangeRules.Count(true, true) == 10, "Remote ammo exchange units");
        Check(AmmoExchangeRules.Price(true, false) == 10 && AmmoExchangeRules.Price(false, true) == 150, "Ammo exchange prices");
        Check(AmmoExchangeRules.Limit(false) == 1000 && AmmoExchangeRules.Limit(true) == 100 && AmmoExchangeRules.Delay == 6, "Ammo team caps and remote delay");

    }

    static void TestHero42mmShield()
    {
        var shield = new Hero42mmShieldRules();
        shield.Observe(0, true, true, 0);
        Check(!shield.Blocked, "Initial zero allowance does not imply overfiring");
        shield.Fired(1, true, true, 1);
        shield.Observe(1, true, true, 0);
        Check(!shield.Blocked, "Last legal shot remains valid");
        shield.Observe(2, false, true, 0);
        shield.Observe(4.999, false, true, 0);
        Check(!shield.Blocked, "Death grace lasts full three seconds");
        shield.Observe(5, false, true, 0);
        Check(shield.Blocked, "Death shields enemy armor after three seconds");
        shield.Observe(6, true, true, 0);
        Check(shield.Blocked, "Reviving without ammunition cannot lift shield");
        shield.Observe(7, true, true, 1);
        Check(!shield.Blocked, "Living hero with allowance lifts shield");
        shield.Observe(8, false, true, 5);
        shield.Fired(8.1, false, true, 5);
        shield.Fired(8.2, false, true, 4);
        Check(!shield.Blocked, "First two posthumous rounds are within grace");
        shield.Fired(8.3, false, true, 3);
        Check(shield.Blocked, "Third posthumous round immediately activates shield");
        shield.Observe(9, true, true, 5);
        shield.Observe(10, false, true, 5);
        shield.Fired(10.1, false, true, 5);
        Check(!shield.Blocked, "Posthumous count resets on next death");
        shield.Observe(11, true, true, 5);
        shield.Fired(12, true, true, 0);
        Check(shield.Blocked, "Firing with zero allowance immediately shields armor");
        shield.Observe(13, true, true, 0);
        Check(shield.Blocked, "Overfire remains blocked until ammunition exists");
        shield.Observe(14, true, true, 3);
        shield.Observe(15, true, false, 3);
        shield.Observe(17.999, true, false, 3);
        Check(!shield.Blocked, "Offline grace also lasts three seconds");
        shield.Observe(18, true, false, 3);
        Check(shield.Blocked, "Offline hero triggers shield even with allowance");
        shield.Observe(19, true, false, 10);
        Check(shield.Blocked, "Buying ammunition cannot clear offline shield");
        shield.Observe(20, true, true, 10);
        Check(!shield.Blocked, "Reconnection with live hero and ammunition clears shield");
        shield.Observe(21, false, true, 10);
        shield.Observe(22, true, true, 0);
        shield.Observe(30, true, true, 0);
        Check(shield.Blocked, "Revival without ammunition cannot cancel pending death trigger");
        shield.Observe(31, true, true, 1);
        Check(!shield.Blocked, "Ammunition also clears a delayed trigger after early revival");
        shield.Observe(32, false, true, 1);
        shield.Observe(33, true, true, 1);
        shield.Observe(40, true, true, 0);
        Check(!shield.Blocked, "Healthy revival during grace cancels delayed trigger");
    }

    static int Main()
    {
        try
        {
            TestRules(); TestRoom(); TestTransport(); TestMalformedFrame();
            Console.WriteLine("PASS: " + assertions + " assertions; 4-player room, real TCP loopback, UTF-8/framing, ownership, scene barrier, disconnect.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
