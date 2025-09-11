using System;

namespace FireIncidents.Services
{
    /// <summary>
    /// Helper class for handling Greece timezone conversions
    /// Greece uses EET (UTC+2) in winter and EEST (UTC+3) in summer
    /// </summary>
    public static class GreeceTimeZoneHelper
    {
        private static readonly TimeZoneInfo _greeceTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
        
        /// <summary>
        /// Gets the current time in Greece timezone
        /// </summary>
        public static DateTime GetCurrentGreeceTime()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _greeceTimeZone);
        }
        
        /// <summary>
        /// Converts UTC time to Greece timezone
        /// </summary>
        public static DateTime ConvertFromUtc(DateTime utcDateTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, _greeceTimeZone);
        }
        
        /// <summary>
        /// Converts Greece local time to UTC
        /// </summary>
        public static DateTime ConvertToUtc(DateTime greeceDateTime)
        {
            return TimeZoneInfo.ConvertTimeToUtc(greeceDateTime, _greeceTimeZone);
        }
        
        /// <summary>
        /// Converts a DateTime to Greece timezone, handling different DateTimeKind values
        /// </summary>
        public static DateTime ToGreeceTime(DateTime dateTime)
        {
            return dateTime.Kind switch
            {
                DateTimeKind.Utc => ConvertFromUtc(dateTime),
                DateTimeKind.Local => TimeZoneInfo.ConvertTime(dateTime, _greeceTimeZone),
                DateTimeKind.Unspecified => dateTime, // Assume already in Greece time
                _ => dateTime
            };
        }
        
        /// <summary>
        /// Gets the Greece TimeZoneInfo object
        /// </summary>
        public static TimeZoneInfo GreeceTimeZone => _greeceTimeZone;
    }
}