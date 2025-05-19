using System;
using System.Threading.Tasks;

namespace WifiCheckApp.Services
{
    public class AttendanceSettings
    {
        private const string AttendanceMethodKey = "AttendanceMethod";
        private readonly IPreferences _preferences;
        private AttendanceMethod? _cachedMethod = null;

        public enum AttendanceMethod
        {
            Wifi = 0,
            Location = 1
        }

        public AttendanceSettings(IPreferences preferences)
        {
            _preferences = preferences;
        }

        public AttendanceMethod GetAttendanceMethod()
        {
            // Return cached value if available
            if (_cachedMethod.HasValue)
            {
                return _cachedMethod.Value;
            }

            // Get from preferences
            string methodString = _preferences.Get(AttendanceMethodKey, AttendanceMethod.Wifi.ToString());

            // Try parse the enum
            if (Enum.TryParse<AttendanceMethod>(methodString, out var method))
            {
                _cachedMethod = method;
                return method;
            }

            // Default value if parsing fails
            _cachedMethod = AttendanceMethod.Wifi;
            return AttendanceMethod.Wifi;
        }

        public void SetAttendanceMethod(AttendanceMethod method)
        {
            // Update preferences
            _preferences.Set(AttendanceMethodKey, method.ToString());

            // Update cache
            _cachedMethod = method;
        }
    }
}