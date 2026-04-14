using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NetCoreServer;
using Server.Protocol;
using Server.Tools;

namespace Server.TCP
{
    /// <summary>
    /// 基于NetCoreServer的TCP会话 — 每个连接的GameServer对应一个Session
    /// NetCoreServer内部使用ReceiveAsync(Memory<byte>)，在macOS/Linux/Windows均稳定
    /// </summary>
    internal class GameDBSession : TcpSession
    {
        private readonly SocketListener _listener;
        internal Socket ClientSocket { get; private set; }

        public GameDBSession(TcpServer server, SocketListener listener) : base(server)
        {
            _listener = listener;
        }

        protected override void OnConnected()
        {
            ClientSocket = Socket;  // Set ngay khi OnConnected fires
            if (ClientSocket == null)
            {
                System.Console.WriteLine("[DBSERVER-ERR] Socket is null in OnConnected, refusing session");
                Disconnect();
                return;
            }
            Interlocked.Increment(ref _listener._connectedCount);
            _listener._sessions[ClientSocket] = this;
            _listener.InvokeConnected(ClientSocket);
        }

        protected override void OnDisconnected()
        {
            if (ClientSocket != null)
            {
                _listener._sessions.TryRemove(ClientSocket, out _);
                Interlocked.Decrement(ref _listener._connectedCount);
                _listener.InvokeClosed(ClientSocket);
            }
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            var sock = ClientSocket;
            if (sock == null) return;  // Guard: ClientSocket not set yet
            Interlocked.Add(ref _listener.totalBytesRead, size);
            bool ok = _listener.InvokeReceived(sock, buffer, (int)offset, (int)size);
            if (!ok)
                Disconnect();
        }

        protected override void OnError(SocketError error)
        {
            // NetCoreServer会自动在错误后触发OnDisconnected
        }
    }

    /// <summary>
    /// NetCoreServer的TcpServer子类
    /// </summary>
    internal class GameDBTcpServer : TcpServer
    {
        private readonly SocketListener _listener;

        public GameDBTcpServer(IPAddress address, int port, SocketListener listener)
            : base(address, port)
        {
            _listener = listener;
        }

        protected override TcpSession CreateSession() => new GameDBSession(this, _listener);
    }

    /// <summary>
    /// 对外公开的SocketListener — 内部用NetCoreServer实现，接口与原版兼容
    /// 彻底解决macOS上SocketAsyncEventArgs的0字节/Poll误报问题
    /// </summary>
    public class SocketListener
    {
        // socket → session映射（线程安全）
        internal ConcurrentDictionary<Socket, GameDBSession> _sessions = new();
        internal int _connectedCount;

        // 字节统计（与原版同名）
        public long totalBytesRead;
        public long totalBytesWrite;

        // Program.cs访问的属性
        public long TotalBytesReadSize => Interlocked.Read(ref totalBytesRead);
        public long TotalBytesWriteSize => Interlocked.Read(ref totalBytesWrite);

        private GameDBTcpServer _server;

        /// <summary>当前已连接数</summary>
        public int ConnectedSocketsCount => _connectedCount;

        // 事件定义（使用简化签名，不再使用SocketAsyncEventArgs）
        public event SocketConnectedEventHandler SocketConnected;
        public event SocketClosedEventHandler SocketClosed;
        public event SocketReceivedEventHandler SocketReceived;
        public event SocketSendedEventHandler SocketSended;

        // 内部事件触发方法（供GameDBSession调用，绕过event的外部invoke限制）
        internal void InvokeConnected(Socket socket)
        {
            try { SocketConnected?.Invoke(this, socket); }
            catch (Exception ex) { LogManager.WriteException(ex.ToString()); }
        }

        internal void InvokeClosed(Socket socket)
        {
            try { SocketClosed?.Invoke(this, socket); }
            catch (Exception ex) { LogManager.WriteException(ex.ToString()); }
        }

        internal bool InvokeReceived(Socket socket, byte[] buffer, int offset, int count)
        {
            try
            {
                return SocketReceived?.Invoke(this, socket, buffer, offset, count) ?? true;
            }
            catch (Exception ex)
            {
                LogManager.WriteException(ex.ToString());
                return false;
            }
        }

        internal void InvokeSended(TCPOutPacket packet)
        {
            try { SocketSended?.Invoke(this, packet); }
            catch { }
        }

        public SocketListener(int capacity, int bufferSize)
        {
            // 参数保留以兼容现有调用代码，NetCoreServer自动管理内部缓冲
        }

        public void Init() { /* NetCoreServer无需额外初始化 */ }

        public void Start(string ip, int port)
        {
            var address = (string.IsNullOrEmpty(ip) || ip == "0.0.0.0" || ip == "*")
                ? IPAddress.Any
                : IPAddress.Parse(ip);

            _server = new GameDBTcpServer(address, port, this);
            _server.OptionKeepAlive = true;
            _server.OptionNoDelay = true;
            _server.Start();

            LogManager.WriteLog(LogTypes.Info,
                $"[GameDBServer] 监听 {ip}:{port} (NetCoreServer)");
        }

        public void Stop()
        {
            _server?.Stop();
        }

        /// <summary>
        /// 向指定Socket发送数据包
        /// NetCoreServer.SendAsync内部拷贝数据到发送队列，调用完毕可立即回收packet
        /// </summary>
        internal bool SendData(Socket s, TCPOutPacket tcpOutPacket)
        {
            if (!_sessions.TryGetValue(s, out var session))
            {
                System.Console.WriteLine($"[DBSERVER-SENDFAIL] No session found for socket {s?.RemoteEndPoint}, sessions={_sessions.Count}, cmd={tcpOutPacket?.PacketCmdID}");
                return false;
            }

            byte[] data = tcpOutPacket.GetPacketBytes();
            int size = tcpOutPacket.PacketDataSize;

            if (size <= 0)
            {
                System.Console.WriteLine($"[DBSERVER-SENDFAIL] size={size} invalid, cmd={tcpOutPacket?.PacketCmdID}");
                return false;
            }

            // CRITICAL FIX: dùng s.Send() synchronous thay vì session.SendAsync() async
            // NetCoreServer's SendAsync trên macOS có thể queue data mà không gửi ngay
            // → GameServer Receive timeout trước khi data được deliver
            // Direct socket send đảm bảo data đến kernel send buffer NGAY LẬP TỨC
            bool sent = false;
            try
            {
                int bytesSent = s.Send(data, 0, size, SocketFlags.None);
                sent = (bytesSent == size);
                System.Console.WriteLine($"[DBSERVER-SENDOK] Direct send cmd={tcpOutPacket?.PacketCmdID} size={size} sent={bytesSent} to {s?.RemoteEndPoint}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DBSERVER-SENDFAIL] Direct send exception for cmd={tcpOutPacket?.PacketCmdID}: {ex.Message}");
            }

            if (sent)
                Interlocked.Add(ref totalBytesWrite, size);

            // 通知上层回收packet
            InvokeSended(tcpOutPacket);

            return sent;
        }
    }
}
