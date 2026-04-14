using System;
using System.Net.Sockets;
using Server.Protocol;

namespace Server.TCP
{
    /// <summary>
    /// 连接成功通知 — 参数为已连接的Socket
    /// </summary>
    public delegate void SocketConnectedEventHandler(object sender, Socket socket);

    /// <summary>
    /// 断开通知 — 参数为断开的Socket
    /// </summary>
    public delegate void SocketClosedEventHandler(object sender, Socket socket);

    /// <summary>
    /// 接收数据通知 — 参数为收到数据的Socket及原始缓冲区
    /// 返回false时SocketListener会断开该连接
    /// </summary>
    public delegate bool SocketReceivedEventHandler(object sender, Socket socket, byte[] buffer, int offset, int count);

    /// <summary>
    /// 发送完成通知 — 参数为已发送的packet（可回收至pool）
    /// </summary>
    public delegate void SocketSendedEventHandler(object sender, TCPOutPacket tcpOutPacket);
}
