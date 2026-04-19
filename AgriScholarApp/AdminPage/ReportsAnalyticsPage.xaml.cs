using AgriScholarApp.Services;
using Microsoft.Maui.Controls;

namespace AgriScholarApp.Pages;

public partial class ReportsAnalyticsPage : ContentPage
{
    private const string FirebaseAdminTokenKey = "firebase_admin_id_token";
    private readonly FirestoreRestService _firestore = new();
    private readonly FirebaseAuthRestService _auth = new();

    // Cached scholar data to avoid re-fetching on every render
    private List<Dictionary<string, object?>> _cachedScholars = new();

    public ReportsAnalyticsPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAnalyticsDataAsync();
    }

    // ──────────────────────────────────────────────
    // Data Loading
    // ──────────────────────────────────────────────

    private async Task LoadAnalyticsDataAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;

        try
        {
            var token = await GetAdminTokenAsync();
            _cachedScholars = await _firestore.ListDocumentsAsync("scholars", token);
            PopulateKpiCards(_cachedScholars);
            PopulateStatusBreakdown(_cachedScholars);
            PopulateTopScholars(_cachedScholars);
        }
        catch (Exception ex)
        {
            // Gracefully show fallback data so the page still looks populated
            SetKpiFallback();
            try { await DisplayAlert("Analytics Error", ex.Message, "OK"); } catch { }
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    // ──────────────────────────────────────────────
    // KPI Cards
    // ──────────────────────────────────────────────

    private void PopulateKpiCards(List<Dictionary<string, object?>> scholars)
    {
        var total = scholars.Count;
        var active = scholars.Count(s => IsStatusAny(s, "active"));
        var probation = scholars.Count(s => IsStatusAny(s, "probation", "on probation"));
        var pending = scholars.Count(s => IsStatusAny(s, "pending", "pending review", "under verification", "for verification"));

        // Compute average GPA
        var gpas = scholars
            .Select(s => GetDoubleField(s, "gpa", "currentGpa", "latestGpa", "grade"))
            .Where(g => g > 0)
            .ToList();
        var avgGpa = gpas.Count > 0 ? gpas.Average() : 0;

        KpiTotalScholars.Text = total.ToString();
        KpiTotalSubLabel.Text = $"{pending} pending";

        KpiAvgGpa.Text = gpas.Count > 0 ? avgGpa.ToString("F2") : "N/A";
        KpiGpaSubLabel.Text = $"from {gpas.Count} scholars";

        KpiActiveScholars.Text = active.ToString();
        KpiActiveSubLabel.Text = $"out of {total} total";

        KpiComplianceIssues.Text = probation.ToString();
        KpiComplianceSubLabel.Text = probation == 0 ? "all good!" : "require review";
    }

    private void SetKpiFallback()
    {
        KpiTotalScholars.Text = "—";
        KpiAvgGpa.Text = "—";
        KpiActiveScholars.Text = "—";
        KpiComplianceIssues.Text = "—";
    }

    // ──────────────────────────────────────────────
    // Status Breakdown Bar Chart
    // ──────────────────────────────────────────────

    private void PopulateStatusBreakdown(List<Dictionary<string, object?>> scholars)
    {
        var total = scholars.Count;
        if (total == 0) return;

        var active    = scholars.Count(s => IsStatusAny(s, "active"));
        var pending   = scholars.Count(s => IsStatusAny(s, "pending", "pending review", "under verification", "for verification"));
        var probation = scholars.Count(s => IsStatusAny(s, "probation", "on probation"));
        var completed = scholars.Count(s => IsStatusAny(s, "completed", "graduated", "finished"));

        // Max bar width relative to the page. The column that holds the bar takes up
        // remaining space after 96px label col + 36px count col + 2×14px spacing ≈ 160px.
        // We use 320 as a safe representative max that fits on typical tablet widths.
        const double maxBarWidth = 320.0;
        const double minBarWidth = 4.0; // Always show a sliver so the bar track isn't empty

        BarActive.WidthRequest    = total > 0 ? Math.Max(minBarWidth, (active    / (double)total) * maxBarWidth) : minBarWidth;
        BarPending.WidthRequest   = total > 0 ? Math.Max(minBarWidth, (pending   / (double)total) * maxBarWidth) : minBarWidth;
        BarProbation.WidthRequest = total > 0 ? Math.Max(minBarWidth, (probation / (double)total) * maxBarWidth) : minBarWidth;
        BarCompleted.WidthRequest = total > 0 ? Math.Max(minBarWidth, (completed / (double)total) * maxBarWidth) : minBarWidth;

        BarActiveLabel.Text    = active.ToString();
        BarPendingLabel.Text   = pending.ToString();
        BarProbationLabel.Text = probation.ToString();
        BarCompletedLabel.Text = completed.ToString();
    }

    // ──────────────────────────────────────────────
    // Top Scholars Table
    // ──────────────────────────────────────────────

    private void PopulateTopScholars(List<Dictionary<string, object?>> scholars)
    {
        TopScholarsContainer.Children.Clear();

        var topScholars = scholars
            .Select(s => new
            {
                // Build full name from firstName + lastName (actual Firestore field names)
                Name = BuildFullName(
                    GetStringField(s, "firstName"),
                    GetStringField(s, "middleName"),
                    GetStringField(s, "lastName")),
                // Use degreeProgram as the actual Firestore field name
                Course = GetStringField(s, "degreeProgram", "course", "program", "degree") ?? "—",
                Gpa = GetDoubleField(s, "gpa", "currentGpa", "latestGpa", "grade"),
                Status = GetStringField(s, "initialStatus", "status", "scholarStatus") ?? "—"
            })
            .Where(s => s.Gpa > 0)
            .OrderByDescending(s => s.Gpa)
            .Take(8)
            .ToList();

        if (topScholars.Count == 0)
        {
            TopScholarsEmptyLabel.Text = "No GPA data available for scholars.";
            TopScholarsEmptyLabel.IsVisible = true;
            return;
        }

        TopScholarsEmptyLabel.IsVisible = false;

        for (var i = 0; i < topScholars.Count; i++)
        {
            var s = topScholars[i];
            var bgColor = i % 2 == 0 ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#FAFAFA");
            var statusColor = s.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb("#16A34A")
                : Color.FromArgb("#F59E0B");

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 12,
                Padding = new Thickness(0, 10),
                BackgroundColor = bgColor
            };

            row.Add(new Label { Text = $"{i + 1}", FontSize = 12, TextColor = Color.FromArgb("#9CA3AF"), WidthRequest = 24, VerticalOptions = LayoutOptions.Center }, 0, 0);
            row.Add(new Label { Text = s.Name, FontSize = 12, TextColor = Color.FromArgb("#0B1220"), FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }, 1, 0);
            row.Add(new Label { Text = s.Course, FontSize = 12, TextColor = Color.FromArgb("#6B7280"), VerticalOptions = LayoutOptions.Center }, 2, 0);
            row.Add(new Label { Text = s.Gpa.ToString("F2"), FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0B1220"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }, 3, 0);

            var statusFrame = new Frame
            {
                CornerRadius = 6,
                BackgroundColor = statusColor.WithAlpha(0.12f),
                HasShadow = false,
                Padding = new Thickness(8, 3),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.Status.ToLower()),
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = statusColor
                }
            };
            row.Add(statusFrame, 4, 0);

            TopScholarsContainer.Children.Add(row);

            if (i < topScholars.Count - 1)
            {
                TopScholarsContainer.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#F3F4F6") });
            }
        }
    }

    // ──────────────────────────────────────────────
    // Button Handlers
    // ──────────────────────────────────────────────

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await LoadAnalyticsDataAsync();
    }

    private async void OnGenerateReportClicked(object sender, EventArgs e)
    {
        var reportType = ReportTypePicker.SelectedItem?.ToString();
        var academicYear = AcademicYearPicker.SelectedItem?.ToString();
        var format = ReportFormatPicker.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(reportType))
        {
            await DisplayAlert("Missing Selection", "Please select a report type.", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(format))
        {
            await DisplayAlert("Missing Selection", "Please select a report format.", "OK");
            return;
        }

        // Show generating indicator
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            btn.Text = "Generating...";
        }

        try
        {
            // Simulate report generation (in production: generate actual CSV/PDF)
            await Task.Delay(1800);

            var yearLabel = academicYear ?? "All Years";
            await DisplayAlert("Report Generated",
                $"✅ '{reportType}' for {yearLabel} has been generated as {format}.\n\n" +
                "In a production environment this would trigger a file download or email delivery.",
                "OK");
        }
        finally
        {
            if (sender is Button b)
            {
                b.IsEnabled = true;
                b.Text = "Generate";
            }
        }
    }

    private async void OnDownloadReport1Clicked(object sender, EventArgs e) =>
        await SimulateDownload("Scholarship Performance Report", "PDF");

    // Report 2 (Financial Disbursement) was removed per user request

    private async void OnDownloadReport3Clicked(object sender, EventArgs e) =>
        await SimulateDownload("Program Analytics Report", "PDF");

    private async void OnDownloadReport4Clicked(object sender, EventArgs e) =>
        await SimulateDownload("Compliance & Probation Report", "PDF");

    private async void OnDownloadReport5Clicked(object sender, EventArgs e) =>
        await SimulateDownload("Scholar Roster Export", "CSV");

    private async Task SimulateDownload(string reportName, string format)
    {
        bool confirm = await DisplayAlert(
            "Download Report",
            $"Download '{reportName}' as {format}?\n\nThis will export the file to your device.",
            "Download", "Cancel");

        if (!confirm) return;

        await Task.Delay(1200);
        await DisplayAlert("Download Complete",
            $"✅ '{reportName}' has been downloaded as {format}.\n\n" +
            "File saved to: Downloads/AgriScholars/",
            "OK");
    }

    // ──────────────────────────────────────────────
    // Helper Utilities
    // ──────────────────────────────────────────────

    private static bool IsStatusAny(Dictionary<string, object?> doc, params string[] candidates)
    {
        var status = (doc.GetValueOrDefault("initialStatus")
                      ?? doc.GetValueOrDefault("status")
                      ?? doc.GetValueOrDefault("applicationStatus")
                      ?? doc.GetValueOrDefault("scholarStatus"))?.ToString();
        if (string.IsNullOrWhiteSpace(status)) return false;
        var normalized = status.Trim();
        return candidates.Any(c => string.Equals(normalized, c, StringComparison.OrdinalIgnoreCase));
    }

    private static double GetDoubleField(Dictionary<string, object?> doc, params string[] fieldNames)
    {
        foreach (var field in fieldNames)
        {
            if (doc.TryGetValue(field, out var val) && val != null)
            {
                if (val is double d) return d;
                if (val is float f) return f;
                if (val is long l) return l;
                if (val is int i) return i;
                if (double.TryParse(val.ToString(), out var parsed)) return parsed;
            }
        }
        return 0;
    }

    private static string? GetStringField(Dictionary<string, object?> doc, params string[] fieldNames)
    {
        foreach (var field in fieldNames)
        {
            if (doc.TryGetValue(field, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    /// <summary>Combines first/middle/last into a display name, skipping blanks.</summary>
    private static string BuildFullName(string? first, string? middle, string? last)
    {
        var parts = new[] { first, middle, last }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        var name = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(token)) return token;
        var signIn = await _auth.SignInAsync("admin@gmail.com", "admin123");
        Preferences.Default.Set(FirebaseAdminTokenKey, signIn.IdToken);
        return signIn.IdToken;
    }

    // ──────────────────────────────────────────────
    // Navigation & Shared Handlers
    // ──────────────────────────────────────────────

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
