/// <summary>
/// Thêm namespace stubs cho các type từ Tmsk.Contract mà GameDBServer thiếu
/// </summary>
using System;
using System.Net;
using System.Security.Principal;

// ===== GameServer.Core.Executor namespace =====
// CoupleWishDefine.cs uses "using GameServer.Core.Executor" for TimeUtil
/// <summary>
/// Namespace GameServer.Core.Executor stubs
/// </summary>
namespace GameServer.Core.Executor
{
    public static class TimeUtil
    {
        public static long NOW() => GameDBServer.Core.TimeUtil.NOW();

        /// <summary>
        /// Returns day of week as 1=Mon ... 7=Sun
        /// </summary>
        public static int GetWeekDay1To7(DateTime dt)
        {
            // DayOfWeek: 0=Sun, 1=Mon ... 6=Sat → remap to 1=Mon ... 7=Sun
            int d = (int)dt.DayOfWeek;
            return d == 0 ? 7 : d;
        }

        /// <summary>
        /// Returns the first day (Monday) of the week containing 'dt' as yyyyMMdd int
        /// </summary>
        public static int MakeFirstWeekday(DateTime dt)
        {
            int d = GetWeekDay1To7(dt); // 1=Mon
            DateTime monday = dt.AddDays(-(d - 1)).Date;
            return monday.Year * 10000 + monday.Month * 100 + monday.Day;
        }
    }
    // TimeUtil đã có từ GameDBServer's own Core/TimeUtil.cs
    // Namespace này chỉ cần tồn tại để "using" không lỗi
}

// PlatformTypes đã có sẵn trong Tmsk.Contract/Const/Enumerations.cs - không cần duplicate

// ===== Fix IAuthorizeRemotingConnection =====
// AuthorizationContext.cs uses "using System.Runtime.Remoting.Messaging"
// nhưng IAuthorizeRemotingConnection nằm ở System.Runtime.Remoting namespace
// Đồng thời IsConnectingIdentityAuthorized dùng IIdentity từ System.Security.Principal
// Interface đã có trong GameDBServerCompat.cs
