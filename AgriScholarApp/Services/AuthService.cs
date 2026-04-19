using Microsoft.Maui.Storage;

namespace AgriScholarApp.Services
{
    public static class AuthService
    {
        private const string IsLoggedInKey = "is_logged_in";
        private const string FirebaseAdminTokenKey = "firebase_admin_id_token";
        private const string ScholarRememberMeKey = "scholar_remember_me";
        private const string ScholarEmailKey = "scholar_email";

        public static bool IsLoggedIn => Preferences.Default.Get(IsLoggedInKey, false);

        public static bool Login(string username, string password)
        {
            var ok = username == "admin" && password == "admin123";
            if (ok)
            {
                Preferences.Default.Set(IsLoggedInKey, true);
            }

            return ok;
        }

        public static void Logout()
        {
            Preferences.Default.Set(IsLoggedInKey, false);
            Preferences.Default.Remove(FirebaseAdminTokenKey);
            Preferences.Default.Remove(ScholarRememberMeKey);
            Preferences.Default.Remove(ScholarEmailKey);
        }
    }
}
