using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;

namespace AgriScholarApp.ScholarPage;

public partial class AcademicRecordsPage : ContentPage
{
    private const string ScholarEmailKey = "scholar_email";
    private const string FirebaseIdTokenKey = "firebase_id_token";
    private const string FirebaseUidKey = "firebase_uid";

    private readonly AgriScholarApp.Services.FirestoreRestService _firestore = new();

    private readonly ObservableCollection<RecordRow> _pending = new();
    private readonly ObservableCollection<RecordRow> _history = new();

    public AcademicRecordsPage()
    {
        InitializeComponent();

        PendingRequirementsCollection.ItemsSource = _pending;
        SubmissionHistoryCollection.ItemsSource = _history;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
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
                .OrderByDescending(d => GetDateTime(d, "createdAt"))
                .ToList();

            if (gradeSubs.Count > 0)
            {
                var latest = gradeSubs[0];
                var status = (GetString(latest, "status") ?? string.Empty).Trim().ToLowerInvariant();
                var createdAt = GetDateTime(latest, "createdAt");

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
                    .OrderByDescending(a => GetDateTime(a, "createdAt"))
                    .Take(2)
                    .ToList();

                foreach (var a in latestAnns)
                {
                    var title = GetString(a, "title") ?? "Announcement";
                    var body = GetString(a, "message") ?? string.Empty;
                    var createdAt = GetDateTime(a, "createdAt");
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

    private static string FormatRelativeTime(DateTime dt)
    {
        if (dt == DateTime.MinValue) return string.Empty;
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    private async Task LoadAsync()
    {
        await LoadScholarHeaderAsync();
        await LoadProgressAndListsAsync();
    }

    private async Task LoadScholarHeaderAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);
            var email = Preferences.Default.Get(ScholarEmailKey, string.Empty);

            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
            {
                RecordsStatusLabel.Text = "Status: -";
                RecordsProgramLabel.Text = "Program: -";
                return;
            }

            Dictionary<string, object?>? me = null;
            try
            {
                me = await _firestore.GetDocumentAsync($"scholars/{uid}", idToken);
            }
            catch
            {
                // ignore
            }

            if (me is null || me.Count == 0)
            {
                var scholars = await _firestore.ListDocumentsAsync("scholars", idToken);
                me = scholars.FirstOrDefault(s => IsSameScholar(s, uid, email));
            }

            var status = me is null ? string.Empty : (GetString(me, "initialStatus") ?? GetString(me, "status") ?? string.Empty);
            var degreeProgram = me is null ? string.Empty : (
                GetString(me, "degreeProgram") ??
                GetString(me, "DegreeProgram") ??
                GetString(me, "course") ??
                GetString(me, "program") ??
                string.Empty);

            RecordsStatusLabel.Text = $"Status: {(string.IsNullOrWhiteSpace(status) ? "-" : status.Trim())}";
            RecordsProgramLabel.Text = $"Program: {(string.IsNullOrWhiteSpace(degreeProgram) ? "-" : degreeProgram.Trim())}";
        }
        catch
        {
            RecordsStatusLabel.Text = "Status: -";
            RecordsProgramLabel.Text = "Program: -";
        }
    }

    private async Task LoadProgressAndListsAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);
            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
            {
                SetProgress(0, 0);
                _pending.Clear();
                _history.Clear();
                return;
            }

            var subs = await _firestore.ListDocumentsAsync($"documents/{uid}/submissions", idToken);
            var submittedTypes = new HashSet<string>(
                subs.Select(s => (GetString(s, "type") ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrWhiteSpace(t)));

            var hasApprovedLegacyGradeReport = subs.Any(s =>
            {
                var t = (GetString(s, "type") ?? string.Empty).Trim();
                if (!string.Equals(t, "grade_report", StringComparison.OrdinalIgnoreCase)) return false;
                var st = (GetString(s, "status") ?? string.Empty).Trim();
                return string.Equals(st, "approved", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(st, "verified", StringComparison.OrdinalIgnoreCase);
            });
            if (hasApprovedLegacyGradeReport && !submittedTypes.Contains("grade_1st_sem"))
            {
                submittedTypes.Add("grade_1st_sem");
            }

            var required = new (string Type, string Title)[]
            {
                ("cor_1st_sem", "COR (1st Semester)"),
                ("grade_1st_sem", "1st Semester Grade"),
                ("cor_2nd_sem", "COR (2nd Semester)"),
                ("grade_2nd_sem", "2nd Semester Grade")
            };

            var completed = required.Count(r => submittedTypes.Contains(r.Type));
            SetProgress(completed, required.Length);

            _pending.Clear();
            foreach (var r in required)
            {
                if (!submittedTypes.Contains(r.Type))
                {
                    _pending.Add(RecordRow.Missing(r.Title));
                }
            }

            _history.Clear();
            foreach (var s in subs
                         .Select(d => SubmissionRow.FromFirestore(d))
                         .OrderByDescending(r => r.CreatedAt)
                         .Take(10))
            {
                _history.Add(RecordRow.FromSubmission(s));
            }
        }
        catch
        {
            SetProgress(0, 0);
            _pending.Clear();
            _history.Clear();
        }
    }

    private void SetProgress(int completed, int total)
    {
        RequirementsProgressLabel.Text = $"{completed} / {total} Completed";

        var ratio = total <= 0 ? 0 : Math.Clamp(completed / (double)total, 0, 1);
        RequirementsProgressFill.WidthRequest = 0;

        // Width is calculated after layout.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (RequirementsProgressFill.Parent is not Frame parent) return;
            var maxWidth = parent.Width;
            if (maxWidth <= 0) return;
            RequirementsProgressFill.WidthRequest = maxWidth * ratio;
        });
    }

    private async void OnViewDocumentsTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//DocumentsPage");
    }

    private sealed record SubmissionRow(string Title, string Status, DateTime CreatedAt)
    {
        public static SubmissionRow FromFirestore(Dictionary<string, object?> d)
        {
            var type = (GetString(d, "type") ?? string.Empty).Trim();
            var title = string.IsNullOrWhiteSpace(type) ? "Document" : type.Replace('_', ' ');
            var status = (GetString(d, "status") ?? "pending").Trim();
            var createdAt = GetDateTime(d, "createdAt");
            return new SubmissionRow(title, status, createdAt);
        }
    }

    private sealed record RecordRow(string Title, string Meta, string BadgeText, Color BadgeColor, Color BadgeTextColor)
    {
        public static RecordRow Missing(string title)
            => new(title, "", "Missing", Color.FromArgb("#FEF3C7"), Color.FromArgb("#92400E"));

        public static RecordRow FromSubmission(SubmissionRow s)
        {
            var meta = s.CreatedAt == DateTime.MinValue ? string.Empty : $"Submitted: {s.CreatedAt.ToLocalTime():MMM d, yyyy}";
            var st = (s.Status ?? string.Empty).Trim().ToLowerInvariant();

            return st switch
            {
                "approved" or "verified" => new RecordRow(s.Title, meta, "Approved", Color.FromArgb("#DCFCE7"), Color.FromArgb("#166534")),
                "rejected" => new RecordRow(s.Title, meta, "Rejected", Color.FromArgb("#FEE2E2"), Color.FromArgb("#991B1B")),
                _ => new RecordRow(s.Title, meta, "Pending", Color.FromArgb("#FEF3C7"), Color.FromArgb("#92400E"))
            };
        }
    }

    private static bool IsSameScholar(Dictionary<string, object?> s, string uid, string email)
    {
        var docId = GetString(s, "__documentId") ?? string.Empty;
        var sUid = GetString(s, "uid") ?? GetString(s, "scholarId") ?? GetString(s, "id") ?? string.Empty;
        var sEmail = GetString(s, "email") ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(uid))
        {
            if (string.Equals(docId, uid, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(sUid, uid, StringComparison.OrdinalIgnoreCase)) return true;
        }

        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(sEmail))
        {
            if (string.Equals(sEmail, email, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        return dict.TryGetValue(key, out var v) ? v?.ToString() : null;
    }

    private static DateTime GetDateTime(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null)
        {
            return DateTime.MinValue;
        }

        if (v is DateTime dt)
        {
            return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        }

        if (DateTime.TryParse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return DateTime.MinValue;
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
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }

    private async void OnHomeTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//ScholarHomePage");
    }

    private void OnRecordsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        // Already on Records
    }

    private async void OnQuickActionTapped(object sender, TappedEventArgs e)
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
        await Shell.Current.GoToAsync("//UploadGradeReportPage?from=records");
    }

    private async void OnUploadDocumentTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//UploadDocumentPage?from=records");
    }

    private async void OnLogCommunityServiceTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await DisplayAlert("Log Community Service", "Coming soon.", "OK");
    }

    private async void OnDocumentsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//DocumentsPage");
    }

    private async void OnProfileTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//ProfilePage");
    }
}
