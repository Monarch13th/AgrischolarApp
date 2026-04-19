using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using AgriScholarApp.Services;
using Microsoft.Maui.ApplicationModel;

namespace AgriScholarApp.ScholarPage;

public partial class DocumentsPage : ContentPage
{
    private const string ScholarEmailKey = "scholar_email";
    private const string FirebaseIdTokenKey = "firebase_id_token";
    private const string FirebaseUidKey = "firebase_uid";

    private readonly FirestoreRestService _firestore = new();

    public ObservableCollection<RequiredSubmissionItem> RequiredItems { get; } = new();
    public ObservableCollection<SubmittedDocumentItem> SubmittedItems { get; } = new();

    public ICommand UploadRequiredCommand { get; }
    public ICommand ViewRequiredCommand { get; }
    public ICommand ViewSubmittedCommand { get; }
    public ICommand DownloadSubmittedCommand { get; }

    public DocumentsPage()
    {
        InitializeComponent();

        UploadRequiredCommand = new Command<RequiredSubmissionItem>(async item => await UploadRequiredAsync(item));
        ViewRequiredCommand = new Command<RequiredSubmissionItem>(async item => await ViewRequiredAsync(item));
        ViewSubmittedCommand = new Command<SubmittedDocumentItem>(async item => await OpenSubmittedAsync(item));
        DownloadSubmittedCommand = new Command<SubmittedDocumentItem>(async item => await OpenSubmittedAsync(item));

        BindingContext = this;
        SetActiveTab(0);
    }

    private async Task OpenSubmittedAsync(SubmittedDocumentItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.DownloadUrl))
        {
            await DisplayAlert("Open", "No file available.", "OK");
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(item.DownloadUrl);
        }
        catch
        {
            await DisplayAlert("Open", "Unable to open the document.", "OK");
        }
    }

    public sealed class SubmittedDocumentItem
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Academic";
        public string StatusText { get; set; } = "Pending";
        public string Meta { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;

        public string StatusChipBackground { get; set; } = "#FEF3C7";
        public string StatusChipTextColor { get; set; } = "#92400E";
    }

    private async Task LoadSubmittedItemsAsync()
    {
        var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
        var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);

        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
        {
            SubmittedItems.Clear();
            return;
        }

        List<Dictionary<string, object?>> subs;
        try
        {
            subs = await _firestore.ListDocumentsAsync($"documents/{uid}/submissions", idToken);
        }
        catch
        {
            SubmittedItems.Clear();
            return;
        }

        var rows = subs
            .OrderByDescending(s => GetDateTime(s, "createdAt") ?? DateTime.MinValue)
            .Select(s =>
            {
                var type = (GetString(s, "type") ?? string.Empty).Trim();
                var title = string.IsNullOrWhiteSpace(type) ? "Document" : HumanizeType(type);
                var status = ((GetString(s, "status") ?? "pending").Trim()).ToLowerInvariant();
                var createdAt = GetDateTime(s, "createdAt");
                var meta = createdAt.HasValue ? $"Submitted: {createdAt.Value.ToLocalTime():MM/dd/yyyy}" : string.Empty;
                var downloadUrl = GetString(s, "downloadUrl") ?? string.Empty;

                var statusText = status switch
                {
                    "verified" => "Approved",
                    "approved" => "Approved",
                    "rejected" => "Rejected",
                    "pending" => "Pending",
                    _ => "Submitted"
                };

                var (bg, fg) = statusText switch
                {
                    "Approved" => ("#DCFCE7", "#166534"),
                    "Rejected" => ("#FEE2E2", "#991B1B"),
                    _ => ("#FEF3C7", "#92400E")
                };

                return new SubmittedDocumentItem
                {
                    Title = title,
                    Category = "Academic",
                    StatusText = statusText,
                    Meta = meta,
                    DownloadUrl = downloadUrl,
                    StatusChipBackground = bg,
                    StatusChipTextColor = fg
                };
            })
            .ToList();

        SubmittedItems.Clear();
        foreach (var r in rows) SubmittedItems.Add(r);
    }

    private static string HumanizeType(string type)
    {
        var t = (type ?? string.Empty).Trim();
        if (string.Equals(t, "cor_1st_sem", StringComparison.OrdinalIgnoreCase)) return "COR (1st Semester)";
        if (string.Equals(t, "grade_1st_sem", StringComparison.OrdinalIgnoreCase)) return "1st Semester Grade";
        if (string.Equals(t, "cor_2nd_sem", StringComparison.OrdinalIgnoreCase)) return "COR (2nd Semester)";
        if (string.Equals(t, "grade_2nd_sem", StringComparison.OrdinalIgnoreCase)) return "2nd Semester Grade";
        if (string.Equals(t, "grade_report", StringComparison.OrdinalIgnoreCase)) return "Grade Report";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.Replace('_', ' '));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadRequiredItemsAsync();
        await LoadSubmittedItemsAsync();
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

    private async Task LoadRequiredItemsAsync()
    {
        var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
        var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);

        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
        {
            RequiredItems.Clear();
            SeedRequiredItems(new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase));
            return;
        }

        Dictionary<string, Dictionary<string, object?>> latestByType;
        try
        {
            var subs = await _firestore.ListDocumentsAsync($"documents/{uid}/submissions", idToken);
            latestByType = subs
                .Where(d => d.TryGetValue("type", out var t) && t is not null)
                .GroupBy(d => (d["type"]?.ToString() ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => GetDateTime(x, "createdAt")).First(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            latestByType = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        }

        RequiredItems.Clear();
        SeedRequiredItems(latestByType);
    }

    private void SeedRequiredItems(Dictionary<string, Dictionary<string, object?>> latestByType)
    {
        RequiredItems.Add(BuildRequiredItem(
            "cor_1st_sem",
            "COR (1st Semester)",
            "Upload your Certificate of Registration for 1st semester",
            "Academic",
            latestByType));

        RequiredItems.Add(BuildRequiredItem(
            "grade_1st_sem",
            "1st Semester Grade", 
            "Upload your grade report for 1st semester",
            "Academic",
            latestByType));

        RequiredItems.Add(BuildRequiredItem(
            "cor_2nd_sem",
            "COR (2nd Semester)",
            "Upload your Certificate of Registration for 2nd semester",
            "Academic",
            latestByType));

        RequiredItems.Add(BuildRequiredItem(
            "grade_2nd_sem",
            "2nd Semester Grade",
            "Upload your grade report for 2nd semester",
            "Academic",
            latestByType));
    }

    private static RequiredSubmissionItem BuildRequiredItem(
        string type,
        string title,
        string description,
        string category,
        Dictionary<string, Dictionary<string, object?>> latestByType)
    {
        latestByType.TryGetValue(type, out var latest);

        var isSubmitted = latest is not null;
        var status = (latest is null ? "pending" : (GetString(latest, "status") ?? "submitted")).Trim();
        var downloadUrl = latest is null ? string.Empty : (GetString(latest, "downloadUrl") ?? string.Empty);

        var statusNorm = status.ToLowerInvariant();
        var statusText = isSubmitted ? statusNorm switch
        {
            "verified" => "Approved",
            "rejected" => "Rejected",
            "pending" => "Pending",
            _ => "Submitted"
        } : "Pending";

        var createdAt = latest is null ? (DateTime?)null : GetDateTime(latest, "createdAt");
        var meta = isSubmitted && createdAt.HasValue
            ? $"Submitted: {createdAt.Value:MM/dd/yyyy}"
            : "Not submitted";

        var chipBg = statusText switch
        {
            "Approved" => "#DCFCE7",
            "Rejected" => "#FEE2E2",
            _ => "#E0F2FE"
        };

        var chipText = statusText switch
        {
            "Approved" => "#166534",
            "Rejected" => "#B91C1C",
            _ => "#0369A1"
        };

        var cardBg = statusText == "Rejected" ? "#FFF7F7" : "White";
        var cardBorder = statusText == "Rejected" ? "#FCA5A5" : "#E5E7EB";

        var iconText = isSubmitted ? "✓" : "🕒";
        var iconBg = isSubmitted ? "#DCFCE7" : "#E3F2FD";
        var iconColor = isSubmitted ? "#166534" : "#111827";

        var metaColor = statusText == "Rejected" ? "#B91C1C" : "#6B7280";

        return new RequiredSubmissionItem
        {
            Type = type,
            Title = title,
            Description = description,
            Category = category,
            StatusText = statusText,
            StatusChipBackground = chipBg,
            StatusChipTextColor = chipText,
            CardBackground = cardBg,
            CardBorder = cardBorder,
            IconText = iconText,
            IconBackground = iconBg,
            IconColor = iconColor,
            Meta = meta,
            MetaColor = metaColor,
            DownloadUrl = downloadUrl,
            ShowUpload = !isSubmitted,
            ShowView = isSubmitted && !string.IsNullOrWhiteSpace(downloadUrl)
        };
    }

    private async Task UploadRequiredAsync(RequiredSubmissionItem? item)
    {
        if (item is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"//UploadGradeReportPage?type={Uri.EscapeDataString(item.Type)}");
    }

    private async Task ViewRequiredAsync(RequiredSubmissionItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.DownloadUrl))
        {
            await DisplayAlert("View", "No file available to view.", "OK");
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(item.DownloadUrl);
        }
        catch
        {
            await DisplayAlert("View", "Unable to open the document.", "OK");
        }
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        return dict.TryGetValue(key, out var v) ? v?.ToString() : null;
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

    public sealed class RequiredSubmissionItem
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        public string StatusText { get; set; } = "Pending";
        public string StatusChipBackground { get; set; } = "#E0F2FE";
        public string StatusChipTextColor { get; set; } = "#0369A1";

        public string CardBackground { get; set; } = "White";
        public string CardBorder { get; set; } = "#E5E7EB";

        public string IconText { get; set; } = "🕒";
        public string IconBackground { get; set; } = "#E3F2FD";
        public string IconColor { get; set; } = "#111827";

        public string Meta { get; set; } = string.Empty;
        public string MetaColor { get; set; } = "#6B7280";

        public string DownloadUrl { get; set; } = string.Empty;

        public bool ShowUpload { get; set; } = true;
        public bool ShowView { get; set; }
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

    private async void OnRecordsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//AcademicRecordsPage");
    }

    private void OnDocumentsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        // Already on Documents
    }

    private async void OnProfileTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//ProfilePage");
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
        await Shell.Current.GoToAsync("//UploadGradeReportPage?from=documents");
    }

    private async void OnUploadDocumentTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//UploadDocumentPage?from=documents");
    }

    private async void OnLogCommunityServiceTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await DisplayAlert("Log Community Service", "Coming soon.", "OK");
    }

    private void OnTabRequiredTapped(object sender, TappedEventArgs e)
    {
        SetActiveTab(0);
    }

    private void OnTabSubmittedTapped(object sender, TappedEventArgs e)
    {
        SetActiveTab(1);
    }

    private void SetActiveTab(int index)
    {
        PanelRequired.IsVisible = index == 0;
        PanelSubmitted.IsVisible = index == 1;

        TabRequired.BackgroundColor = index == 0 ? Colors.White : Colors.Transparent;
        TabSubmitted.BackgroundColor = index == 1 ? Colors.White : Colors.Transparent;
    }

    // Removed placeholder submitted document handlers; submitted actions are now command-driven.
}
