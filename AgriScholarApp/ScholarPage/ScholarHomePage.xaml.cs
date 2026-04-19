namespace AgriScholarApp.ScholarPage;

public partial class ScholarHomePage : ContentPage
{
    private const string ScholarEmailKey = "scholar_email";
    private const string FirebaseIdTokenKey = "firebase_id_token";
    private const string FirebaseUidKey = "firebase_uid";

    private readonly AgriScholarApp.Services.FirestoreRestService _firestore = new();

    public ScholarHomePage()
    {
        InitializeComponent();

        ScholarEmailLabel.Text = Preferences.Default.Get(ScholarEmailKey, string.Empty);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadScholarProfileAsync();
        await LoadNotificationsAsync();
        await LoadHomeFeedAsync();
    }

    private async Task LoadHomeFeedAsync()
    {
        await LoadAnnouncementsAsync();
        await LoadUpcomingTasksAsync();
        await LoadUnitsDoneAsync();
    }

    private async Task LoadUnitsDoneAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);
            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
            {
                SetUnitsDone(0, 0);
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

                var status = (GetString(s, "status") ?? string.Empty).Trim();
                return string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase);
            });

            // Units Done is defined as the count of submitted REQUIRED documents out of 4.
            // These match the required submissions shown in DocumentsPage.
            var requiredTypes = new[]
            {
                "cor_1st_sem",
                "grade_1st_sem",
                "cor_2nd_sem",
                "grade_2nd_sem"
            };

            // Backwards compatible behavior:
            // If an older submission exists with type=grade_report and it's already approved/verified,
            // count it as 1st semester grade submission if the specific type isn't present.
            if (hasApprovedLegacyGradeReport && !submittedTypes.Contains("grade_1st_sem"))
            {
                submittedTypes.Add("grade_1st_sem");
            }

            var submittedCount = requiredTypes.Count(t => submittedTypes.Contains(t));
            SetUnitsDone(submittedCount, requiredTypes.Length);
        }
        catch
        {
            SetUnitsDone(0, 0);
        }
    }

    private void SetUnitsDone(int submitted, int total)
    {
        UnitsDoneValueLabel.Text = $"{submitted}/{total}";
        _unitsDoneSubmitted = submitted;
        _unitsDoneTotal = total;
        UpdateUnitsDoneProgress();
    }

    private int _unitsDoneSubmitted;
    private int _unitsDoneTotal;

    private void UpdateUnitsDoneProgress()
    {
        var total = _unitsDoneTotal;
        if (total <= 0)
        {
            UnitsDoneProgressFill.WidthRequest = 0;
            return;
        }

        var ratio = Math.Clamp(_unitsDoneSubmitted / (double)total, 0, 1);
        var maxWidth = Math.Max(0, UnitsDoneProgressFill.Parent is Grid g ? g.Width : 0);
        UnitsDoneProgressFill.WidthRequest = maxWidth * ratio;
    }

    private async Task LoadAnnouncementsAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            if (string.IsNullOrWhiteSpace(idToken))
            {
                SetAnnouncementCards(Array.Empty<HomeAnnouncement>());
                return;
            }

            var docs = await _firestore.ListDocumentsAsync("announcements", idToken);
            var anns = docs
                .Select(d => new HomeAnnouncement(
                    GetString(d, "title") ?? string.Empty,
                    GetString(d, "message") ?? string.Empty,
                    GetDateTime(d, "createdAt")))
                .Where(a => !string.IsNullOrWhiteSpace(a.Title) || !string.IsNullOrWhiteSpace(a.Message))
                .OrderByDescending(a => a.CreatedAt)
                .Take(2)
                .ToArray();

            SetAnnouncementCards(anns);
        }
        catch
        {
            SetAnnouncementCards(Array.Empty<HomeAnnouncement>());
        }
    }

    private void SetAnnouncementCards(HomeAnnouncement[] anns)
    {
        var hasAny = anns.Length > 0;
        AnnouncementsHeader.IsVisible = hasAny;

        AnnouncementCard1.IsVisible = anns.Length > 0;
        AnnouncementCard2.IsVisible = anns.Length > 1;

        if (anns.Length > 0)
        {
            AnnouncementTitle1.Text = anns[0].Title;
            AnnouncementBody1.Text = anns[0].Message;
            AnnouncementDate1.Text = FormatDate(anns[0].CreatedAt);
        }
        if (anns.Length > 1)
        {
            AnnouncementTitle2.Text = anns[1].Title;
            AnnouncementBody2.Text = anns[1].Message;
            AnnouncementDate2.Text = FormatDate(anns[1].CreatedAt);
        }
    }

    private async Task LoadUpcomingTasksAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            if (string.IsNullOrWhiteSpace(idToken))
            {
                SetUpcomingTasks(Array.Empty<HomeRequirementTask>());
                return;
            }

            var docs = await _firestore.ListDocumentsAsync("requirements", idToken);
            var tasks = docs
                .Select(d =>
                {
                    var title = (GetString(d, "title") ?? string.Empty).Trim();
                    var deadline = GetDateTime(d, "deadline");
                    var status = (GetString(d, "status") ?? "Active").Trim();

                    // Prefer updatedAt if present, otherwise createdAt.
                    var created = GetDateTime(d, "createdAt");
                    var updated = GetDateTime(d, "updatedAt");
                    var stamp = updated != DateTime.MinValue ? updated : created;

                    return new
                    {
                        Title = title,
                        Deadline = deadline,
                        Status = status,
                        Stamp = stamp
                    };
                })
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .Where(t => string.Equals(t.Status, "Active", StringComparison.OrdinalIgnoreCase))
                // Latest posted first (updated/created), then soonest deadline.
                .OrderByDescending(t => t.Stamp)
                .ThenBy(t => t.Deadline == DateTime.MinValue ? DateTime.MaxValue : t.Deadline)
                .Take(2)
                .Select(t => new HomeRequirementTask(t.Title, t.Deadline, t.Status))
                .ToArray();

            SetUpcomingTasks(tasks);
        }
        catch
        {
            SetUpcomingTasks(Array.Empty<HomeRequirementTask>());
        }
    }

    private void SetUpcomingTasks(HomeRequirementTask[] tasks)
    {
        var hasAny = tasks.Length > 0;
        UpcomingTasksHeader.IsVisible = hasAny;
        UpcomingTasksCard.IsVisible = hasAny;

        UpcomingTaskRow1.IsVisible = tasks.Length > 0;
        UpcomingTaskRow2.IsVisible = tasks.Length > 1;

        if (tasks.Length > 0)
        {
            UpcomingTaskTitle1.Text = tasks[0].Title;
            UpcomingTaskMeta1.Text = FormatDue(tasks[0].Deadline);
        }
        if (tasks.Length > 1)
        {
            UpcomingTaskTitle2.Text = tasks[1].Title;
            UpcomingTaskMeta2.Text = FormatDue(tasks[1].Deadline);
        }
    }

    private async void OnUpcomingTaskTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//DocumentsPage");
    }

    private static string FormatDate(DateTime dt)
    {
        if (dt == DateTime.MinValue) return string.Empty;
        return dt.ToLocalTime().ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatDue(DateTime dt)
    {
        if (dt == DateTime.MinValue) return "";
        return $"Due: {dt.ToLocalTime():MMMM d, yyyy}";
    }

    private readonly record struct HomeAnnouncement(string Title, string Message, DateTime CreatedAt);
    private readonly record struct HomeRequirementTask(string Title, DateTime Deadline, string Status);

    private async Task LoadScholarProfileAsync()
    {
        try
        {
            var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
            var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);
            var email = Preferences.Default.Get(ScholarEmailKey, string.Empty);

            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
            {
                ScholarCourseLabel.Text = "-";
                ScholarshipStatusLabel.Text = "-";
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

            if (me is null || me.Count == 0)
            {
                ScholarCourseLabel.Text = "-";
                return;
            }

            var degreeProgram =
                GetString(me, "degreeProgram") ??
                GetString(me, "DegreeProgram") ??
                GetString(me, "course") ??
                GetString(me, "program") ??
                string.Empty;
            var firstName = GetString(me, "firstName") ?? string.Empty;
            var lastName = GetString(me, "lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                ScholarNameLabel.Text = fullName;
            }
            else if (!string.IsNullOrWhiteSpace(email))
            {
                ScholarNameLabel.Text = email.Split('@')[0];
            }

            ScholarCourseLabel.Text = string.IsNullOrWhiteSpace(degreeProgram) ? "-" : degreeProgram.Trim();

            var status = GetString(me, "initialStatus") ?? GetString(me, "status") ?? string.Empty;
            var statusText = string.IsNullOrWhiteSpace(status) ? "-" : status.Trim();
            ScholarshipStatusLabel.Text = statusText;
            ScholarshipStatusLabel.TextColor = IsActiveStatus(statusText) ? Color.FromArgb("#16A34A") : Color.FromArgb("#6B7280");
        }
        catch
        {
            ScholarCourseLabel.Text = "-";
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

    private static bool IsActiveStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        var s = status.Trim().ToLowerInvariant();
        return s == "active";
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
                // ignore announcements load failure
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

        return DateTime.TryParse(v.ToString(), out var parsed) ? parsed.ToUniversalTime() : DateTime.MinValue;
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

    private async void OnSearchTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        NotificationsOverlay.IsVisible = false;
        QuickActionOverlay.IsVisible = false;

        SearchOverlay.IsVisible = true;
        SearchEntry.Text = string.Empty;
        SearchEntry.Focus();
        ApplySearchFilter(string.Empty);
    }

    private void OnSearchOverlayTapped(object sender, TappedEventArgs e)
    {
        CloseSearch();
    }

    private void OnCloseSearchTapped(object sender, TappedEventArgs e)
    {
        CloseSearch();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySearchFilter(e.NewTextValue);
    }

    private void CloseSearch()
    {
        SearchOverlay.IsVisible = false;
        SearchEntry.Unfocus();
        ApplySearchFilter(string.Empty);
    }

    private void ApplySearchFilter(string? query)
    {
        query ??= string.Empty;
        var q = query.Trim().ToLowerInvariant();
        var showAll = string.IsNullOrWhiteSpace(q);

        var statusMatch = q is "overdue" or "pending" or "approved" or "under review" or "review";

        var statsMatch = showAll || "scholarship active units done".Contains(q);
        var announcementsMatch = showAll ||
                                 "announcements scholarship renewal deadline academic performance review".Contains(q);
        var tasksMatch = showAll ||
                         "upcoming tasks submit grade report update contact information".Contains(q);

        if (!showAll && statusMatch)
        {
            announcementsMatch = true;
            tasksMatch = true;
        }

        StatsRow.IsVisible = statsMatch;

        AnnouncementsHeader.IsVisible = announcementsMatch;
        AnnouncementCard1.IsVisible = announcementsMatch;
        AnnouncementCard2.IsVisible = announcementsMatch;

        UpcomingTasksHeader.IsVisible = tasksMatch;
        UpcomingTasksCard.IsVisible = tasksMatch;
    }

    private async void OnNotificationsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        SearchOverlay.IsVisible = false;
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
        SearchOverlay.IsVisible = false;
        MenuOverlay.IsVisible = true;
    }

    private void OnMenuOverlayTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
    }

    private void OnHomeTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        // Already on Home
    }

    private async void OnRecordsTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//AcademicRecordsPage");
    }

    private async void OnQuickActionTapped(object sender, TappedEventArgs e)
    {
        MenuOverlay.IsVisible = false;
        NotificationsOverlay.IsVisible = false;
        SearchOverlay.IsVisible = false;
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
        await Shell.Current.GoToAsync("//UploadGradeReportPage?from=home");
    }

    private async void OnUploadDocumentTapped(object sender, TappedEventArgs e)
    {
        QuickActionOverlay.IsVisible = false;
        await Shell.Current.GoToAsync("//UploadDocumentPage?from=home");
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

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        MenuOverlay.IsVisible = false;
        QuickActionOverlay.IsVisible = false;
        Preferences.Default.Remove(ScholarEmailKey);
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }
}
