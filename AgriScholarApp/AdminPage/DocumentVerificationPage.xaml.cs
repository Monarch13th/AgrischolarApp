using AgriScholarApp.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AgriScholarApp.Pages;

public partial class DocumentVerificationPage : ContentPage
{
    private const string FirebaseAdminTokenKey = "firebase_admin_id_token";
    public ObservableCollection<PendingSubmissionRow> PendingGradeReports { get; } = new();
    public ObservableCollection<PendingSubmissionRow> VerifiedGradeReports { get; } = new();
    public ObservableCollection<PendingSubmissionRow> RejectedGradeReports { get; } = new();
    public ObservableCollection<PendingSubmissionRow> FilteredGradeReports { get; } = new();

    public bool ShowDecisionActions { get; private set; } = true;

    private VerificationFilter _activeFilter = VerificationFilter.Pending;

    private readonly FirestoreRestService _firestore = new();
    private readonly FirebaseAuthRestService _auth = new();

    public DocumentVerificationPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadPendingGradeReportsAsync();
    }

    private enum VerificationFilter
    {
        Pending,
        Verified,
        Rejected
    }

    private async Task EnsureAdminTokenAsync()
    {
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var signIn = await _auth.SignInAsync("admin@gmail.com", "admin123");
        Preferences.Default.Set(FirebaseAdminTokenKey, signIn.IdToken);
    }

    private async Task LoadPendingGradeReportsAsync()
    {
        try
        {
            await EnsureAdminTokenAsync();
            var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            if (string.IsNullOrWhiteSpace(token))
            {
                PendingReviewValue.Text = "-";
                VerifiedTodayValue.Text = "-";
                RejectedValue.Text = "-";
                PendingGradeReports.Clear();
                return;
            }

            var scholars = await _firestore.ListDocumentsAsync("scholars", token);

            var pendingRows = new List<PendingSubmissionRow>();
            var verifiedRows = new List<PendingSubmissionRow>();
            var rejectedRows = new List<PendingSubmissionRow>();

            foreach (var s in scholars)
            {
                var uid = GetString(s, "uid") ?? GetString(s, "scholarId");
                if (string.IsNullOrWhiteSpace(uid))
                {
                    continue;
                }

                List<Dictionary<string, object?>> subs;
                try
                {
                    subs = await _firestore.ListDocumentsAsync($"documents/{uid}/submissions", token);
                }
                catch
                {
                    continue;
                }

                foreach (var sub in subs)
                {
                    var type = GetString(sub, "type");
                    if (!string.Equals(type, "grade_report", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var status = GetString(sub, "status") ?? "pending";
                    var createdAt = GetDateTime(sub, "createdAt");

                    var submissionId = GetString(sub, "docId") ?? GetString(sub, "__documentId") ?? string.Empty;
                    var row = new PendingSubmissionRow
                    {
                        Uid = uid,
                        Email = GetString(sub, "email") ?? GetString(s, "email") ?? string.Empty,
                        ScholarName = BuildScholarName(s),
                        Title = "Certificate of Grades",
                        Status = status,
                        CreatedAt = createdAt,
                        DownloadUrl = GetString(sub, "downloadUrl") ?? string.Empty,
                        SubmissionDocId = submissionId
                    };

                    if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingRows.Add(row);
                    }
                    else if (string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase))
                    {
                        verifiedRows.Add(row);
                    }
                    else if (string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        rejectedRows.Add(row);
                    }
                }
            }

            PendingGradeReports.Clear();
            foreach (var row in pendingRows.OrderByDescending(r => r.CreatedAt)) PendingGradeReports.Add(row);

            VerifiedGradeReports.Clear();
            foreach (var row in verifiedRows.OrderByDescending(r => r.CreatedAt)) VerifiedGradeReports.Add(row);

            RejectedGradeReports.Clear();
            foreach (var row in rejectedRows.OrderByDescending(r => r.CreatedAt)) RejectedGradeReports.Add(row);

            PendingReviewValue.Text = PendingGradeReports.Count.ToString(CultureInfo.InvariantCulture);
            VerifiedTodayValue.Text = VerifiedGradeReports.Count.ToString(CultureInfo.InvariantCulture);
            RejectedValue.Text = RejectedGradeReports.Count.ToString(CultureInfo.InvariantCulture);

            ApplyFilter(_activeFilter);
        }
        catch
        {
            PendingReviewValue.Text = "-";
            VerifiedTodayValue.Text = "-";
            RejectedValue.Text = "-";
        }
    }

    private void ApplyFilter(VerificationFilter filter)
    {
        _activeFilter = filter;
        ShowDecisionActions = filter == VerificationFilter.Pending;
        OnPropertyChanged(nameof(ShowDecisionActions));
        FilteredGradeReports.Clear();

        IEnumerable<PendingSubmissionRow> source = filter switch
        {
            VerificationFilter.Verified => VerifiedGradeReports,
            VerificationFilter.Rejected => RejectedGradeReports,
            _ => PendingGradeReports
        };

        foreach (var row in source)
        {
            FilteredGradeReports.Add(row);
        }

        switch (filter)
        {
            case VerificationFilter.Verified:
                SelectedFilterHeader.Text = "Verified Documents";
                SelectedFilterSubHeader.Text = "Documents verified by admin";
                break;
            case VerificationFilter.Rejected:
                SelectedFilterHeader.Text = "Rejected Documents";
                SelectedFilterSubHeader.Text = "Documents rejected by admin";
                break;
            default:
                SelectedFilterHeader.Text = "Pending Documents";
                SelectedFilterSubHeader.Text = "Documents awaiting verification";
                break;
        }
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

        if (DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTime.MinValue;
    }

    private static string BuildScholarName(Dictionary<string, object?> scholar)
    {
        var first = GetString(scholar, "firstName") ?? string.Empty;
        var middle = GetString(scholar, "middleName") ?? string.Empty;
        var last = GetString(scholar, "lastName") ?? string.Empty;
        var parts = new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return parts.Length == 0 ? "(unknown)" : string.Join(" ", parts);
    }

    public sealed class PendingSubmissionRow
    {
        public string Uid { get; init; } = string.Empty;
        public string ScholarName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public string DownloadUrl { get; init; } = string.Empty;
        public string SubmissionDocId { get; init; } = string.Empty;

        public string StatusDisplay => string.Equals(Status, "pending", StringComparison.OrdinalIgnoreCase) ? "Pending Review" : Status;
        public string ScholarDisplay => $"Scholar: {ScholarName}";
        public string EmailDisplay => $"Email: {Email}";
        public string UploadedDisplay => CreatedAt == DateTime.MinValue ? "Uploaded: -" : $"Uploaded: {CreatedAt:yyyy-MM-dd}";
    }

    private void OnNotificationsClicked(object sender, EventArgs e)
    {
        NotificationsOverlay.IsVisible = true;
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        _ = AgriScholarApp.Helpers.AdminNotificationHelper.BuildNotificationsOverlayAsync(
            token, _firestore, NotificationsList, HeaderBadgeLabel, HeaderBadgeFrame,
            OverlayBadgeLabel, OverlayBadgeFrame, NotificationsOverlay);
    }

    private void OnCloseNotifications(object sender, EventArgs e) => NotificationsOverlay.IsVisible = false;

    private void OnMarkAllReadClicked(object sender, EventArgs e)
    {
        AgriScholarApp.Helpers.AdminNotificationHelper.MarkAllRead(
            NotificationsList, HeaderBadgeLabel, HeaderBadgeFrame,
            OverlayBadgeLabel, OverlayBadgeFrame, NotificationsOverlay);
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        AuthService.Logout();
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }

    private async void OnViewClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PendingSubmissionRow row)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.DownloadUrl))
        {
            await DisplayAlert("Missing", "No download URL saved for this submission.", "OK");
            return;
        }

        await Launcher.Default.OpenAsync(row.DownloadUrl);
    }

    private async void OnVerifyClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PendingSubmissionRow row)
        {
            return;
        }

        var confirmed = await DisplayAlert("Verify", "Mark this grade report as verified?", "Yes", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await UpdateStatusAsync(row, "verified");
    }

    private async void OnRejectClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PendingSubmissionRow row)
        {
            return;
        }

        if (_activeFilter != VerificationFilter.Pending)
        {
            return;
        }

        var confirmed = await DisplayAlert("Reject", "Reject this grade report?", "Yes", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await UpdateStatusAsync(row, "rejected");
    }

    private void OnPendingBoxTapped(object sender, EventArgs e)
    {
        ApplyFilter(VerificationFilter.Pending);
    }

    private void OnVerifiedBoxTapped(object sender, EventArgs e)
    {
        ApplyFilter(VerificationFilter.Verified);
    }

    private void OnRejectedBoxTapped(object sender, EventArgs e)
    {
        ApplyFilter(VerificationFilter.Rejected);
    }

    private async Task UpdateStatusAsync(PendingSubmissionRow row, string newStatus)
    {
        try
        {
            await EnsureAdminTokenAsync();
            var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            if (string.IsNullOrWhiteSpace(token))
            {
                await DisplayAlert("Error", "Admin token missing.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(row.Uid) || string.IsNullOrWhiteSpace(row.SubmissionDocId))
            {
                await DisplayAlert("Error", "Missing submission identifiers.", "OK");
                return;
            }

            var updateData = new Dictionary<string, object?>
            {
                ["status"] = newStatus,
                ["updatedAt"] = DateTime.UtcNow
            };

            if (string.Equals(newStatus, "verified", StringComparison.OrdinalIgnoreCase))
            {
                updateData["verifiedAt"] = DateTime.UtcNow;
            }
            else if (string.Equals(newStatus, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                updateData["rejectedAt"] = DateTime.UtcNow;
            }

            await _firestore.UpdateDocumentAsync($"documents/{row.Uid}/submissions/{row.SubmissionDocId}", token, updateData);
            await DisplayAlert("Saved", $"Status updated to {newStatus}.", "OK");
            await LoadPendingGradeReportsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnDashboardClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//AdminDashboardPage");
    }

    private async void OnScholarsManagementClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//ScholarsManagementPage");
    }

    private async void OnDocumentVerificationClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//DocumentVerificationPage");
    }

    private async void OnRequirementsAnnouncementsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//RequirementsAnnouncementsManagementPage");
    }

    private async void OnReportsAnalyticsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//ReportsAnalyticsPage");
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPage");
    }
}
