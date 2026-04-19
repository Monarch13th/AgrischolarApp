using AgriScholarApp.Services;

namespace AgriScholarApp.ScholarPage;

public partial class ProfilePage : ContentPage
{
    private const string ScholarEmailKey = "scholar_email";
    private const string FirebaseIdTokenKey = "firebase_id_token";
    private const string FirebaseUidKey = "firebase_uid";

    private readonly FirestoreRestService _firestore = new();

    private Dictionary<string, object?>? _me;

    public ProfilePage()
    {
        InitializeComponent();

        var email = Preferences.Default.Get(ScholarEmailKey, string.Empty);
        EmailLabel.Text = email;

        if (!string.IsNullOrWhiteSpace(email) && email.Contains('@'))
        {
            FullNameLabel.Text = email.Split('@')[0];
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadProfileAsync();
        await LoadNotificationsAsync();
    }

    private async Task LoadNotificationsAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);

            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
            {
                SetNotifications(Array.Empty<(string Title, string Body, string Time)>());
                return;
            }

            var items = new List<(string Title, string Body, string Time)>();

            var subs = await _firestore.ListDocumentsAsync($"documents/{uid}/submissions", idToken);
            var gradeSubs = subs
                .Where(d => string.Equals(GetString(d, "type"), "grade_report", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => GetDateTime(d, "createdAt") ?? DateTime.MinValue)
                .ToList();

            if (gradeSubs.Count > 0)
            {
                var latest = gradeSubs[0];
                var status = (GetString(latest, "status") ?? string.Empty).Trim().ToLowerInvariant();
                var createdAt = GetDateTime(latest, "createdAt") ?? DateTime.MinValue;

                if (status == "verified")
                {
                    items.Add(("Grade Report Approved", "Your grade report has been approved by the admin.", FormatRelativeTime(createdAt)));
                }
                else if (status == "rejected")
                {
                    items.Add(("Grade Report Rejected", "Your grade report was rejected. Please resubmit.", FormatRelativeTime(createdAt)));
                }
            }

            try
            {
                var anns = await _firestore.ListDocumentsAsync("announcements", idToken);
                var latestAnns = anns
                    .OrderByDescending(a => GetDateTime(a, "createdAt") ?? DateTime.MinValue)
                    .Take(2)
                    .ToList();

                foreach (var a in latestAnns)
                {
                    var title = GetString(a, "title") ?? "Announcement";
                    var body = GetString(a, "message") ?? string.Empty;
                    var createdAt = GetDateTime(a, "createdAt") ?? DateTime.MinValue;
                    items.Add((title, body, FormatRelativeTime(createdAt)));
                }
            }
            catch
            {
                // ignore
            }

            SetNotifications(items.Take(2).ToArray());
        }
        catch
        {
            SetNotifications(Array.Empty<(string Title, string Body, string Time)>());
        }
    }

    private void SetNotifications((string Title, string Body, string Time)[] items)
    {
        var count = items.Length;
        NotificationsBadgeLabel.Text = count.ToString();
        NotificationsBadgeLabel.IsVisible = count > 0;

        NotificationRow1.IsVisible = count > 0;
        NotificationRow2.IsVisible = count > 1;

        if (count > 0)
        {
            NotificationTitle1.Text = items[0].Title;
            NotificationBody1.Text = items[0].Body;
            NotificationTime1.Text = items[0].Time;
        }
        if (count > 1)
        {
            NotificationTitle2.Text = items[1].Title;
            NotificationBody2.Text = items[1].Body;
            NotificationTime2.Text = items[1].Time;
        }
    }

    private static DateTime? GetDateTime(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        if (v is DateTime dt)
        {
            return dt;
        }

        if (DateTime.TryParse(v.ToString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatRelativeTime(DateTime dt)
    {
        if (dt == DateTime.MinValue) return string.Empty;
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        var diff = DateTime.UtcNow - utc;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);
            var email = Preferences.Default.Get(ScholarEmailKey, string.Empty);

            EmailLabel.Text = email;

            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
            {
                return;
            }

            _me = await _firestore.GetDocumentAsync($"scholars/{uid}", idToken);

            var firestoreEmail = GetString(_me, "email")
                ?? GetString(_me, "Email")
                ?? GetString(_me, "scholarEmail");
            if (!string.IsNullOrWhiteSpace(firestoreEmail))
            {
                EmailLabel.Text = firestoreEmail;
            }

            FirstNameValue.Text = GetString(_me, "firstName")
                ?? GetString(_me, "firstname")
                ?? GetString(_me, "givenName")
                ?? FirstNameValue.Text;

            MiddleNameValue.Text = GetString(_me, "middleName")
                ?? GetString(_me, "middlename")
                ?? GetString(_me, "mi")
                ?? MiddleNameValue.Text;

            LastNameValue.Text = GetString(_me, "lastName")
                ?? GetString(_me, "lastname")
                ?? GetString(_me, "surname")
                ?? LastNameValue.Text;

            PhoneNumberValue.Text = GetString(_me, "phoneNumber")
                ?? GetString(_me, "phone")
                ?? GetString(_me, "mobile")
                ?? GetString(_me, "contactNumber")
                ?? PhoneNumberValue.Text;

            HomeAddressValue.Text = GetString(_me, "homeAddress")
                ?? GetString(_me, "address")
                ?? GetString(_me, "home_address")
                ?? HomeAddressValue.Text;

            SexValue.Text = GetString(_me, "sex")
                ?? GetString(_me, "gender")
                ?? SexValue.Text;

            var dobRaw = GetString(_me, "dateOfBirth")
                ?? GetString(_me, "birthDate")
                ?? GetString(_me, "dob");
            DateOfBirthValue.Text = FormatDateOnly(dobRaw) ?? DateOfBirthValue.Text;

            var firstName = GetString(_me, "firstName");
            var lastName = GetString(_me, "lastName");
            var fullName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                FullNameLabel.Text = fullName;
            }

            StudentIdValue.Text = GetString(_me, "studentId")
                ?? GetString(_me, "scholarId")
                ?? GetString(_me, "id")
                ?? StudentIdValue.Text;

            ProgramValue.Text = GetString(_me, "degreeProgram")
                ?? GetString(_me, "program")
                ?? ProgramValue.Text;

            YearLevelValue.Text = GetString(_me, "yearLevel")
                ?? YearLevelValue.Text;

            // Scholarship Type
            var scholarshipType = GetString(_me, "scholarshipType") ?? GetString(_me, "scholarshiptype") ?? "";
            if (!string.IsNullOrWhiteSpace(scholarshipType))
            {
                ScholarshipTypeValue.Text = scholarshipType;
                ScholarshipTypeLabel.Text = scholarshipType;
            }

            // Status Badge
            var status = GetString(_me, "initialStatus") ?? GetString(_me, "status") ?? "Active";
            StatusLabel.Text = char.ToUpper(status[0]) + status.Substring(1).ToLower();

            // Quick stats bar
            YearLevelQuickLabel.Text = GetString(_me, "yearLevel") ?? "—";
            SchoolQuickLabel.Text    = GetString(_me, "school") ?? GetString(_me, "university") ?? "—";

            // GPA display + progress bar
            var gpaRaw = GetString(_me, "gpa") ?? GetString(_me, "currentGpa") ?? "";
            if (double.TryParse(gpaRaw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var gpaVal))
            {
                GpaQuickLabel.Text   = gpaVal.ToString("F2");
                GpaDisplayLabel.Text = gpaVal.ToString("F2");

                // Progress bar width: GPA is out of 4.00
                const double maxGpaBarWidth = 200.0;
                var barFraction = Math.Clamp(gpaVal / 4.0, 0.0, 1.0);
                GpaProgressBar.WidthRequest = barFraction * maxGpaBarWidth;

                // Color + text based on GPA
                if (gpaVal >= 3.5)
                {
                    GpaProgressBar.BackgroundColor = Color.FromArgb("#16A34A");
                    GpaStatusLabel.Text = "Excellent standing — keep it up!";
                    GpaStatusBadge.Text = "Excellent";
                }
                else if (gpaVal >= 2.5)
                {
                    GpaProgressBar.BackgroundColor = Color.FromArgb("#1565C0");
                    GpaStatusLabel.Text = "Good standing — meets requirements.";
                    GpaStatusBadge.Text = "Good";
                }
                else
                {
                    GpaProgressBar.BackgroundColor = Color.FromArgb("#EF4444");
                    GpaStatusLabel.Text = "⚠ At risk — GPA below minimum requirement.";
                    GpaStatusBadge.Text = "At Risk";
                }
            }
            else
            {
                GpaQuickLabel.Text   = "—";
                GpaDisplayLabel.Text = "—";
                GpaStatusLabel.Text  = "No GPA recorded yet.";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Profile", $"Failed to load profile: {ex.Message}", "OK");
        }
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val) || val is null) return null;
        return val.ToString();
    }

    private static string? FormatDateOnly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (DateTimeOffset.TryParse(raw, out var dto))
        {
            return dto.Date.ToString("yyyy-MM-dd");
        }

        if (DateTime.TryParse(raw, out var dt))
        {
            return dt.Date.ToString("yyyy-MM-dd");
        }

        return raw;
    }

    private async void OnSearchTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Search", "Coming soon.", "OK");
    }

    private async void OnNotificationsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        QuickActionOverlay.IsVisible = false;
        NotificationsOverlay.IsVisible = true;
    }

    private void OnCloseNotifications(object sender, EventArgs e)
    {
        NotificationsOverlay.IsVisible = false;
    }

    private void OnDeleteNotification1(object sender, EventArgs e)
    {
        NotificationsOverlay.IsVisible = false;
    }

    private void OnDeleteNotification2(object sender, EventArgs e)
    {
        NotificationsOverlay.IsVisible = false;
    }

    private void OnMenuTapped(object sender, TappedEventArgs e)
    {
        NotificationsOverlay.IsVisible = false;
        MenuOverlay.IsVisible = true;
    }

    private void OnMenuOverlayTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        MenuOverlay.IsVisible = false;
        QuickActionOverlay.IsVisible = false;
        Preferences.Default.Remove(ScholarEmailKey);
        Preferences.Default.Remove(FirebaseIdTokenKey);
        Preferences.Default.Remove(FirebaseUidKey);
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }

    private async void OnLogoutRowTapped(object sender, TappedEventArgs e)
    {
        await OnLogoutAsync();
    }

    private async Task OnLogoutAsync()
    {
        MenuOverlay.IsVisible = false;
        QuickActionOverlay.IsVisible = false;
        Preferences.Default.Remove(ScholarEmailKey);
        Preferences.Default.Remove(FirebaseIdTokenKey);
        Preferences.Default.Remove(FirebaseUidKey);
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }

    private async void OnHomeTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//ScholarHomePage");
    }

    private async void OnRecordsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//AcademicRecordsPage");
    }

    private async void OnDocumentsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//DocumentsPage");
    }

    private void OnProfileTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        // Already on Profile
    }

    private void OnQuickActionTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        QuickActionOverlay.IsVisible = true;
    }

    private void OnQuickActionOverlayTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
    }

    private void OnQuickActionCancelClicked(object sender, EventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
    }

    private async void OnSubmitGradeReportTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//UploadGradeReportPage?from=profile");
    }

    private async void OnUploadDocumentTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//UploadDocumentPage?from=profile");
    }

    private async void OnLogCommunityServiceTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await DisplayAlert("Log Community Service", "Coming soon.", "OK");
    }

    private async void OnEditProfileTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);
            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
            {
                await DisplayAlert("Edit Profile", "Session expired. Please login again.", "OK");
                await Shell.Current.GoToAsync("//ScholarLoginPage");
                return;
            }

            if (_me is null)
            {
                await LoadProfileAsync();
            }

            var currentFirst = _me is null ? string.Empty : (GetString(_me, "firstName") ?? string.Empty);
            var currentLast = _me is null ? string.Empty : (GetString(_me, "lastName") ?? string.Empty);
            var currentProgram = _me is null ? string.Empty : (GetString(_me, "degreeProgram") ?? GetString(_me, "program") ?? string.Empty);
            var currentYear = _me is null ? string.Empty : (GetString(_me, "yearLevel") ?? string.Empty);

            var firstName = await DisplayPromptAsync("Edit Profile", "First name", initialValue: currentFirst);
            if (firstName is null) return;
            var lastName = await DisplayPromptAsync("Edit Profile", "Last name", initialValue: currentLast);
            if (lastName is null) return;
            var program = await DisplayPromptAsync("Edit Profile", "Degree program", initialValue: currentProgram);
            if (program is null) return;
            var yearLevel = await DisplayPromptAsync("Edit Profile", "Year level", initialValue: currentYear);
            if (yearLevel is null) return;

            var update = new Dictionary<string, object?>
            {
                ["firstName"] = firstName.Trim(),
                ["lastName"] = lastName.Trim(),
                ["degreeProgram"] = program.Trim(),
                ["yearLevel"] = yearLevel.Trim(),
                ["updatedAt"] = DateTime.UtcNow.ToString("o")
            };

            await _firestore.UpdateScholarAsync(uid, idToken, update);
            await LoadProfileAsync();
            await DisplayAlert("Edit Profile", "Profile updated.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Edit Profile", $"Failed to update profile: {ex.Message}", "OK");
        }
    }

    private async void OnChangePasswordTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Change Password", "Please request a password reset/change from the admin.", "OK");
    }

    private async void OnSettingsTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Settings", "Coming soon.", "OK");
    }
}
