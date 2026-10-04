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

    static int Main()
    {
        try
        {
            TestRoom(); TestTransport(); TestMalformedFrame();
            Console.WriteLine("PASS: " + assertions + " assertions; 4-player room, real TCP loopback, UTF-8/framing, ownership, scene barrier, disconnect.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
