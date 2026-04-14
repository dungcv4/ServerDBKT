using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using Server.Protocol;
using Server.TCP;
using Server.Tools;
//using System.Windows.Forms;
using GameDBServer.DB;
using GameDBServer.Logic;
using GameDBServer.Core;

namespace GameDBServer.Server
{
    /// <summary>
    /// TCP管理对象
    /// </summary>
    class TCPManager
    {
        private static TCPManager instance = new TCPManager();

        public static long processCmdNum = 0;
        public static long processTotalTime = 0;
        public static Dictionary<int, PorcessCmdMoniter> cmdMoniter = new Dictionary<int, PorcessCmdMoniter>();

        private TCPManager() { }

        public static TCPManager getInstance()
        {
            return instance;
        }

        public void initialize(int capacity)
        {
            capacity = Math.Max(capacity, 250);
            socketListener = new SocketListener(capacity, (int)TCPCmdPacketSize.MAX_SIZE / 4);
            socketListener.SocketClosed += SocketClosed;
            socketListener.SocketConnected += SocketConnected;
            socketListener.SocketReceived += SocketReceived;
            socketListener.SocketSended += SocketSended;

            tcpInPacketPool = new TCPInPacketPool(capacity);
/*            tcpOutPacketPool = new TCPOutPacketPool(capacity * 5);*/
            tcpOutPacketPool = TCPOutPacketPool.getInstance();
            tcpOutPacketPool.initialize(capacity * 5);
            TCPCmdDispatcher.getInstance().initialize();
            dictInPackets = new Dictionary<Socket, TCPInPacket>(capacity);
            gameServerClients = new Dictionary<Socket, GameServerClient>();
        }

        public GameServerClient getClient(Socket socket)
        {
            GameServerClient client = null;
            gameServerClients.TryGetValue(socket, out client);
            return client;
        }

        /// <summary>
        /// 服务器端的侦听对象
        /// </summary>
        private SocketListener socketListener = null;

        /// <summary>
        /// 服务器端的侦听对象
        /// </summary>
        public SocketListener MySocketListener
        {
            get { return socketListener; }
        }

        /// <summary>
        /// 接收的命令包缓冲池
        /// </summary>
        private TCPInPacketPool tcpInPacketPool = null;

        /// <summary>
        /// 发送的命令包缓冲池
        /// </summary>
        private TCPOutPacketPool tcpOutPacketPool = null;

        /// <summary>
        /// 主窗口对象
        /// </summary>
        public Program RootWindow
        {
            get;
            set;
        }

        /// <summary>
        /// 数据库服务对象
        /// </summary>
        public DBManager DBMgr
        {
            get;
            set;
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <param name="port"></param>
        public void Start(string ip, int port)
        {
            socketListener.Init();
            socketListener.Start(ip, port);
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        /// <param name="port"></param>
        public void Stop()
        {
            socketListener.Stop();
        }

        #region 事件处理

        [ThreadStatic]
        public static GameServerClient CurrentClient;

        /// <summary>
        /// 命令包接收完毕后的回调事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private bool TCPCmdPacketEvent(object sender)
        {
            TCPInPacket tcpInPacket = sender as TCPInPacket;
            TCPOutPacket tcpOutPacket = null;
            TCPProcessCmdResults result = TCPProcessCmdResults.RESULT_FAILED;

            // 加锁保证线程安全（NetCoreServer多线程并发访问）
            GameServerClient client = null;
            lock (gameServerClients)
            {
                gameServerClients.TryGetValue(tcpInPacket.CurrentSocket, out client);
            }

            if (client == null)
            {
                // 连接已关闭或未建立，记录日志但不断开（可能是时序问题）
                System.Console.WriteLine($"[DBSERVER-WARN] No client session for cmd {(TCPGameServerCmds)tcpInPacket.PacketCmdID}, socket={Global.GetSocketRemoteEndPoint(tcpInPacket.CurrentSocket)}");
                return true; // 保持连接，等待下一个命令
            }

            CurrentClient = client;
            long processBeginTime = TimeUtil.NowEx();

            try
            {
                result = TCPCmdHandler.ProcessCmd(client, DBMgr, tcpOutPacketPool,
                    tcpInPacket.PacketCmdID, tcpInPacket.GetPacketBytes(),
                    tcpInPacket.PacketDataSize, out tcpOutPacket);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DBSERVER-EX] ProcessCmd exception for cmd {(TCPGameServerCmds)tcpInPacket.PacketCmdID}: {ex.Message}");
                LogManager.WriteException(ex.ToString());
                CurrentClient = null;
                return true; // 不因为单个命令异常就断开整个连接
            }

            long processTime = (TimeUtil.NowEx() - processBeginTime);

            if (result == TCPProcessCmdResults.RESULT_DATA && null != tcpOutPacket)
            {
                socketListener.SendData(tcpInPacket.CurrentSocket, tcpOutPacket);
            }
            else if (result == TCPProcessCmdResults.RESULT_FAILED)
            {
                // 记录失败信息，但不断开连接 — 单命令失败不应影响整个会话
                System.Console.WriteLine($"[DBSERVER-FAIL] ProcessCmd RESULT_FAILED for cmd {(TCPGameServerCmds)tcpInPacket.PacketCmdID}, endpoint={Global.GetSocketRemoteEndPoint(tcpInPacket.CurrentSocket)}");
                LogManager.WriteLog(LogTypes.Error, string.Format("解析并执行命令失败: {0},{1}",
                    (TCPGameServerCmds)tcpInPacket.PacketCmdID,
                    Global.GetSocketRemoteEndPoint(tcpInPacket.CurrentSocket)));
            }

            lock (cmdMoniter)
            {
                int cmdID = tcpInPacket.PacketCmdID;
                PorcessCmdMoniter moniter = null;
                if (!cmdMoniter.TryGetValue(cmdID, out moniter))
                {
                    moniter = new PorcessCmdMoniter(cmdID, processTime);
                    cmdMoniter.Add(cmdID, moniter);
                }
                moniter.onProcessNoWait(processTime);
            }

            CurrentClient = null;
            return true;
        }

        //接收的数据包队列
        private Dictionary<Socket, TCPInPacket> dictInPackets = null;
        //GameServer会话实例集合
        private Dictionary<Socket, GameServerClient> gameServerClients = null;

        /// <summary>
        /// 连接成功通知函数
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SocketConnected(object sender, Socket socket)
        {
            SocketListener sl = sender as SocketListener;
            RootWindow.TotalConnections = sl.ConnectedSocketsCount;

            lock (gameServerClients)
            {
                GameServerClient client = null;
                if (!gameServerClients.TryGetValue(socket, out client))
                {
                    client = new GameServerClient(socket);
                    gameServerClients.Add(socket, client);
                }
            }
        }

        /// <summary>
        /// 断开成功通知函数
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SocketClosed(object sender, Socket socket)
        {
            SocketListener sl = sender as SocketListener;

            lock (dictInPackets)
            {
                if (dictInPackets.ContainsKey(socket))
                {
                    TCPInPacket tcpInPacket = dictInPackets[socket];
                    tcpInPacketPool.Push(tcpInPacket);
                    dictInPackets.Remove(socket);
                }
            }

            lock (gameServerClients)
            {
                GameServerClient client = null;
                if (gameServerClients.TryGetValue(socket, out client))
                {
                    client.release();
                    gameServerClients.Remove(socket);
                }
            }

            RootWindow.TotalConnections = sl.ConnectedSocketsCount;
        }

        /// <summary>
        /// 接收数据通知函数
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private bool SocketReceived(object sender, Socket socket, byte[] buffer, int offset, int count)
        {
            TCPInPacket tcpInPacket = null;
            lock (dictInPackets)
            {
                if (!dictInPackets.TryGetValue(socket, out tcpInPacket))
                {
                    tcpInPacket = tcpInPacketPool.Pop(socket, TCPCmdPacketEvent);
                    dictInPackets[socket] = tcpInPacket;
                }
            }

            if (!tcpInPacket.WriteData(buffer, offset, count))
                return false;

            return true;
        }

        /// <summary>
        /// 发送数据通知函数
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SocketSended(object sender, TCPOutPacket tcpOutPacket)
        {
            tcpOutPacketPool.Push(tcpOutPacket);
        }

        #endregion //事件处理
    }
}
