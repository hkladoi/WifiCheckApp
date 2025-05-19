using System;
using System.Threading.Tasks;

namespace WifiCheckApp.Services
{
    public class EmailService
    {
        private const string EmailKey = "UserEmail";
        private const string EmailSaveDateKey = "EmailSaveDate";
        private const int EmailExpirationDays = 7;

        private readonly IPreferences _preferences;
        private string _cachedEmail;
        private DateTime _cacheTime = DateTime.MinValue;

        public EmailService(IPreferences preferences)
        {
            _preferences = preferences;
        }

        public async Task SaveEmail(string email)
        {
            // Update preferences
            _preferences.Set(EmailKey, email);
            _preferences.Set(EmailSaveDateKey, DateTime.Now.ToString("o"));

            // Update cache
            _cachedEmail = email;
            _cacheTime = DateTime.Now;
        }

        public async Task<string> GetSavedEmail()
        {
            // Return cached email if it's still valid (cache for 1 minute)
            if (!string.IsNullOrEmpty(_cachedEmail) &&
                (DateTime.Now - _cacheTime).TotalMinutes < 1)
            {
                return _cachedEmail;
            }

            // Check if email exists
            if (!_preferences.ContainsKey(EmailKey) || !_preferences.ContainsKey(EmailSaveDateKey))
            {
                _cachedEmail = string.Empty;
                return string.Empty;
            }

            // Check if email was saved more than a week ago
            string savedDateStr = _preferences.Get(EmailSaveDateKey, string.Empty);
            if (string.IsNullOrEmpty(savedDateStr))
            {
                _cachedEmail = string.Empty;
                return string.Empty;
            }

            try
            {
                DateTime savedDate = DateTime.Parse(savedDateStr);
                if ((DateTime.Now - savedDate).TotalDays > EmailExpirationDays)
                {
                    // Clear expired email
                    _preferences.Remove(EmailKey);
                    _preferences.Remove(EmailSaveDateKey);
                    _cachedEmail = string.Empty;
                    return string.Empty;
                }

                // Update cache
                _cachedEmail = _preferences.Get(EmailKey, string.Empty);
                _cacheTime = DateTime.Now;
                return _cachedEmail;
            }
            catch (Exception)
            {
                // Handle date parsing errors
                _cachedEmail = string.Empty;
                return string.Empty;
            }
        }
    }
}