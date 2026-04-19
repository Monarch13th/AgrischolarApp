using AgriScholarApp.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AgriScholarApp.Pages;

public partial class RequirementsAnnouncementsManagementPage : ContentPage
{
    private const string FirebaseAdminTokenKey = "firebase_admin_id_token";

    private readonly FirestoreRestService _firestore = new();
    private readonly FirebaseAuthRestService _auth = new();

    public ObservableCollection<RequirementRow> Requirements { get; } = new();
    public ObservableCollection<RequirementRow> FilteredRequirements { get; } = new();
    public ObservableCollection<AnnouncementRow> Announcements { get; } = new();

    private RequirementRow? _editing;

    public RequirementsAnnouncementsManagementPage()
    {
        InitializeComponent();
        BindingContext = this;

        StatusFilterPicker.SelectedIndex = 0;
        DeadlinePicker.Date = DateTime.Today.AddDays(7);
    }



    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
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

    private async Task LoadAsync()
    {
        try
        {
            await EnsureAdminTokenAsync();
            await LoadRequirementsAsync();
            await LoadAnnouncementsAsync();
            ApplyRequirementFilter();
        }
        catch (Exception ex) when (IsUnauthenticated(ex))
        {
            Preferences.Default.Remove(FirebaseAdminTokenKey);
            await EnsureAdminTokenAsync();
            await LoadRequirementsAsync();
            await LoadAnnouncementsAsync();
            ApplyRequirementFilter();
        }
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

    private async Task LoadRequirementsAsync()
    {
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        if (string.IsNullOrWhiteSpace(token))
        {
            Requirements.Clear();
            FilteredRequirements.Clear();
            return;
        }

        var docs = await _firestore.ListDocumentsAsync("requirements", token);
        var rows = docs
            .Select(d => RequirementRow.FromFirestore(d))
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        Requirements.Clear();
        foreach (var r in rows) Requirements.Add(r);
    }

    private async Task LoadAnnouncementsAsync()
    {
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        if (string.IsNullOrWhiteSpace(token))
        {
            Announcements.Clear();
            return;
        }

        var docs = await _firestore.ListDocumentsAsync("announcements", token);
        var rows = docs
            .Select(d => AnnouncementRow.FromFirestore(d))
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToList();

        Announcements.Clear();
        foreach (var a in rows) Announcements.Add(a);
    }

    private void ApplyRequirementFilter()
    {
        var selected = StatusFilterPicker.SelectedItem?.ToString() ?? "All";
        IEnumerable<RequirementRow> source = Requirements;

        if (string.Equals(selected, "Active", StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(r => string.Equals(r.Status, "Active", StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(selected, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(r => string.Equals(r.Status, "Disabled", StringComparison.OrdinalIgnoreCase));
        }

        FilteredRequirements.Clear();
        foreach (var r in source) FilteredRequirements.Add(r);
    }

    private void OnStatusFilterChanged(object sender, EventArgs e)
    {
        ApplyRequirementFilter();
    }

    private void OnCreateRequirementClicked(object sender, EventArgs e)
    {
        _editing = null;
        EditorTitleLabel.Text = "Create Requirement";
        SaveRequirementButton.Text = "Save";

        RequirementTitleEntry.Text = string.Empty;
        DescriptionEditor.Text = string.Empty;
        CategoryEntry.Text = "Academic";
        StatusEntry.Text = string.Empty;
        RequiredForEntry.Text = "All Scholars";
        DeadlinePicker.Date = DateTime.Today.AddDays(7);

        RequirementEditorOverlay.IsVisible = true;
    }

    private void OnEditRequirementClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not RequirementRow row)
        {
            return;
        }

        _editing = row;
        EditorTitleLabel.Text = "Edit Requirement";
        SaveRequirementButton.Text = "Save Changes";

        RequirementTitleEntry.Text = row.Title;
        DescriptionEditor.Text = row.Description;
        CategoryEntry.Text = row.Category;
        StatusEntry.Text = row.Status;
        RequiredForEntry.Text = row.RequiredFor;
        DeadlinePicker.Date = row.Deadline == DateTime.MinValue ? DateTime.Today.AddDays(7) : row.Deadline.ToLocalTime().Date;

        RequirementEditorOverlay.IsVisible = true;
    }

    private async void OnDeleteRequirementClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not RequirementRow row)
        {
            return;
        }

        var confirm = await DisplayAlert("Delete Requirement", $"Delete '{row.Title}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        try
        {
            await RunWithFreshAdminTokenAsync(async token =>
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    await DisplayAlert("Error", "Admin token missing.", "OK");
                    return;
                }

                var announcements = await _firestore.ListDocumentsAsync("announcements", token);
                foreach (var ann in announcements)
                {
                    var requirementDocId = GetString(ann, "requirementDocId") ?? GetString(ann, "requirementId");
                    var shouldDelete = !string.IsNullOrWhiteSpace(requirementDocId) && string.Equals(requirementDocId, row.DocId, StringComparison.Ordinal);

                    if (!shouldDelete)
                    {
                        var type = GetString(ann, "type") ?? string.Empty;
                        var message = GetString(ann, "message") ?? string.Empty;
                        shouldDelete = string.Equals(type, "requirement_created", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(row.Title)
                            && message.Contains(row.Title, StringComparison.OrdinalIgnoreCase);
                    }

                    if (shouldDelete)
                    {
                        var annDocId = GetString(ann, "__documentId") ?? GetString(ann, "docId") ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(annDocId))
                        {
                            await _firestore.DeleteDocumentAsync($"announcements/{annDocId}", token);
                        }
                    }
                }

                await _firestore.DeleteDocumentAsync($"requirements/{row.DocId}", token);
            });

            await LoadRequirementsAsync();
            await LoadAnnouncementsAsync();
            ApplyRequirementFilter();

            for (var i = Announcements.Count - 1; i >= 0; i--)
            {
                var a = Announcements[i];
                if (string.Equals(a.RequirementDocId, row.DocId, StringComparison.Ordinal))
                {
                    Announcements.RemoveAt(i);
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnExtendDeadlineClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not RequirementRow row)
        {
            return;
        }

        var newDate = await DisplayPromptAsync("Extend Deadline", "Enter new deadline (YYYY-MM-DD):", initialValue: row.DeadlineDisplay);
        if (string.IsNullOrWhiteSpace(newDate))
        {
            return;
        }

        if (!DateTime.TryParseExact(newDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            await DisplayAlert("Invalid date", "Use format YYYY-MM-DD", "OK");
            return;
        }

        try
        {
            await RunWithFreshAdminTokenAsync(async token =>
            {
                if (string.IsNullOrWhiteSpace(row.DocId))
                {
                    return;
                }

                var updates = new Dictionary<string, object?>
                {
                    ["deadline"] = dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    ["updatedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                };

                await _firestore.UpdateDocumentAsync($"requirements/{row.DocId}", token, updates);
            });

            await LoadRequirementsAsync();
            ApplyRequirementFilter();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private void OnCloseRequirementEditor(object sender, TappedEventArgs e)
    {
        RequirementEditorOverlay.IsVisible = false;
    }

    private void OnCancelRequirementEditor(object sender, EventArgs e)
    {
        RequirementEditorOverlay.IsVisible = false;
    }



    private async void OnSaveRequirementClicked(object sender, EventArgs e)
    {
        var title = (RequirementTitleEntry.Text ?? string.Empty).Trim();
        var category = (CategoryEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "Academic";
        }
        var description = (DescriptionEditor.Text ?? string.Empty).Trim();
        var status = (StatusEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            status = "Active";
        }
        var requiredFor = (RequiredForEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requiredFor))
        {
            requiredFor = "All Scholars";
        }
        var deadlineLocal = DeadlinePicker.Date;

        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Missing title", "Requirement Title is required.", "OK");
            return;
        }

        try
        {
            await RunWithFreshAdminTokenAsync(async token =>
            {
                var now = DateTime.UtcNow;
                var deadlineUtc = new DateTime(deadlineLocal.Year, deadlineLocal.Month, deadlineLocal.Day, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();

                if (_editing is null)
                {
                    var reqDocId = Guid.NewGuid().ToString("N");
                    var reqData = new Dictionary<string, object?>
                    {
                        ["docId"] = reqDocId,
                        ["title"] = title,
                        ["category"] = category,
                        ["description"] = description,
                        ["deadline"] = deadlineUtc.ToString("o", CultureInfo.InvariantCulture),
                        ["requiredFor"] = requiredFor,
                        ["status"] = status,
                        ["createdAt"] = now.ToString("o", CultureInfo.InvariantCulture),
                        ["updatedAt"] = now.ToString("o", CultureInfo.InvariantCulture)
                    };

                    await _firestore.CreateDocumentAsync("requirements", reqDocId, token, reqData);

                    var annDocId = Guid.NewGuid().ToString("N");
                    var message = $"Please upload your {title} before {deadlineLocal:MMMM d, yyyy}.";
                    var annData = new Dictionary<string, object?>
                    {
                        ["docId"] = annDocId,
                        ["type"] = "requirement_created",
                        ["title"] = "New Document Requirement",
                        ["message"] = message,
                        ["requirementDocId"] = reqDocId,
                        ["createdAt"] = now.ToString("o", CultureInfo.InvariantCulture)
                    };

                    await _firestore.CreateDocumentAsync("announcements", annDocId, token, annData);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(_editing.DocId))
                    {
                        return;
                    }

                    var updates = new Dictionary<string, object?>
                    {
                        ["title"] = title,
                        ["category"] = category,
                        ["description"] = description,
                        ["deadline"] = deadlineUtc.ToString("o", CultureInfo.InvariantCulture),
                        ["requiredFor"] = requiredFor,
                        ["status"] = status,
                        ["updatedAt"] = now.ToString("o", CultureInfo.InvariantCulture)
                    };

                    await _firestore.UpdateDocumentAsync($"requirements/{_editing.DocId}", token, updates);
                }
            });

            RequirementEditorOverlay.IsVisible = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task RunWithFreshAdminTokenAsync(Func<string, Task> action)
    {
        await EnsureAdminTokenAsync();
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Admin authentication token is missing.");
        }

        try
        {
            await action(token);
        }
        catch (Exception ex) when (IsUnauthenticated(ex))
        {
            Preferences.Default.Remove(FirebaseAdminTokenKey);
            await EnsureAdminTokenAsync();

            token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw;
            }

            await action(token);
        }
    }

    private static bool IsUnauthenticated(Exception ex)
    {
        return ex.Message.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("Missing or invalid authentication", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("\"code\": 401", StringComparison.OrdinalIgnoreCase);
    }

    private async void OnDashboardClicked(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//AdminDashboardPage");
    }

    private async void OnScholarsManagementClicked(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//ScholarsManagementPage");
    }

    private async void OnDocumentVerificationClicked(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//DocumentVerificationPage");
    }

    private async void OnReportsAnalyticsClicked(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//ReportsAnalyticsPage");
    }

    private async void OnSettingsClicked(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPage");
    }

    public sealed class RequirementRow
    {
        public string DocId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public DateTime Deadline { get; init; }
        public string RequiredFor { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }

        public string CategoryDisplay => $"Category: {Category}";
        public string DeadlineDisplay => Deadline == DateTime.MinValue ? "-" : Deadline.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string StatusDisplay => Status;

        public Color StatusBadgeColor => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb("#DCFCE7") : Color.FromArgb("#E5E7EB");
        public Color StatusTextColor => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb("#166534") : Color.FromArgb("#111827");

        public static RequirementRow FromFirestore(Dictionary<string, object?> doc)
        {
            return new RequirementRow
            {
                DocId = GetString(doc, "docId") ?? GetString(doc, "__documentId") ?? string.Empty,
                Title = GetString(doc, "title") ?? string.Empty,
                Category = GetString(doc, "category") ?? string.Empty,
                Description = GetString(doc, "description") ?? string.Empty,
                RequiredFor = GetString(doc, "requiredFor") ?? "All Scholars",
                Status = GetString(doc, "status") ?? "Active",
                Deadline = GetDateTime(doc, "deadline"),
                CreatedAt = GetDateTime(doc, "createdAt")
            };
        }
    }

    public sealed class AnnouncementRow
    {
        public string DocId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string RequirementDocId { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }

        public string CreatedAtDisplay => CreatedAt == DateTime.MinValue ? string.Empty : CreatedAt.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

        public static AnnouncementRow FromFirestore(Dictionary<string, object?> doc)
        {
            return new AnnouncementRow
            {
                DocId = GetString(doc, "docId") ?? GetString(doc, "__documentId") ?? string.Empty,
                Title = GetString(doc, "title") ?? string.Empty,
                Message = GetString(doc, "message") ?? string.Empty,
                RequirementDocId = GetString(doc, "requirementDocId") ?? GetString(doc, "requirementId") ?? string.Empty,
                CreatedAt = GetDateTime(doc, "createdAt")
            };
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
}
