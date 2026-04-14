/// <summary>
/// MySQLDriverCS → MySqlConnector compatibility layer (comprehensive version)
/// Maps old MySQLDriverCS types to modern MySqlConnector equivalents
/// Hỗ trợ đầy đủ API: MySQLSelectCommand.Table, MySQLDataAdapter, MySQLException, etc.
/// </summary>
using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Text;

namespace MySQLDriverCS
{
    /// <summary>
    /// Chuỗi kết nối MySQL - tương thích MySQLDriverCS.MySQLConnectionString
    /// </summary>
    public class MySQLConnectionString
    {
        public string Host { get; set; }
        public string Database { get; set; }
        public string UserID { get; set; }
        public string Password { get; set; }
        public int Port { get; set; } = 3306;
        public int ConnectionTimeout { get; set; } = 30;

        public MySQLConnectionString(string host, string database, string userId, string password, int port = 3306)
        {
            Host = host; Database = database; UserID = userId; Password = password; Port = port;
        }
        public MySQLConnectionString() { }

        public string AsString => ToString();

        public override string ToString()
        {
            return $"Server={Host};Port={Port};Database={Database};User={UserID};Password={Password};Connection Timeout={ConnectionTimeout};SslMode=Preferred;AllowPublicKeyRetrieval=true;";
        }
    }

    /// <summary>
    /// MySQLConnection wrapper
    /// </summary>
    public class MySQLConnection : IDisposable
    {
        private MySqlConnector.MySqlConnection _inner;
        public MySQLConnection(MySQLConnectionString connStr) { _inner = new MySqlConnector.MySqlConnection(connStr.ToString()); }
        public MySQLConnection(string connectionString) { _inner = new MySqlConnector.MySqlConnection(connectionString); }
        public MySQLConnection() { _inner = new MySqlConnector.MySqlConnection(); }

        public MySqlConnector.MySqlConnection InnerConnection => _inner;
        public string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public ConnectionState State => _inner.State;
        public void Open() => _inner.Open();
        public void Close() => _inner.Close();
        
        public MySqlConnector.MySqlTransaction BeginTransaction()
        {
            return _inner.BeginTransaction();
        }

        public void Dispose() { _inner?.Dispose(); }
    }

    /// <summary>
    /// MySQLCommand wrapper
    /// </summary>
    public class MySQLCommand : IDisposable
    {
        private MySqlConnector.MySqlCommand _inner;
        public MySQLCommand(string cmdText, MySQLConnection conn)
        { _inner = new MySqlConnector.MySqlCommand(cmdText, conn?.InnerConnection); }
        public MySQLCommand(string cmdText) { _inner = new MySqlConnector.MySqlCommand(cmdText); }
        public MySQLCommand() { _inner = new MySqlConnector.MySqlCommand(); }

        public MySqlConnector.MySqlCommand InnerCommand => _inner;
        public string CommandText { get => _inner.CommandText; set => _inner.CommandText = value; }
        public MySQLConnection Connection { get; set; }
        public MySqlConnector.MySqlParameterCollection Parameters => _inner.Parameters;
        
        public int CommandTimeout { get => _inner.CommandTimeout; set => _inner.CommandTimeout = value; }
        public CommandType CommandType { get => _inner.CommandType; set => _inner.CommandType = value; }
        public MySqlConnector.MySqlTransaction Transaction 
        { 
            get => _inner.Transaction; 
            set => _inner.Transaction = value; 
        }

        public int ExecuteNonQuery() => _inner.ExecuteNonQuery();
        public MySQLDataReader ExecuteReaderEx() => new MySQLDataReader(_inner.ExecuteReader());
        public MySQLDataReader ExecuteReader() => new MySQLDataReader(_inner.ExecuteReader());
        public object ExecuteScalar() => _inner.ExecuteScalar();
        public void Dispose() { _inner?.Dispose(); }
    }

    /// <summary>
    /// MySQLDataReader wrapper — buffers all data into DataTable immediately.
    /// Mục đích: giải phóng MySqlConnection NGAY sau khi query thực thi.
    /// Game code cũ thường không Close/Dispose reader → connection bị giữ "in use"
    /// → crash "MySqlConnection already in use" khi reuse connection từ pool.
    /// Giải pháp: load tất cả data vào DataTable, close live reader ngay lập tức.
    /// </summary>
    public class MySQLDataReader : IDisposable
    {
        private DataTable _data;
        private int _currentRow = -1;

        /// <summary>Load tất cả rows từ live reader vào DataTable, close reader ngay.</summary>
        public MySQLDataReader(MySqlConnector.MySqlDataReader reader)
        {
            _data = new DataTable();
            try
            {
                _data.Load(reader);   // Reads ALL rows into memory
            }
            finally
            {
                try { reader.Close(); } catch { }   // Release connection "in use" immediately
            }
        }

        public MySQLDataReader(DataTable dt) { _data = dt ?? new DataTable(); }
        public MySQLDataReader() { _data = new DataTable(); }

        public bool Read()
        {
            _currentRow++;
            return _data != null && _currentRow < _data.Rows.Count;
        }

        public void Close() { _currentRow = _data?.Rows.Count ?? 0; }
        public bool IsClosed => _data == null || _currentRow >= (_data?.Rows.Count ?? 0);
        public int FieldCount => _data?.Columns.Count ?? 0;
        public string GetName(int i) => _data?.Columns[i]?.ColumnName;
        public Type GetFieldType(int i) => _data?.Columns[i]?.DataType;
        public DataTable GetSchemaTable() => _data;

        private DataRow CurrentRow => (_data != null && _currentRow >= 0 && _currentRow < _data.Rows.Count)
            ? _data.Rows[_currentRow] : null;

        public object this[int i] => CurrentRow?[i];
        public object this[string name] => CurrentRow?[name];

        public bool IsDBNull(int i) => CurrentRow == null || CurrentRow.IsNull(i);
        public string GetString(int i) => CurrentRow?[i]?.ToString();
        public int GetInt32(int i) { var v = CurrentRow?[i]; return v == null || v is DBNull ? 0 : Convert.ToInt32(v); }
        public long GetInt64(int i) { var v = CurrentRow?[i]; return v == null || v is DBNull ? 0L : Convert.ToInt64(v); }
        public double GetDouble(int i) { var v = CurrentRow?[i]; return v == null || v is DBNull ? 0.0 : Convert.ToDouble(v); }
        public float GetFloat(int i) { var v = CurrentRow?[i]; return v == null || v is DBNull ? 0f : Convert.ToSingle(v); }
        public DateTime GetDateTime(int i) { var v = CurrentRow?[i]; return v == null || v is DBNull ? DateTime.MinValue : Convert.ToDateTime(v); }
        public object GetValue(int i) => CurrentRow?[i];

        public void Dispose() { _data?.Dispose(); _data = null; }
    }

    /// <summary>
    /// MySQLSelectCommand - wrapper cho SELECT queries (DataTable pattern)
    /// 
    /// MySQLDriverCS gốc: excute query → lưu kết quả vào DataTable (Table property)
    /// Code GameDBServer: cmd.Table.Rows[i] / cmd.Table.Rows.Count
    /// </summary>
    public class MySQLSelectCommand : IDisposable
    {
        /// <summary>
        /// DataTable chứa kết quả query - đây là property chính mà code game sử dụng
        /// </summary>
        public DataTable Table { get; private set; }

        private void ExecuteAndFill(MySqlConnector.MySqlCommand cmd)
        {
            Table = new DataTable();
            using var adapter = new MySqlConnector.MySqlDataAdapter(cmd);
            adapter.Fill(Table);
        }

        /// <summary>
        /// Constructor 1: columns, tables, whereConditions (object[,]), orderBy, limit
        /// Pattern: new MySQLSelectCommand(conn, cols, tables, new object[,] { {"col","=",val} }, orderBy, limit)
        /// </summary>
        public MySQLSelectCommand(MySQLConnection conn, string[] columnNames, string[] tableNames,
            object[,] whereConditions, string orderByCondition, int limitCount = 0)
        {
            string columns = (columnNames != null && columnNames.Length > 0) ? string.Join(", ", columnNames) : "*";
            string tables = string.Join(", ", tableNames ?? new string[] { "" });
            string sql = $"SELECT {columns} FROM {tables}";

            var cmd = new MySqlConnector.MySqlCommand();
            cmd.Connection = conn.InnerConnection;

            if (whereConditions != null && whereConditions.GetLength(0) > 0)
            {
                var clauses = new List<string>();
                for (int i = 0; i < whereConditions.GetLength(0); i++)
                {
                    string col = whereConditions[i, 0]?.ToString();
                    string op = whereConditions[i, 1]?.ToString();
                    object val = whereConditions[i, 2];
                    string paramName = $"@wp{i}";
                    clauses.Add($"{col} {op} {paramName}");
                    cmd.Parameters.AddWithValue(paramName, val);
                }
                sql += " WHERE " + string.Join(" AND ", clauses);
            }

            if (!string.IsNullOrEmpty(orderByCondition)) sql += $" ORDER BY {orderByCondition}";
            if (limitCount > 0) sql += $" LIMIT {limitCount}";

            cmd.CommandText = sql;
            ExecuteAndFill(cmd);
            cmd.Dispose();
        }

        /// <summary>
        /// Constructor 2: columns, tables, joinTables, joinOnCmds, whereStr, orderBy, limit
        /// Pattern: new MySQLSelectCommand(conn, cols, tables, joins, joinOns, where, orderBy, limit)
        /// </summary>
        public MySQLSelectCommand(MySQLConnection conn, string[] columnNames, string[] tableNames,
            string[] joinTableName, string[] joinOnCommands, string whereCondition, string orderByCondition,
            string limit)
        {
            string columns = (columnNames != null && columnNames.Length > 0) ? string.Join(", ", columnNames) : "*";
            string tables = string.Join(", ", tableNames ?? new string[] { "" });
            string sql = $"SELECT {columns} FROM {tables}";

            if (joinTableName != null && joinOnCommands != null)
            {
                for (int i = 0; i < joinTableName.Length && i < joinOnCommands.Length; i++)
                    sql += $" JOIN {joinTableName[i]} ON {joinOnCommands[i]}";
            }

            if (!string.IsNullOrEmpty(whereCondition)) sql += $" WHERE {whereCondition}";
            if (!string.IsNullOrEmpty(orderByCondition)) sql += $" ORDER BY {orderByCondition}";
            if (!string.IsNullOrEmpty(limit)) sql += $" LIMIT {limit}";

            using var cmd = new MySqlConnector.MySqlCommand(sql, conn.InnerConnection);
            ExecuteAndFill(cmd);
        }

        /// <summary>
        /// Constructor 3: columns, tables, whereConditions (object[,]), orderBy, limit, 
        ///                 joinTableName, joinOnCommands, groupBy, having
        /// 10-param constructor pattern
        /// </summary>
        public MySQLSelectCommand(MySQLConnection conn, string[] columnNames, string[] tableNames,
            object[,] whereConditions, string orderByCondition, string limit,
            string[] joinTableName, string[] joinOnCommands, string groupBy, string having)
        {
            string columns = (columnNames != null && columnNames.Length > 0) ? string.Join(", ", columnNames) : "*";
            string tables = string.Join(", ", tableNames ?? new string[] { "" });
            string sql = $"SELECT {columns} FROM {tables}";

            var cmd = new MySqlConnector.MySqlCommand();
            cmd.Connection = conn.InnerConnection;

            if (joinTableName != null && joinOnCommands != null)
            {
                for (int i = 0; i < joinTableName.Length && i < joinOnCommands.Length; i++)
                    sql += $" JOIN {joinTableName[i]} ON {joinOnCommands[i]}";
            }

            if (whereConditions != null && whereConditions.GetLength(0) > 0)
            {
                var clauses = new List<string>();
                for (int i = 0; i < whereConditions.GetLength(0); i++)
                {
                    string col = whereConditions[i, 0]?.ToString();
                    string op = whereConditions[i, 1]?.ToString();
                    object val = whereConditions[i, 2];
                    string paramName = $"@wp{i}";
                    clauses.Add($"{col} {op} {paramName}");
                    cmd.Parameters.AddWithValue(paramName, val);
                }
                sql += " WHERE " + string.Join(" AND ", clauses);
            }

            if (!string.IsNullOrEmpty(groupBy)) sql += $" GROUP BY {groupBy}";
            if (!string.IsNullOrEmpty(having)) sql += $" HAVING {having}";
            if (!string.IsNullOrEmpty(orderByCondition)) sql += $" ORDER BY {orderByCondition}";
            if (!string.IsNullOrEmpty(limit)) sql += $" LIMIT {limit}";

            cmd.CommandText = sql;
            ExecuteAndFill(cmd);
            cmd.Dispose();
        }

        /// <summary>
        /// Constructor 4: SQL đơn giản
        /// </summary>
        public MySQLSelectCommand(MySQLConnection conn, string sql)
        {
            using var cmd = new MySqlConnector.MySqlCommand(sql, conn.InnerConnection);
            ExecuteAndFill(cmd);
        }

        /// <summary>
        /// Constructor 6: 10 parameters (GameDBServer specific pattern)
        /// Pattern: conn, cols, tables, where(object[,]), joinTables(string[]?), orderBy(string[,]), limitRow(bool), limitStart(int), limitCount(int), flag(bool)
        /// </summary>
        public MySQLSelectCommand(MySQLConnection conn, string[] columnNames, string[] tableNames,
            object[,] whereConditions, string[] joinTableName, string[,] orderByCondition,
            bool limitRow, int limitStart, int limitCount, bool flag)
        {
            string columns = (columnNames != null && columnNames.Length > 0) ? string.Join(", ", columnNames) : "*";
            string tables = string.Join(", ", tableNames ?? new string[] { "" });
            string sql = $"SELECT {columns} FROM {tables}";

            var cmd = new MySqlConnector.MySqlCommand();
            cmd.Connection = conn.InnerConnection;

            if (joinTableName != null && joinTableName.Length > 0)
            {
                // Simple join assumption or ignore if array is empty (GameDBServer usually passes null)
                // Just to prevent crash if not null and not handled:
                sql += " " + string.Join(" ", joinTableName);
            }

            if (whereConditions != null && whereConditions.GetLength(0) > 0)
            {
                var clauses = new List<string>();
                int paramIdx = 0;
                for (int i = 0; i < whereConditions.GetLength(0); i++)
                {
                    string col = whereConditions[i, 0]?.ToString();
                    string op = whereConditions[i, 1]?.ToString();
                    object val = whereConditions[i, 2];
                    string paramName = $"@wp{paramIdx++}";
                    clauses.Add($"{col} {op} {paramName}");
                    cmd.Parameters.AddWithValue(paramName, val);
                }
                sql += " WHERE " + string.Join(" AND ", clauses);
            }

            if (orderByCondition != null && orderByCondition.GetLength(0) > 0)
            {
                var orders = new List<string>();
                for (int i = 0; i < orderByCondition.GetLength(0); i++)
                {
                    orders.Add($"{orderByCondition[i, 0]} {orderByCondition[i, 1]}");
                }
                sql += " ORDER BY " + string.Join(", ", orders);
            }

            // GameDBServer often sets limitCount = 4 and limitStart = 0, along with limitRow = true
            if (limitRow || limitCount > 0)
            {
                sql += $" LIMIT {limitStart}, {limitCount}";
            }

            cmd.CommandText = sql;
            ExecuteAndFill(cmd);
            cmd.Dispose();
        }

        /// <summary>
        /// Constructor 7: 6 parameters (conn, cols, tables, where, join, orderBy)
        /// Used in DB/DBRoleInfo.cs line 1567
        /// </summary>
        public MySQLSelectCommand(MySQLConnection conn, string[] columnNames, string[] tableNames,
            object[,] whereConditions, string[] joinTableName, string[,] orderByCondition)
            : this(conn, columnNames, tableNames, whereConditions, joinTableName, orderByCondition, false, 0, 0, false)
        {
        }


        // Read() giả lập cho backward compat (ít code dùng pattern này)
        private int _currentRow = -1;
        public bool Read()
        {
            _currentRow++;
            return Table != null && _currentRow < Table.Rows.Count;
        }
        
        public int FieldCount => Table?.Columns.Count ?? 0;
        public string GetName(int i) => Table?.Columns[i]?.ColumnName;
        public object this[int i] => Table?.Rows[_currentRow]?[i];
        public object this[string name] => Table?.Rows[_currentRow]?[name];

        public void Dispose()
        {
            Table?.Dispose();
        }
    }

    /// <summary>
    /// MySQLInsertCommand
    /// </summary>
    public class MySQLInsertCommand
    {
        public static int Insert(MySQLConnection conn, string tableName, string[] columnNames, string[] values)
        {
            string cols = string.Join(", ", columnNames);
            string vals = string.Join(", ", Array.ConvertAll(values, v => $"'{MySqlConnector.MySqlHelper.EscapeString(v ?? "")}'"));
            string sql = $"INSERT INTO {tableName} ({cols}) VALUES ({vals})";
            using var cmd = new MySqlConnector.MySqlCommand(sql, conn.InnerConnection);
            return cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// MySQLUpdateCommand
    /// </summary>
    public class MySQLUpdateCommand
    {
        public static int Update(MySQLConnection conn, string tableName, string[] setExpressions, string whereCondition)
        {
            string sets = string.Join(", ", setExpressions);
            string sql = $"UPDATE {tableName} SET {sets}";
            if (!string.IsNullOrEmpty(whereCondition)) sql += $" WHERE {whereCondition}";
            using var cmd = new MySqlConnector.MySqlCommand(sql, conn.InnerConnection);
            return cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// MySQLDataAdapter wrapper
    /// </summary>
    public class MySQLDataAdapter : IDisposable
    {
        private MySqlConnector.MySqlDataAdapter _inner;
        public MySQLDataAdapter(string sql, MySQLConnection conn) { _inner = new MySqlConnector.MySqlDataAdapter(sql, conn.InnerConnection); }
        public MySQLDataAdapter(MySQLCommand cmd) { _inner = new MySqlConnector.MySqlDataAdapter(cmd.InnerCommand); }
        public MySQLDataAdapter() { _inner = new MySqlConnector.MySqlDataAdapter(); }

        public MySQLCommand SelectCommand 
        { 
            get => null; // stub
            set 
            { 
                if (value != null) _inner.SelectCommand = value.InnerCommand;
            } 
        }

        public int Fill(DataSet ds) => _inner.Fill(ds);
        public int Fill(DataSet ds, string srcTable) => _inner.Fill(ds, srcTable);
        public int Fill(DataTable dt) => _inner.Fill(dt);
        public DataTable[] FillSchema(DataSet ds, SchemaType schemaType) => _inner.FillSchema(ds, schemaType);
        public void Dispose() { _inner?.Dispose(); }
    }

    /// <summary>
    /// MySQLException wrapper
    /// </summary>
    public class MySQLException : Exception
    {
        public int Number { get; set; }
        public MySQLException() : base() { }
        public MySQLException(string message) : base(message) { }
        public MySQLException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// MySQLParameter wrapper
    /// </summary>
    public class MySQLParameter
    {
        public string ParameterName { get; set; }
        public object Value { get; set; }
        public System.Data.DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public MySQLParameter(string name, object value) { ParameterName = name; Value = value; }
        public MySQLParameter() { }

        public MySqlConnector.MySqlParameter ToMySqlParameter()
        {
            var p = new MySqlConnector.MySqlParameter(ParameterName, Value);
            p.Direction = Direction;
            return p;
        }
    }
}

// ===== DataHelper2 stub =====
namespace Server.Tools
{
    /// <summary>
    /// DataHelper2 - stub cho Tmsk.Contract references  
    /// Cung cấp serialization helpers bằng protobuf-net
    /// </summary>
    public static class DataHelper2
    {
        public static byte[] ObjectToBytes<T>(T obj)
        {
            try
            {
                using var ms = new System.IO.MemoryStream();
                ProtoBuf.Serializer.Serialize(ms, obj);
                return ms.ToArray();
            }
            catch { return null; }
        }

        public static T BytesToObject<T>(byte[] data, int offset, int length)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(data, offset, length);
                return ProtoBuf.Serializer.Deserialize<T>(ms);
            }
            catch { return default; }
        }

        public static T BytesToObject<T>(byte[] data)
        {
            return BytesToObject<T>(data, 0, data?.Length ?? 0);
        }

        public static void SortBytes(byte[] buffer, int offset, int length) { }

        public static string Bytes2HexString(byte[] b)
        {
            if (b == null) return string.Empty;
            return BitConverter.ToString(b).Replace("-", "").ToLower();
        }

        public static void WriteExceptionLogEx(Exception ex, string msg)
        {
            Server.Tools.LogManager.WriteExceptionUseCache(msg + "\r\n" + ex.ToString());
        }
    }
}

// ===== System.Windows stub =====
namespace System.Windows
{
    public class Application
    {
        public static void DoEvents() { }
    }
}
