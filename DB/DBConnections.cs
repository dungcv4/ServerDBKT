using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MySQLDriverCS;
using System.Data;
using System.Windows;
using Server.Tools;
using System.Threading;
using System.Collections.Concurrent;

namespace GameDBServer.DB
{
    /// <summary>
    /// Quản lý kết nối DB
    /// </summary>
    public class DBConnections
    {
        /// <summary>
        /// Tên DB
        /// </summary>
        public static string dbNames = "";

        /// <summary>
        /// Đối tượng Semaphore
        /// </summary>
        private Semaphore SemaphoreClients = null;

        /// <summary>
        /// Danh sách kết nối đang chờ
        /// </summary>
        private ConcurrentQueue<MySQLConnection> DBConns = new ConcurrentQueue<MySQLConnection>();

        /// <summary>
        /// MUTEX dùng trong LOCK
        /// </summary>
        private object Mutex = new object();

        /// <summary>
        /// Chuỗi kết nói
        /// </summary>
        private string ConnectionString;

        /// <summary>
        /// Tổng số kết nối
        /// </summary>
        private int CurrentCount;

        /// <summary>
        /// Kết nối tối đa
        /// </summary>
        private int MaxCount;

        /// <summary>
        /// Tạo kết nối mới tới Database
        /// </summary>
        /// <param name="connStr"></param>
        public void BuidConnections(MySQLConnectionString connStr, int maxCount)
        {
            //lock (this.Mutex)
            {
                ConnectionString = connStr.AsString;
                MaxCount = maxCount;
                SemaphoreClients = new Semaphore(0, maxCount);

                System.Console.WriteLine($"Creating {maxCount} database connections...");
                for (int i = 0; i < maxCount; i++)
                {
                    MySQLConnection dbConn = CreateAConnection();
                    if (null == dbConn)
                    {
                        throw new Exception(string.Format("Connect to MySQL faild at connection {0}", i));
                    }
                    System.Console.Write($"\rConnected: {i + 1}/{maxCount}");
                }
                System.Console.WriteLine();
                System.Console.WriteLine($"All {maxCount} connections established.");
            }
        }

        /// <summary>
        /// Tạo kết nối
        /// </summary>
        /// <returns></returns>
        private MySQLConnection CreateAConnection()
        {
            try
            {
                MySQLConnection dbConn = null;
                dbConn = new MySQLConnection(ConnectionString);
                dbConn.Open();
                if (!string.IsNullOrEmpty(dbNames))
                {
                    using (MySQLCommand cmd = new MySQLCommand(string.Format("SET names '{0}'", dbNames), dbConn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                //lock (this.Mutex)
                {
                    DBConns.Enqueue(dbConn);
                    CurrentCount++;
                    this.SemaphoreClients.Release();
                }

                return dbConn;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ERROR] Create database connection exception: {ex.Message}");
                LogManager.WriteLog(LogTypes.Exception, string.Format("Create database connection exception: \r\n{0}", ex.ToString()));
            }

            return null;
        }

        /// <summary>
        /// Duy trì kết nối
        /// </summary>
        /// <returns></returns>
        public bool SupplyConnections()
        {
            bool result = false;
            //lock (this.Mutex)
            {
                if (CurrentCount < MaxCount)
                {
                    CreateAConnection();
                }
            }

            return result;
        }

        /// <summary>
        /// Trả về tổng số kết nối đến Database
        /// </summary>
        /// <returns></returns>
        public int GetDBConnsCount()
        {
            //lock (this.Mutex)
            {
                return this.DBConns.Count;
            }
        }

        /// <summary>
        /// Lấy kết nối đến Database trong hàng đợi để thực thi
        /// </summary>
        /// <returns></returns>
        public MySQLConnection PopDBConnection()
        {
            // WaitOne blocks until a connection is available in the pool
            SemaphoreClients.WaitOne();

            if (!this.DBConns.TryDequeue(out MySQLConnection conn))
            {
                SemaphoreClients.Release(); // compensate
                return null;
            }

            if (conn == null)
            {
                return null;
            }

            if (conn.State != ConnectionState.Open)
            {
                try
                {
                    conn.Open();
                    if (!string.IsNullOrEmpty(dbNames))
                    {
                        using (MySQLCommand cmd = new MySQLCommand(string.Format("SET names '{0}'", dbNames), conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        conn.Dispose();
                    }
                    catch
                    {
                    }

                    LogManager.WriteLog(LogTypes.Exception, string.Format("Reopen pooled database connection exception: \r\n{0}", ex));

                    conn = new MySQLConnection(ConnectionString);
                    conn.Open();
                    if (!string.IsNullOrEmpty(dbNames))
                    {
                        using (MySQLCommand cmd = new MySQLCommand(string.Format("SET names '{0}'", dbNames), conn))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }

            return conn;
        }

        /// <summary>
        /// Thực thi kết nối
        /// </summary>
        /// <param name="conn"></param>
        public void PushDBConnection(MySQLConnection conn)
        {
            if (null != conn)
            {
                //lock (this.Mutex)
                {
                    this.DBConns.Enqueue(conn);
                }

                SemaphoreClients.Release();
            }
        }
    }
}
