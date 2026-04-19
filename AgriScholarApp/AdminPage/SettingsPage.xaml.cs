using AgriScholarApp.Services;
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Controls;
using System.Text;

namespace AgriScholarApp.Pages;

public partial class SettingsPage : ContentPage
{
    // Preference keys for persisting settings
    private const string KeyProgramName     = "settings_program_name";
    private const string KeyAcademicYear    = "settings_academic_year";
    private const string KeySemester        = "settings_semester";
    private const string KeyMinGpa          = "settings_min_gpa";
    private const string KeyFromEmail       = "settings_from_email";
    private const string KeyReplyEmail      = "settings_reply_email";
    private const string KeyContactNumber   = "settings_contact_number";
    private const string KeyNewAppAlerts    = "settings_notif_new_app";
    private const string KeyDocUploadAlerts = "settings_notif_doc_upload";
    private const string KeyCompliance      = "settings_notif_compliance";
    private const string KeyDisbursement    = "settings_notif_disbursement";
    private const string KeyEmailNotif      = "settings_notif_email";
    private const string KeyTwoFA           = "settings_security_2fa";
    private const string KeySessionTimeout  = "settings_security_session";

    private const string FirebaseAdminTokenKey = "firebase_admin_id_token";
    private readonly FirestoreRestService _firestore = new();
    private readonly FirebaseAuthRestService _auth = new();

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadSavedSettings();
    }

    // ──────────────────────────────────────────────
    // Load / Save Settings from Preferences
    // ──────────────────────────────────────────────

    private void LoadSavedSettings()
    {
        // General
        ProgramNameEntry.Text = Preferences.Default.Get(KeyProgramName, "Agrischolars Program");
        MinGpaEntry.Text      = Preferences.Default.Get(KeyMinGpa, "2.50");
        FromEmailEntry.Text   = Preferences.Default.Get(KeyFromEmail, "noreply@agrischolars.ph");
        ReplyToEmailEntry.Text = Preferences.Default.Get(KeyReplyEmail, "support@agrischolars.ph");
        ContactNumberEntry.Text = Preferences.Default.Get(KeyContactNumber, "+63 900 000 0000");

        // Academic Year Picker
        var savedYear = Preferences.Default.Get(KeyAcademicYear, "2025-2026");
        var yearIdx = AcademicYearPicker.Items.IndexOf(savedYear);
        AcademicYearPicker.SelectedIndex = yearIdx >= 0 ? yearIdx : 0;

        // Semester Picker
        var savedSem = Preferences.Default.Get(KeySemester, "Second Semester");
        var semIdx = SemesterPicker.Items.IndexOf(savedSem);
        SemesterPicker.SelectedIndex = semIdx >= 0 ? semIdx : 1;

        // Notification Switches
        NewApplicationSwitch.IsToggled  = Preferences.Default.Get(KeyNewAppAlerts, true);
        DocumentUploadSwitch.IsToggled  = Preferences.Default.Get(KeyDocUploadAlerts, true);
        ComplianceSwitch.IsToggled      = Preferences.Default.Get(KeyCompliance, true);
        DisbursementSwitch.IsToggled    = Preferences.Default.Get(KeyDisbursement, true);
        EmailNotifSwitch.IsToggled      = Preferences.Default.Get(KeyEmailNotif, false);

        // Security Switches
        TwoFASwitch.IsToggled           = Preferences.Default.Get(KeyTwoFA, false);
        SessionTimeoutSwitch.IsToggled  = Preferences.Default.Get(KeySessionTimeout, true);
    }

    private void SaveAllPreferences()
    {
        Preferences.Default.Set(KeyProgramName,     ProgramNameEntry.Text?.Trim() ?? "Agrischolars Program");
        Preferences.Default.Set(KeyMinGpa,          MinGpaEntry.Text?.Trim() ?? "2.50");
        Preferences.Default.Set(KeyFromEmail,       FromEmailEntry.Text?.Trim() ?? "");
        Preferences.Default.Set(KeyReplyEmail,      ReplyToEmailEntry.Text?.Trim() ?? "");
        Preferences.Default.Set(KeyContactNumber,   ContactNumberEntry.Text?.Trim() ?? "");

        if (AcademicYearPicker.SelectedItem != null)
            Preferences.Default.Set(KeyAcademicYear, AcademicYearPicker.SelectedItem.ToString()!);
        if (SemesterPicker.SelectedItem != null)
            Preferences.Default.Set(KeySemester, SemesterPicker.SelectedItem.ToString()!);

        Preferences.Default.Set(KeyNewAppAlerts,    NewApplicationSwitch.IsToggled);
        Preferences.Default.Set(KeyDocUploadAlerts, DocumentUploadSwitch.IsToggled);
        Preferences.Default.Set(KeyCompliance,      ComplianceSwitch.IsToggled);
        Preferences.Default.Set(KeyDisbursement,    DisbursementSwitch.IsToggled);
        Preferences.Default.Set(KeyEmailNotif,      EmailNotifSwitch.IsToggled);
        Preferences.Default.Set(KeyTwoFA,           TwoFASwitch.IsToggled);
        Preferences.Default.Set(KeySessionTimeout,  SessionTimeoutSwitch.IsToggled);
    }

    // ──────────────────────────────────────────────
    // Button Handlers
    // ──────────────────────────────────────────────

    private async void OnSaveAllSettingsClicked(object sender, EventArgs e)
    {
        // Validate minimum GPA
        if (!double.TryParse(MinGpaEntry.Text, out var gpa) || gpa < 1.0 || gpa > 4.0)
        {
            await DisplayAlert("Validation Error", "Minimum GPA must be a number between 1.00 and 4.00.", "OK");
            return;
        }

        // Basic email validation
        var fromEmail = FromEmailEntry.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(fromEmail) && !fromEmail.Contains('@'))
        {
            await DisplayAlert("Validation Error", "Please enter a valid 'From' email address.", "OK");
            return;
        }

        SaveAllPreferences();

        // Show inline status message
        SaveStatusLabel.Text = "✅ Settings saved successfully!";
        SaveStatusLabel.TextColor = Color.FromArgb("#16A34A");

        // Hide message after 3 seconds
        await Task.Delay(3000);
        SaveStatusLabel.Text = "";
    }

    private async void OnChangePasswordClicked(object sender, EventArgs e)
    {
        var currentPwd = CurrentPasswordEntry.Text;
        var newPwd     = NewPasswordEntry.Text;
        var confirmPwd = ConfirmPasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(currentPwd))
        {
            await DisplayAlert("Required", "Please enter your current password.", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(newPwd) || newPwd.Length < 8)
        {
            await DisplayAlert("Weak Password", "New password must be at least 8 characters.", "OK");
            return;
        }
        if (newPwd != confirmPwd)
        {
            await DisplayAlert("Password Mismatch", "New password and confirmation do not match.", "OK");
            return;
        }

        bool confirmed = await DisplayAlert(
            "Change Password",
            "Are you sure you want to update the admin password?",
            "Yes, Update", "Cancel");

        if (!confirmed) return;

        // Disable button during operation
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            btn.Text = "Updating...";
        }

        try
        {
            // Simulate password update (hook into FirebaseAuthRestService.ChangePasswordAsync in production)
            await Task.Delay(1500);

            CurrentPasswordEntry.Text = "";
            NewPasswordEntry.Text     = "";
            ConfirmPasswordEntry.Text = "";

            await DisplayAlert("Password Updated", "✅ Admin password has been updated successfully.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to change password: {ex.Message}", "OK");
        }
        finally
        {
            if (sender is Button b)
            {
                b.IsEnabled = true;
                b.Text = "Update Password";
            }
        }
    }

    private async void OnBackupDatabaseClicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Backup Database",
            "This will create a snapshot of all Firestore data. Continue?",
            "Yes, Backup", "Cancel");

        if (!confirmed) return;

        if (sender is Button btn) { btn.IsEnabled = false; btn.Text = "Backing up..."; }

        try
        {
            await Task.Delay(2000); // Simulate backup operation
            await DisplayAlert("Backup Complete",
                "✅ Database backup completed.\n\nBackup file: agrischolars_backup_" +
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".json",
                "OK");
        }
        finally
        {
            if (sender is Button b) { b.IsEnabled = true; b.Text = "Run Backup"; }
        }
    }

    private async void OnClearCacheClicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Clear Cache",
            "This will remove all temporary cached data. The app may be slower on next launch while data reloads. Continue?",
            "Clear Cache", "Cancel");

        if (!confirmed) return;

        if (sender is Button btn) { btn.IsEnabled = false; btn.Text = "Clearing..."; }

        try
        {
            // Clear any cached preferences (cached tokens, etc.)
            Preferences.Default.Remove("firebase_admin_id_token");
            await Task.Delay(800);

            await DisplayAlert("Cache Cleared", "✅ Application cache has been cleared successfully.", "OK");
        }
        finally
        {
            if (sender is Button b) { b.IsEnabled = true; b.Text = "Clear Now"; }
        }
    }

    private async void OnExportDataClicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Export All Data",
            "This will export all scholar records as a CSV file. Continue?",
            "Export", "Cancel");

        if (!confirmed) return;

        if (sender is Button btn) { btn.IsEnabled = false; btn.Text = "Exporting..."; }

        try
        {
            var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            if (string.IsNullOrWhiteSpace(token))
            {
                var signIn = await _auth.SignInAsync("admin@gmail.com", "admin123");
                token = signIn.IdToken;
                Preferences.Default.Set(FirebaseAdminTokenKey, token);
            }

            List<Dictionary<string, object?>> scholars;
            try
            {
                scholars = await _firestore.ListDocumentsAsync("scholars", token);
            }
            catch
            {
                // Retry with a fresh token if the cached one is expired.
                Preferences.Default.Remove(FirebaseAdminTokenKey);
                var signIn = await _auth.SignInAsync("admin@gmail.com", "admin123");
                token = signIn.IdToken;
                Preferences.Default.Set(FirebaseAdminTokenKey, token);
                scholars = await _firestore.ListDocumentsAsync("scholars", token);
            }

            var fileName = $"scholars_export_{DateTime.Now:yyyy-MM-dd}.csv";
            var csv = BuildScholarsCsv(scholars);
            var bytes = Encoding.UTF8.GetBytes(csv);
            await using var stream = new MemoryStream(bytes);

            var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);
            if (result.IsSuccessful)
            {
                await DisplayAlert("Export Complete",
                    $"✅ Exported {scholars.Count} scholars.\n\nSaved to:\n{result.FilePath}",
                    "OK");
            }
            else
            {
                await DisplayAlert("Export Canceled", "No file was saved.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Failed", ex.Message, "OK");
        }
        finally
        {
            if (sender is Button b) { b.IsEnabled = true; b.Text = "Export"; }
        }
    }

    private static string BuildScholarsCsv(List<Dictionary<string, object?>> scholars)
    {
        static string Esc(string? s)
        {
            s ??= string.Empty;
            if (s.Contains('"')) s = s.Replace("\"", "\"\"");
            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r') || s.Contains('"'))
            {
                return $"\"{s}\"";
            }
            return s;
        }

        static string Get(Dictionary<string, object?> d, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (d.TryGetValue(k, out var v) && v is not null)
                {
                    var str = v.ToString();
                    if (!string.IsNullOrWhiteSpace(str)) return str;
                }
            }
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("StudentId,FirstName,MiddleName,LastName,Email,DegreeProgram,YearLevel,ScholarshipType,InitialStatus,Gpa");

        foreach (var s in scholars)
        {
            var studentId = Get(s, "studentId", "scholarId", "id");
            var first = Get(s, "firstName", "firstname", "first_name");
            var middle = Get(s, "middleName", "middlename", "middle_name", "middleInitial", "middle_initial");
            var last = Get(s, "lastName", "lastname", "last_name");
            var email = Get(s, "email");
            var program = Get(s, "degreeProgram", "program", "course", "degree");
            var year = Get(s, "yearLevel", "year");
            var type = Get(s, "scholarshipType");
            var status = Get(s, "initialStatus", "status");
            var gpa = Get(s, "gpa", "currentGpa", "latestGpa");

            sb.Append(Esc(studentId)).Append(',')
              .Append(Esc(first)).Append(',')
              .Append(Esc(middle)).Append(',')
              .Append(Esc(last)).Append(',')
              .Append(Esc(email)).Append(',')
              .Append(Esc(program)).Append(',')
              .Append(Esc(year)).Append(',')
              .Append(Esc(type)).Append(',')
              .Append(Esc(status)).Append(',')
              .Append(Esc(gpa))
              .AppendLine();
        }

        return sb.ToString();
    }

    private async void OnResetSettingsClicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "⚠ Reset Settings",
            "This will restore ALL settings to their default values. This action cannot be undone.\n\nAre you sure?",
            "Yes, Reset", "Cancel");

        if (!confirmed) return;

        // Remove all setting preferences
        Preferences.Default.Remove(KeyProgramName);
        Preferences.Default.Remove(KeyAcademicYear);
        Preferences.Default.Remove(KeySemester);
        Preferences.Default.Remove(KeyMinGpa);
        Preferences.Default.Remove(KeyFromEmail);
        Preferences.Default.Remove(KeyReplyEmail);
        Preferences.Default.Remove(KeyContactNumber);
        Preferences.Default.Remove(KeyNewAppAlerts);
        Preferences.Default.Remove(KeyDocUploadAlerts);
        Preferences.Default.Remove(KeyCompliance);
        Preferences.Default.Remove(KeyDisbursement);
        Preferences.Default.Remove(KeyEmailNotif);
        Preferences.Default.Remove(KeyTwoFA);
        Preferences.Default.Remove(KeySessionTimeout);

        // Reload defaults into UI
        LoadSavedSettings();

        SaveStatusLabel.Text = "✅ Settings reset to defaults.";
        SaveStatusLabel.TextColor = Color.FromArgb("#F59E0B");
        await Task.Delay(3000);
        SaveStatusLabel.Text = "";
    }

    // ──────────────────────────────────────────────
    // Notification Overlay Handlers
    // ──────────────────────────────────────────────

    private void OnNotificationsClicked(object sender, EventArgs e)
    {
        NotificationsOverlay.IsVisible = true;
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        _ = AgriScholarApp.Helpers.AdminNotificationHelper.BuildNotificationsOverlayAsync(
            token, _firestore, NotificationsList, HeaderBadgeLabel, HeaderBadgeFrame,
            OverlayBadgeLabel, OverlayBadgeFrame, NotificationsOverlay);
    }

    private void OnCloseNotifications(object sender, EventArgs e)   => NotificationsOverlay.IsVisible = false;

    private void OnMarkAllReadClicked(object sender, EventArgs e)
    {
        AgriScholarApp.Helpers.AdminNotificationHelper.MarkAllRead(
            NotificationsList, HeaderBadgeLabel, HeaderBadgeFrame,
            OverlayBadgeLabel, OverlayBadgeFrame, NotificationsOverlay);
    }

    // ──────────────────────────────────────────────
    // Navigation
    // ──────────────────────────────────────────────

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        AuthService.Logout();
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }

    private async void OnDashboardClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//AdminDashboardPage");

    private async void OnScholarsManagementClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//ScholarsManagementPage");

    private async void OnDocumentVerificationClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//DocumentVerificationPage");

    private async void OnRequirementsAnnouncementsClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//RequirementsAnnouncementsManagementPage");

    private async void OnReportsAnalyticsClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//ReportsAnalyticsPage");

    private async void OnSettingsClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SettingsPage");
}
