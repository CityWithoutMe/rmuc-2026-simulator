using System;
using System.Collections.Generic;
using System.Linq;

// 主机专用的四席房间状态机，可脱离 Unity 测试。客户端不能声明别人的身份。
public sealed class LanRoom
{
    public sealed class Member
    {
        public int peer;
        public int slot = -1;
        public bool ready;
        public bool loaded;
    }

    readonly Dictionary<int, Member> members = new Dictionary<int, Member>();
    int startedCount;
    public bool Started { get; private set; }
    public bool AllLoaded => Started && members.Count == startedCount && members.Values.All(m => m.loaded);
    public Member[] Members => members.Values.OrderBy(m => m.peer).ToArray();
    public bool CanStart => !Started && members.ContainsKey(0) && members.Values.All(m => m.slot >= 0 && m.ready);

    public bool Add(int peer)
    {
        if (Started || members.Count >= 4 || members.ContainsKey(peer)) return false;
        members.Add(peer, new Member { peer = peer });
        return true;
    }

    public bool Select(int peer, int slot)
    {
        if (Started || slot < 0 || slot >= 4 || !members.TryGetValue(peer, out Member m)) return false;
        if (members.Values.Any(other => other.peer != peer && other.slot == slot)) return false;
        if (m.slot != slot) m.ready = false;
        m.slot = slot;
        return true;
    }

    public bool Ready(int peer, bool value)
    {
        if (Started || !members.TryGetValue(peer, out Member m) || m.slot < 0) return false;
        m.ready = value;
        return true;
    }

    public bool Start(int peer)
    {
        if (peer != 0 || !CanStart) return false;
        Started = true;
        startedCount = members.Count;
        return true;
    }

    public void Loaded(int peer) { if (Started && members.TryGetValue(peer, out Member m)) m.loaded = true; }
    public int Slot(int peer) => members.TryGetValue(peer, out Member m) ? m.slot : -1;
    public bool Remove(int peer) => members.Remove(peer);
}
