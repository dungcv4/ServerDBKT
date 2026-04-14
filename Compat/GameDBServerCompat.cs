/// <summary>
/// Compat layer cho GameDBServer .NET 10 migration
/// Cung cấp các type stub còn thiếu
/// </summary>
using System;
using System.Net;

// MySQLParameter đã chuyển sang MySQLDriverCSCompat.cs

// ===== System.Runtime.Remoting stubs =====
namespace System.Runtime.Remoting
{
    namespace Channels
    {
        namespace Tcp
        {
            public class TcpServerChannel : IDisposable
            {
                public TcpServerChannel(string name, int port)
                { Name = name; Port = port; }
                public TcpServerChannel(string name, int port, object sinkProvider)
                { Name = name; Port = port; }
                public string Name { get; set; }
                public int Port { get; set; }
                public void Dispose() { }
            }
            
            public class TcpClientChannel : IDisposable
            {
                public TcpClientChannel() { }
                public TcpClientChannel(string name, object sinkProvider) { }
                public void Dispose() { }
            }
        }

        public static class ChannelServices
        {
            public static void RegisterChannel(object channel, bool ensureSecurity) { }
            public static void UnregisterChannel(object channel) { }
        }

        public class BinaryServerFormatterSinkProvider
        {
            public object TypeFilterLevel { get; set; }
        }

        public class BinaryClientFormatterSinkProvider { }

        /// <summary>
        /// IAuthorizeRemotingConnection - stub cho .NET Remoting
        /// Trong .NET Framework nằm ở System.Runtime.Remoting.Channels
        /// </summary>
        public interface IAuthorizeRemotingConnection
        {
            bool IsConnectingEndPointAuthorized(EndPoint endPoint);
            bool IsConnectingIdentityAuthorized(System.Security.Principal.IIdentity identity);
        }
    }

    namespace Messaging
    {
        /// <summary>
        /// CallContext stub - thay thế .NET Remoting CallContext
        /// Sử dụng AsyncLocal cho cross-platform
        /// </summary>
        public static class CallContext
        {
            private static System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.AsyncLocal<object>> _state 
                = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.AsyncLocal<object>>();

            public static void SetData(string name, object data)
            {
                _state.GetOrAdd(name, _ => new System.Threading.AsyncLocal<object>()).Value = data;
            }

            public static object GetData(string name)
            {
                return _state.TryGetValue(name, out var asyncLocal) ? asyncLocal.Value : null;
            }
        }

        public class CustomErrorsModes
        {
            public static int Off = 0;
        }
    }

    public static class RemotingConfiguration
    {
        public static int CustomErrorsMode { get; set; }
        public static void RegisterWellKnownServiceType(Type type, string objectUri, int mode) { }
    }

    public enum WellKnownObjectMode
    {
        SingleCall = 0,
        Singleton = 1
    }
}

// ===== ComponentAce (zlib.net replacement) =====
namespace ComponentAce.Compression.Libs.zlib
{
    public class ZOutputStream : System.IO.Stream
    {
        private System.IO.Stream _inner;
        private System.IO.Compression.DeflateStream _deflate;
        private bool _compress;

        public ZOutputStream(System.IO.Stream output)
        {
            _inner = output;
            _compress = false;
        }

        public ZOutputStream(System.IO.Stream output, int level)
        {
            _inner = output;
            _compress = true;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, System.IO.SeekOrigin origin) => 0;
        public override void SetLength(long value) { }

        public void finish() => Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner?.Dispose();
            base.Dispose(disposing);
        }
    }

    public static class zlibConst
    {
        public const int Z_DEFAULT_COMPRESSION = -1;
        public const int Z_BEST_COMPRESSION = 9;
    }
}

// PlatformTypes đã chuyển sang Compat/NamespaceStubs.cs (namespace Tmsk.Contract)

// ===== Tmsk.Tools namespace for MmTimer =====
namespace Tmsk.Tools
{
    // MmTimer đã có từ linked Tmsk.Contract/Tools/MmTimer.cs
}

namespace GameDBServer.DB
{
    public static class TianMaCharSet
    {
        public static int ConvertToCodePage { get; set; } = 65001;
    }
}
