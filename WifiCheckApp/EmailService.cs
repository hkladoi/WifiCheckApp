using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WifiCheckApp
{
    public class EmailService
    {
        private const string EmailKey = "UserEmail";
        private const string EmailSaveDateKey = "EmailSaveDate";

        private readonly IPreferences _preferences;

        public EmailService(IPreferences preferences)
        {
            _preferences = preferences;
        }

        public async Task SaveEmail(string email)
        {
            _preferences.Set(EmailKey, email);
            _preferences.Set(EmailSaveDateKey, DateTime.Now.ToString("o"));
        }

        public async Task<string> GetSavedEmail()
        {
            // Check if email exists
            if (!_preferences.ContainsKey(EmailKey) || !_preferences.ContainsKey(EmailSaveDateKey))
            {
                return string.Empty;
            }

            // Check if email was saved more than a week ago
            string savedDateStr = _preferences.Get(EmailSaveDateKey, string.Empty);
            if (string.IsNullOrEmpty(savedDateStr))
            {
                return string.Empty;
            }

            DateTime savedDate = DateTime.Parse(savedDateStr);
            if ((DateTime.Now - savedDate).TotalDays > 7) // Email expires after 7 days
            {
                // Clear expired email
                _preferences.Remove(EmailKey);
                _preferences.Remove(EmailSaveDateKey);
                return string.Empty;
            }

            return _preferences.Get(EmailKey, string.Empty);
        }
    }
}
