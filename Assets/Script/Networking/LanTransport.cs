using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// 无 Unity 依赖的局域网传输。后台线程只处理字节，Unity 对象只在主线程访问。
public sealed class LanTransport : IDisposable
{
    public const int MaxFrameBytes = 256 * 1024;
    public sealed class Event
    {
        public int peer;
        public string kind;
        public string data;
        public double receivedAt;
    }

    sealed class Connection : IDisposable
    {
        readonly TcpClient socket;
        readonly NetworkStream stream;
        readonly ConcurrentQueue<byte[]> outgoing = new ConcurrentQueue<byte[]>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly LanTransport owner;
        readonly int id;
        int closed;
        int queued;

        public Connection(LanTransport owner, TcpClient socket, int id)
        {
            this.owner = owner;
            this.socket = socket;
            this.id = id;
            socket.NoDelay = true;
            socket.ReceiveTimeout = 180000; // 首次图形/场景激活可能较慢，主线程另外检测心跳。
            socket.SendTimeout = 10000;
            stream = socket.GetStream();
        }

        public void Start()
        {
            new Thread(ReadLoop) { IsBackground = true, Name = "LAN receive" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "LAN send" }.Start();
        }

        public void Send(byte[] bytes)
        {
            if (Volatile.Read(ref closed) != 0) return;
            if (Interlocked.Increment(ref queued) > 64)
            {
                Interlocked.Decrement(ref queued);
                Close("发送积压，连接已断开");
                return;
            }
            outgoing.Enqueue(bytes);
            wake.Set();
        }

        void ReadLoop()
        {
            try
            {
                byte[] size = new byte[4];
                while (Volatile.Read(ref closed) == 0)
                {
                    ReadExact(stream, size);
                    int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(size, 0));
                    if (length <= 0 || length > MaxFrameBytes) throw new IOException("无效消息长度");
                    byte[] body = new byte[length];
                    ReadExact(stream, body);
                    owner.Enqueue(id, "data", Encoding.UTF8.GetString(body));
                }
            }
            catch (Exception e) { Close(e.Message); }
        }

        void WriteLoop()
        {
            try
            {
                while (Volatile.Read(ref closed) == 0)
                {
                    if (!outgoing.TryDequeue(out byte[] body)) { wake.WaitOne(100); continue; }
                    Interlocked.Decrement(ref queued);
                    byte[] size = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(body.Length));
                    stream.Write(size, 0, size.Length);
                    stream.Write(body, 0, body.Length);
                }
            }
            catch (Exception e) { Close(e.Message); }
        }

        void Close(string reason)
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            socket.Close();
            wake.Set();
            owner.Remove(id, this, reason);
        }

        public void Dispose() { Close("连接关闭"); }
    }

    readonly object gate = new object();
    readonly Dictionary<int, Connection> connections = new Dictionary<int, Connection>();
    readonly ConcurrentQueue<Event> events = new ConcurrentQueue<Event>();
    TcpListener listener;
    TcpClient connecting;
    volatile bool disposed;
    int nextPeer;
    int pendingEvents;

    public void Host(int port)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start(3);
        new Thread(AcceptLoop) { IsBackground = true, Name = "LAN accept" }.Start();
    }

    public void Join(string address, int port)
    {
        new Thread(() =>
        {
            try
            {
                var socket = new TcpClient();
                lock (gate) { if (disposed) { socket.Close(); return; } connecting = socket; }
                var task = socket.ConnectAsync(address, port);
                if (!task.Wait(5000)) throw new IOException("连接超时，请检查主机 IP、防火墙和端口");
                lock (gate)
                {
                    connecting = null;
                    if (disposed) { socket.Close(); return; }
                    var connection = new Connection(this, socket, 0);
                    connections.Add(0, connection);
                    Enqueue(0, "connected", "");
                    connection.Start();
                }
            }
            catch (Exception e)
            {
                lock (gate) { connecting?.Close(); connecting = null; }
                Enqueue(0, "error", e.GetBaseException().Message);
            }
        }) { IsBackground = true, Name = "LAN connect" }.Start();
    }

    void AcceptLoop()
    {
        try
        {
            while (!disposed)
            {
                TcpClient socket = listener.AcceptTcpClient();
                lock (gate)
                {
                    if (disposed || connections.Count >= 3) { socket.Close(); continue; }
                    int id = ++nextPeer;
                    var connection = new Connection(this, socket, id);
                    connections.Add(id, connection);
                    Enqueue(id, "connected", "");
                    connection.Start();
                }
            }
        }
        catch (Exception e) { if (!disposed) Enqueue(-1, "error", e.Message); }
    }

    static void ReadExact(Stream stream, byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int n = stream.Read(bytes, offset, bytes.Length - offset);
            if (n == 0) throw new EndOfStreamException("对方已断开");
            offset += n;
        }
    }

    void Enqueue(int peer, string kind, string data)
    {
        if (disposed) return;
        if (Interlocked.Increment(ref pendingEvents) > 4096 && kind == "data")
        {
            Interlocked.Decrement(ref pendingEvents);
            lock (gate) { if (connections.TryGetValue(peer, out Connection c)) c.Dispose(); }
            return;
        }
        events.Enqueue(new Event { peer = peer, kind = kind, data = data,
            receivedAt = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency });
    }

    void Remove(int peer, Connection connection, string reason)
    {
        lock (gate)
        {
            if (connections.TryGetValue(peer, out Connection existing) && existing == connection)
                connections.Remove(peer);
        }
        Enqueue(peer, "disconnected", reason);
    }

    public bool Poll(out Event message)
    {
        if (!events.TryDequeue(out message)) return false;
        Interlocked.Decrement(ref pendingEvents);
        return true;
    }

    public void Send(int peer, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length == 0 || bytes.Length > MaxFrameBytes) throw new ArgumentException("消息过大");
        lock (gate) { if (connections.TryGetValue(peer, out Connection c)) c.Send(bytes); }
    }

    public void Disconnect(int peer)
    {
        lock (gate) { if (connections.TryGetValue(peer, out Connection c)) c.Dispose(); }
    }

    public void Broadcast(string json)
    {
        int[] peers;
        lock (gate) { peers = new int[connections.Count]; connections.Keys.CopyTo(peers, 0); }
        foreach (int peer in peers) Send(peer, json);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            listener?.Stop();
            connecting?.Close();
            var all = new List<Connection>(connections.Values);
            foreach (Connection c in all) c.Dispose();
            connections.Clear();
        }
    }
}
