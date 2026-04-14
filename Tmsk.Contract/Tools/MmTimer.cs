/// <summary>
/// MmTimer - Cross-platform replacement cho Windows winmm.dll timer
/// Sử dụng System.Threading.Timer thay vì winmm.dll P/Invoke
/// Cung cấp high-resolution timer cho game loop
/// </summary>
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace System.Threading
{
    public sealed class MmTimer : IComponent, IDisposable
    {
        // Fields
        private int interval = 1;
        private bool isRunning;
        private MmTimerMode mode;
        private int resolution = 1;
        private Timer _timer;

        // Events
        public event EventHandler Disposed;
        public event EventHandler Tick;

        // Methods
        static MmTimer()
        {
            // Không cần gọi winmm.dll timeGetDevCaps nữa
        }

        public MmTimer()
        {
            this.interval = 1;
            this.resolution = 1;
            this.mode = MmTimerMode.Periodic;
            this.isRunning = false;
        }

        public MmTimer(IContainer container)
            : this()
        {
            container.Add(this);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
            EventHandler disposed = this.Disposed;
            if (disposed != null)
            {
                disposed(this, EventArgs.Empty);
            }
        }

        ~MmTimer()
        {
            Stop();
        }

        private void OnTick(EventArgs e)
        {
            EventHandler tick = this.Tick;
            if (tick != null)
            {
                tick(this, e);
            }
        }

        /// <summary>
        /// Khởi động timer - sử dụng System.Threading.Timer cross-platform
        /// </summary>
        public void Start()
        {
            if (!this.isRunning)
            {
                if (this.Mode == MmTimerMode.Periodic)
                {
                    // Timer periodic - lặp lại mỗi interval ms
                    _timer = new Timer(TimerCallback, null, 0, this.interval);
                }
                else
                {
                    // Timer one-shot - chỉ chạy 1 lần
                    _timer = new Timer(OneShotCallback, null, this.interval, Timeout.Infinite);
                }
                this.isRunning = true;
            }
        }

        private void TimerCallback(object state)
        {
            OnTick(EventArgs.Empty);
        }

        private void OneShotCallback(object state)
        {
            OnTick(EventArgs.Empty);
            Stop();
        }

        /// <summary>
        /// Dừng timer
        /// </summary>
        public void Stop()
        {
            if (this.isRunning)
            {
                _timer?.Dispose();
                _timer = null;
                this.isRunning = false;
            }
        }

        // Properties
        public int Interval
        {
            get
            {
                return this.interval;
            }
            set
            {
                if (value < 1 || value > 1000000)
                {
                    throw new Exception("invalid period");
                }
                this.interval = value;
            }
        }

        public bool IsRunning
        {
            get
            {
                return this.isRunning;
            }
        }

        public MmTimerMode Mode
        {
            get
            {
                return this.mode;
            }
            set
            {
                this.mode = value;
            }
        }

        public ISite Site { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MmTimerCaps
    {
        public int periodMin;
        public int periodMax;
    }

    public enum MmTimerMode
    {
        OneShot,
        Periodic
    }
}
