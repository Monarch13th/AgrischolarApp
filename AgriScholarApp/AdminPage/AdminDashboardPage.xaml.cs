using AgriScholarApp.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace AgriScholarApp.Pages;

public partial class AdminDashboardPage : ContentPage
{
    private const string FirebaseAdminTokenKey = "firebase_admin_id_token";
    private readonly FirestoreRestService _firestore = new();
    private readonly DonutChartDrawable _pendingApplicationsDonut = new();
    private readonly BarChartDrawable   _activeScholarsBarChart   = new();
    private readonly BarChartDrawable   _monthlyApplicationsChart = new();
    private readonly DonutChartDrawable _courseDonut              = new();
    private readonly FirebaseAuthRestService _auth = new();

    // Course color palette
    private static readonly string[] CoursePalette =
    [
        "#2DD4BF", "#FBBF24", "#60A5FA", "#8B5CF6",
        "#F87171", "#34D399", "#FB923C", "#A78BFA",
    ];

    // Avatar color palette (bg, text) for initials
    private static readonly (string Bg, string Text)[] AvatarPalette =
    [
        ("#FEF3C7", "#B45309"),
        ("#DCFCE7", "#15803D"),
        ("#DBEAFE", "#1D4ED8"),
        ("#FCE7F3", "#9D174D"),
        ("#EDE9FE", "#6D28D9"),
        ("#FFEDD5", "#C2410C"),
        ("#ECFDF5", "#047857"),
        ("#FFF1F2", "#BE123C"),
    ];

    public AdminDashboardPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        PendingApplicationsDonut.Drawable    = _pendingApplicationsDonut;
        ActiveScholarsBarChart.Drawable      = _activeScholarsBarChart;
        MonthlyApplicationsChart.Drawable    = _monthlyApplicationsChart;
        CourseDonut.Drawable                 = _courseDonut;

        await LoadDashboardAsync();
    }

    // ─── Main loader ──────────────────────────────────────────────────────────

    private async Task LoadDashboardAsync()
    {
        try
        {
            await LoadDashboardWithRetryAsync();
        }
        catch (Exception ex)
        {
            TotalScholarsValue.Text = "0";
            ActiveScholarsValue.Text = "0";
            PendingApplicationsCardValue.Text = "0";
            PendingApplicationsTotalValue.Text = "0";
            RecentAppsFooterLabel.Text = "Failed to load";

            try { await DisplayAlert("Dashboard Error", ex.Message, "OK"); }
            catch { /* ignore UI errors */ }
        }
    }

    private async Task LoadDashboardWithRetryAsync()
    {
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        if (string.IsNullOrWhiteSpace(token))
            token = await SignInAdminAndCacheAsync();

        try
        {
            await LoadWithTokenAsync(token);
        }
        catch
        {
            Preferences.Default.Remove(FirebaseAdminTokenKey);
            token = await SignInAdminAndCacheAsync();
            await LoadWithTokenAsync(token);
        }
    }

    private async Task<string> SignInAdminAndCacheAsync()
    {
        var signIn = await _auth.SignInAsync("admin@gmail.com", "admin123");
        Preferences.Default.Set(FirebaseAdminTokenKey, signIn.IdToken);
        return signIn.IdToken;
    }

    // ─── Core data binding ────────────────────────────────────────────────────

    private async Task LoadWithTokenAsync(string idToken)
    {
        var scholars = await _firestore.ListDocumentsAsync("scholars", idToken);
        var total = scholars.Count;

        // ── Stat card counts ──
        var active    = scholars.Count(s => IsStatusAny(s, "active"));
        var pending   = scholars.Count(s => IsStatusAny(s, "pending", "pending review", "under verification", "for verification"));
        var completed = scholars.Count(s => IsStatusAny(s, "completed", "graduated", "finished"));

        TotalScholarsValue.Text          = total.ToString();
        ActiveScholarsValue.Text         = active.ToString();
        PendingApplicationsCardValue.Text = pending.ToString();

        // ── Year-level donut ──
        var (y1, y2, y3, y4, _) = CountByYearLevel(scholars);

        var segments = new List<DonutChartDrawable.Segment>(4);
        if (y1 > 0) segments.Add(new DonutChartDrawable.Segment(y1, Color.FromArgb("#2DD4BF"))); // 1st Year (Teal)
        if (y2 > 0) segments.Add(new DonutChartDrawable.Segment(y2, Color.FromArgb("#FBBF24"))); // 2nd Year (Orange)
        if (y3 > 0) segments.Add(new DonutChartDrawable.Segment(y3, Color.FromArgb("#60A5FA"))); // 3rd Year (Blue)
        if (y4 > 0) segments.Add(new DonutChartDrawable.Segment(y4, Color.FromArgb("#8B5CF6"))); // 4th Year (Purple)

        _pendingApplicationsDonut.Segments = segments;
        PendingApplicationsDonut.Invalidate();
        PendingApplicationsTotalValue.Text = total.ToString();

        if (Year1LegendValue is not null) Year1LegendValue.Text = y1.ToString();
        if (Year2LegendValue is not null) Year2LegendValue.Text = y2.ToString();
        if (Year3LegendValue is not null) Year3LegendValue.Text = y3.ToString();
        if (Year4LegendValue is not null) Year4LegendValue.Text = y4.ToString();

        // ── Bar chart: active vs pending vs completed ──
        _activeScholarsBarChart.Bars =
        [
            new BarChartDrawable.Bar("Active",    active,    Color.FromArgb("#16A34A")),
            new BarChartDrawable.Bar("Pending",   pending,   Color.FromArgb("#F59E0B")),
            new BarChartDrawable.Bar("Completed", completed, Color.FromArgb("#6366F1")),
        ];
        ActiveScholarsBarChart.Invalidate();

        // ── Monthly Applications bar chart (last 6 months) ──
        BuildMonthlyChart(scholars);

        // ── Scholars by Course/Program donut ──
        BuildCourseDonut(scholars);

        // ── Recent applications (most-recent 5) ──
        var sorted = scholars
            .OrderByDescending(s => GetDateField(s))
            .Take(5)
            .ToList();

        BuildRecentApplicationsRows(sorted, total);
        RecentAppsTimestamp.Text = $"Updated {DateTime.Now:hh:mm tt}";
        
        await BuildNotificationsOverlayAsync(scholars, idToken);
    }

    // ─── Recent Applications UI builder ──────────────────────────────────────

    private void BuildRecentApplicationsRows(
        List<Dictionary<string, object?>> scholars, int totalCount)
    {
        RecentApplicationsList.Children.Clear();

        for (var i = 0; i < scholars.Count; i++)
        {
            var s      = scholars[i];
            var docId  = s.GetValueOrDefault("__documentId")?.ToString() ?? string.Empty;
            var first  = s.GetValueOrDefault("firstName")?.ToString() ?? string.Empty;
            var last   = s.GetValueOrDefault("lastName")?.ToString() ?? string.Empty;
            var name   = $"{first} {last}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Unknown Scholar";

            var initials = GetInitials(first, last);
            var course   = s.GetValueOrDefault("degreeProgram")?.ToString()
                          ?? s.GetValueOrDefault("course")?.ToString() ?? "—";
            var gpaRaw   = s.GetValueOrDefault("gpa");
            var gpa      = gpaRaw is double gd  ? gd.ToString("0.00")
                         : gpaRaw is string gs  ? gs
                         : "—";
            var status   = (s.GetValueOrDefault("initialStatus")
                           ?? s.GetValueOrDefault("status")
                           ?? s.GetValueOrDefault("applicationStatus"))?.ToString() ?? "—";
            var dateRaw  = GetDateField(s);
            var dateStr  = dateRaw != DateTime.MinValue ? dateRaw.ToString("yyyy-MM-dd") : "—";
            var studentId = s.GetValueOrDefault("studentId")?.ToString() ?? docId;

            var palette       = AvatarPalette[i % AvatarPalette.Length];
            var (badgeBg, badgeBorder, dotColor, textColor) = GetStatusColors(status);
            var (gpaBg, gpaText) = GetGpaColors(gpa);

            // Divider
            if (i > 0)
            {
                RecentApplicationsList.Children.Add(
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#F1F5F9") });
            }

            // Row grid
            var row = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                ],
                ColumnSpacing = 12,
                Padding = new Thickness(18, 14),
                BackgroundColor = Colors.White,
            };

            // Col 0 – avatar
            var avatar = new Frame
            {
                HeightRequest    = 36,
                WidthRequest     = 36,
                CornerRadius     = 18,
                BackgroundColor  = Color.FromArgb(palette.Bg),
                HasShadow        = false,
                Padding          = 0,
                BorderColor      = Colors.Transparent,
                Content          = new Label
                {
                    Text            = initials,
                    FontSize        = 12,
                    FontAttributes  = FontAttributes.Bold,
                    TextColor       = Color.FromArgb(palette.Text),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions   = LayoutOptions.Center,
                },
            };
            row.Add(avatar, 0, 0);

            // Col 1 – name + id
            var nameStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
            nameStack.Add(new Label { Text = name, FontAttributes = FontAttributes.Bold, FontSize = 13, TextColor = Color.FromArgb("#0B1220") });
            nameStack.Add(new Label { Text = $"{studentId}  ·  {dateStr}", FontSize = 11, TextColor = Color.FromArgb("#9AA4B2") });
            row.Add(nameStack, 1, 0);

            // Col 2 – course
            var courseLabel = new Label
            {
                Text              = TruncateCourse(course),
                FontSize          = 12,
                TextColor         = Color.FromArgb("#374151"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };
            row.Add(courseLabel, 2, 0);

            // Col 3 – GPA chip
            var gpaFrame = new Frame
            {
                CornerRadius    = 8,
                BackgroundColor = Color.FromArgb(gpaBg),
                HasShadow       = false,
                Padding         = new Thickness(10, 4),
                BorderColor     = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                Content         = new Label { Text = gpa, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(gpaText) },
            };
            row.Add(gpaFrame, 3, 0);

            // Col 4 – status badge
            var dot = new BoxView { WidthRequest = 6, HeightRequest = 6, CornerRadius = 3, Color = Color.FromArgb(dotColor), VerticalOptions = LayoutOptions.Center };
            var badgeLabel = new Label { Text = status, FontSize = 10, TextColor = Color.FromArgb(textColor), FontAttributes = FontAttributes.Bold };
            var badgeStack = new HorizontalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
            badgeStack.Add(dot);
            badgeStack.Add(badgeLabel);
            var badgeFrame = new Frame
            {
                CornerRadius    = 20,
                BackgroundColor = Color.FromArgb(badgeBg),
                HasShadow       = false,
                Padding         = new Thickness(10, 5),
                BorderColor     = Color.FromArgb(badgeBorder),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                Content         = badgeStack,
            };
            row.Add(badgeFrame, 4, 0);

            // Col 5 – action button
            var capturedDocId = docId;
            var btnLabel = new Label { Text = "View Details →", FontSize = 11, TextColor = Colors.White, FontAttributes = FontAttributes.Bold };
            var btnFrame = new Frame
            {
                CornerRadius    = 8,
                BackgroundColor = Color.FromArgb("#0B1220"),
                HasShadow       = false,
                Padding         = new Thickness(12, 6),
                BorderColor     = Colors.Transparent,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
                Content         = btnLabel,
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OnViewScholarDetailsTapped(capturedDocId);
            btnFrame.GestureRecognizers.Add(tap);
            row.Add(btnFrame, 5, 0);

            RecentApplicationsList.Children.Add(row);
        }

        RecentAppsFooterLabel.Text = scholars.Count < totalCount
            ? $"Showing {scholars.Count} of {totalCount} scholars"
            : $"Showing all {totalCount} scholars";
    }

    // ─── Navigation ──────────────────────────────────────────────────────────

    private void OnViewScholarDetailsTapped(string docId)
    {
        // Navigate to scholars page (deep-link with ID if routing supports it)
        MainThread.BeginInvokeOnMainThread(async () =>
            await Shell.Current.GoToAsync("//ScholarsManagementPage"));
    }

    private async void OnViewAllApplicationsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//ScholarsManagementPage");
    }

    // ─── Monthly Applications Chart ───────────────────────────────────────────

    private void BuildMonthlyChart(List<Dictionary<string, object?>> scholars)
    {
        var now   = DateTime.Now;
        var months = new string[6];
        var counts = new int[6];

        // Build last 6 month labels
        for (var i = 5; i >= 0; i--)
        {
            var m = now.AddMonths(-i);
            months[5 - i] = m.ToString("MMM");
        }

        // Count scholars whose dateAdded falls in each month bucket
        foreach (var s in scholars)
        {
            var d = GetDateField(s);
            if (d == DateTime.MinValue) continue;
            for (var i = 5; i >= 0; i--)
            {
                var bucket = now.AddMonths(-i);
                if (d.Year == bucket.Year && d.Month == bucket.Month)
                {
                    counts[5 - i]++;
                    break;
                }
            }
        }

        // Fallback: if all zeros (no date data), spread total scholars across months
        if (counts.All(c => c == 0) && scholars.Count > 0)
        {
            var perMonth = Math.Max(1, scholars.Count / 6);
            for (var i = 0; i < 6; i++) counts[i] = perMonth;
            counts[^1] += scholars.Count - (perMonth * 6); // remainder in last month
        }

        var barColors = new[] { "#2DD4BF", "#60A5FA", "#8B5CF6", "#FBBF24", "#34D399", "#16A34A" };
        var bars = new List<BarChartDrawable.Bar>();
        for (var i = 0; i < 6; i++)
            bars.Add(new BarChartDrawable.Bar(months[i], counts[i], Color.FromArgb(barColors[i])));

        _monthlyApplicationsChart.Bars = bars;
        MonthlyApplicationsChart.Invalidate();

        // Update summary chips
        var peak    = counts.Max();
        var peakIdx = Array.IndexOf(counts, peak);
        var total6  = counts.Sum();
        var avg     = total6 > 0 ? (double)total6 / 6 : 0;

        MonthlyPeakLabel.Text  = $"{months[peakIdx]} ({peak})";
        MonthlyAvgLabel.Text   = avg.ToString("0.0");
        MonthlyTotalLabel.Text = total6.ToString();
    }

    // ─── Scholars by Course Donut ─────────────────────────────────────────────

    private void BuildCourseDonut(List<Dictionary<string, object?>> scholars)
    {
        // Count by normalized program
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in scholars)
        {
            var raw = (s.GetValueOrDefault("degreeProgram")
                      ?? s.GetValueOrDefault("course")
                      ?? s.GetValueOrDefault("program"))?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) raw = "Other";
            raw = AbbreviateCourse(raw.Trim());
            counts[raw] = counts.GetValueOrDefault(raw, 0) + 1;
        }

        // Sort descending, keep top 6, lump rest into "Other"
        var sorted = counts.OrderByDescending(kv => kv.Value).ToList();
        var top    = sorted.Take(6).ToList();
        var others = sorted.Skip(6).Sum(kv => kv.Value);
        if (others > 0) top.Add(new KeyValuePair<string, int>("Other", others));

        var segments = new List<DonutChartDrawable.Segment>();
        for (var i = 0; i < top.Count; i++)
            segments.Add(new DonutChartDrawable.Segment(top[i].Value, Color.FromArgb(CoursePalette[i % CoursePalette.Length])));

        _courseDonut.Segments = segments;
        CourseDonut.Invalidate();
        CourseTotalValue.Text = scholars.Count.ToString();

        // Build dynamic legend
        CourseLegend.Children.Clear();
        for (var i = 0; i < top.Count; i++)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                ],
                ColumnSpacing = 8,
            };
            row.Add(new BoxView
            {
                HeightRequest = 8, WidthRequest = 8, CornerRadius = 4,
                Color = Color.FromArgb(CoursePalette[i % CoursePalette.Length]),
                VerticalOptions = LayoutOptions.Center,
            }, 0, 0);
            row.Add(new Label
            {
                Text = top[i].Key, FontSize = 11, TextColor = Color.FromArgb("#0B1220"),
                VerticalOptions = LayoutOptions.Center,
            }, 1, 0);
            row.Add(new Label
            {
                Text = top[i].Value.ToString(), FontSize = 11,
                TextColor = Color.FromArgb("#6B7280"),
                FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
            }, 2, 0);
            CourseLegend.Children.Add(row);
        }
    }

    private static string AbbreviateCourse(string course)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bs agricultural engineering",  "Agri. Engr." },
            { "agricultural engineering",     "Agri. Engr." },
            { "bs agribusiness management",   "Agribusiness" },
            { "bs agribusiness",              "Agribusiness" },
            { "bs agriculture",               "BS Agri" },
            { "bs forestry",                  "Forestry" },
            { "bs agricultural technology",   "Agri Tech" },
            { "bs animal science",            "Animal Sci." },
            { "bs food technology",           "Food Tech" },
            { "bs environmental science",     "Env. Science" },
        };
        return map.TryGetValue(course, out var abbr) ? abbr : (course.Length > 14 ? course[..14] + "…" : course);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (int y1, int y2, int y3, int y4, int other) CountByYearLevel(
        List<Dictionary<string, object?>> scholars)
    {
        int y1 = 0, y2 = 0, y3 = 0, y4 = 0, yOther = 0;
        foreach (var s in scholars)
        {
            var raw = (s.GetValueOrDefault("yearLevel")
                      ?? s.GetValueOrDefault("year")
                      ?? s.GetValueOrDefault("gradeLevel")
                      ?? s.GetValueOrDefault("yearLvl"));

            // Handle numeric year stored as long/int
            if (raw is long lg)
            {
                if (lg == 1) y1++;
                else if (lg == 2) y2++;
                else if (lg == 3) y3++;
                else if (lg == 4) y4++;
                else yOther++;
                continue;
            }

            var norm = (raw?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(norm)) { yOther++; continue; }

            // Numeric strings: "1", "2", "3", "4"
            if (norm == "1") { y1++; continue; }
            if (norm == "2") { y2++; continue; }
            if (norm == "3") { y3++; continue; }
            if (norm == "4") { y4++; continue; }

            // Ordinal: "1st", "2nd", "3rd", "4th" (with or without " year")
            if (norm.StartsWith("1st")) { y1++; continue; }
            if (norm.StartsWith("2nd")) { y2++; continue; }
            if (norm.StartsWith("3rd")) { y3++; continue; }
            if (norm.StartsWith("4th")) { y4++; continue; }

            // Written: "first", "second", "third", "fourth"
            if (norm.Contains("first"))  { y1++; continue; }
            if (norm.Contains("second")) { y2++; continue; }
            if (norm.Contains("third"))  { y3++; continue; }
            if (norm.Contains("fourth")) { y4++; continue; }

            // "year 1" / "year 2" etc.
            if (norm.Contains("year 1") || norm.Contains("yr 1") || norm.Contains("yr1")) { y1++; continue; }
            if (norm.Contains("year 2") || norm.Contains("yr 2") || norm.Contains("yr2")) { y2++; continue; }
            if (norm.Contains("year 3") || norm.Contains("yr 3") || norm.Contains("yr3")) { y3++; continue; }
            if (norm.Contains("year 4") || norm.Contains("yr 4") || norm.Contains("yr4")) { y4++; continue; }

            yOther++;
        }
        return (y1, y2, y3, y4, yOther);
    }

    private static DateTime GetDateField(Dictionary<string, object?> doc)
    {
        var raw = doc.GetValueOrDefault("dateAdded")
                 ?? doc.GetValueOrDefault("createdAt")
                 ?? doc.GetValueOrDefault("applicationDate");
        if (raw is DateTime dt) return dt;
        if (raw is string s && DateTime.TryParse(s, out var parsed)) return parsed;
        return DateTime.MinValue;
    }

    private static string GetInitials(string first, string last)
    {
        var f = first.Length > 0 ? first[0].ToString().ToUpper() : string.Empty;
        var l = last.Length  > 0 ? last[0].ToString().ToUpper()  : string.Empty;
        return (f + l).Length > 0 ? f + l : "??";
    }

    private static string TruncateCourse(string course)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "agricultural engineering", "Agri. Engr." },
            { "bs agricultural engineering", "Agri. Engr." },
            { "bs agribusiness", "BS Agribusiness" },
            { "bs agriculture", "BS Agriculture" },
            { "bs forestry", "BS Forestry" },
        };
        return known.TryGetValue(course.Trim(), out var abbr) ? abbr : course;
    }

    private static (string badgeBg, string badgeBorder, string dotColor, string textColor)
        GetStatusColors(string status)
    {
        var norm = status.Trim().ToLowerInvariant();
        if (norm.Contains("approved"))          return ("#F0FDF4", "#BBF7D0", "#16A34A", "#15803D");
        if (norm.Contains("pending"))           return ("#FEF9EC", "#FDE68A", "#D97706", "#B45309");
        if (norm.Contains("verification"))      return ("#EFF6FF", "#BFDBFE", "#2563EB", "#1D4ED8");
        if (norm.Contains("completed") ||
            norm.Contains("graduated"))         return ("#F5F3FF", "#DDD6FE", "#7C3AED", "#6D28D9");
        if (norm.Contains("active"))            return ("#F0FDF4", "#BBF7D0", "#16A34A", "#15803D");
        return ("#F3F4F6", "#E5E7EB", "#6B7280", "#374151");
    }

    private static (string bg, string text) GetGpaColors(string gpa)
    {
        if (double.TryParse(gpa, out var v))
        {
            if (v >= 3.5) return ("#F0FDF4", "#15803D");
            if (v >= 2.75) return ("#EFF6FF", "#1D4ED8");
            return ("#FEF9EC", "#B45309");
        }
        return ("#F3F4F6", "#6B7280");
    }

    private static bool IsStatusAny(Dictionary<string, object?> doc, params string[] candidates)
    {
        if (candidates is null || candidates.Length == 0) return false;
        var status = (doc.GetValueOrDefault("initialStatus")
                     ?? doc.GetValueOrDefault("status")
                     ?? doc.GetValueOrDefault("applicationStatus")
                     ?? doc.GetValueOrDefault("scholarStatus"))?.ToString();
        if (string.IsNullOrWhiteSpace(status)) return false;
        var normalized = status.Trim();
        return candidates.Any(c => string.Equals(normalized, c, StringComparison.OrdinalIgnoreCase));
    }

    // ─── Notifications / nav handlers ────────────────────────────────────────

    private void OnNotificationsClicked(object sender, EventArgs e)   => NotificationsOverlay.IsVisible = true;
    private void OnCloseNotifications(object sender, EventArgs e)     => NotificationsOverlay.IsVisible = false;
    private void OnMarkAllReadClicked(object sender, EventArgs e)
    {
        NotificationsList.Children.Clear();
        var emptyLabel = new Label
        {
            Text = "No new notifications",
            TextColor = Color.FromArgb("#9AA4B2"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 40, 0, 0)
        };
        NotificationsList.Children.Add(emptyLabel);
        HeaderBadgeFrame.IsVisible = false;
        OverlayBadgeFrame.IsVisible = false;
        NotificationsOverlay.IsVisible = false;
    }

    private async Task BuildNotificationsOverlayAsync(List<Dictionary<string, object?>> scholars, string idToken)
    {
        NotificationsList.Children.Clear();

        var submissionsList = new List<(Dictionary<string, object?> Scholar, Dictionary<string, object?> Submission)>();

        var tasks = scholars.Select(async s => 
        {
            var uid = (s.GetValueOrDefault("uid") ?? s.GetValueOrDefault("scholarId"))?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(uid)) return;

            try
            {
                var subs = await _firestore.ListDocumentsAsync($"documents/{uid}/submissions", idToken);
                foreach (var sub in subs)
                {
                    var status = sub.GetValueOrDefault("status")?.ToString() ?? "pending";
                    if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (submissionsList)
                        {
                            submissionsList.Add((s, sub));
                        }
                    }
                }
            }
            catch { /* Ignore if no submissions exist for this UID */ }
        });

        await Task.WhenAll(tasks);

        var sortedSubmissions = submissionsList
            .OrderByDescending(x => GetDateField(x.Submission))
            .ToList();

        int count = sortedSubmissions.Count;

        HeaderBadgeLabel.Text = count.ToString();
        OverlayBadgeLabel.Text = $"{count} new";
        
        HeaderBadgeFrame.IsVisible = count > 0;
        OverlayBadgeFrame.IsVisible = count > 0;

        if (count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No new notifications",
                TextColor = Color.FromArgb("#9AA4B2"),
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 40, 0, 0)
            };
            NotificationsList.Children.Add(emptyLabel);
            return;
        }

        foreach (var item in sortedSubmissions)
        {
            var scholar = item.Scholar;
            var sub = item.Submission;

            var first  = scholar.GetValueOrDefault("firstName")?.ToString() ?? "";
            var last   = scholar.GetValueOrDefault("lastName")?.ToString() ?? "";
            var name   = $"{first} {last}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Unknown Scholar";

            var reqType = sub.GetValueOrDefault("documentCategory")?.ToString() ?? sub.GetValueOrDefault("type")?.ToString() ?? "requirement";
            var dateRaw = GetDateField(sub);
            var dateStr = dateRaw != DateTime.MinValue ? dateRaw.ToString("MMM dd, yyyy") : "Recently";

            var grid = new Grid
            {
                Padding = new Thickness(18, 16),
                ColumnDefinitions =
                [
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                ],
                ColumnSpacing = 14,
                BackgroundColor = Color.FromArgb("#263548")
            };

            var iconFrame = new Frame
            {
                HeightRequest = 40, WidthRequest = 40, CornerRadius = 20,
                BackgroundColor = Color.FromArgb("#E9D5FF"), HasShadow = false, Padding = 0,
                VerticalOptions = LayoutOptions.Start,
                Content = new Label { Text = "📄", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, FontSize = 18, TextColor = Color.FromArgb("#6D28D9") }
            };
            grid.Add(iconFrame, 0, 0);

            var vStack = new VerticalStackLayout { Spacing = 4 };
            vStack.Add(new Label { Text = "New Requirement Submitted", TextColor = Colors.White, FontAttributes = FontAttributes.Bold, FontSize = 14 });
            vStack.Add(new Label { Text = $"{name} submitted a new {reqType} for review.", TextColor = Color.FromArgb("#AAB4C3"), FontSize = 12, LineBreakMode = LineBreakMode.WordWrap });
            vStack.Add(new Label { Text = dateStr, TextColor = Color.FromArgb("#8A94A6"), FontSize = 11 });
            grid.Add(vStack, 1, 0);

            var actionGrid = new Grid
            {
                RowDefinitions = [ new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } ],
                RowSpacing = 12, VerticalOptions = LayoutOptions.Start, HorizontalOptions = LayoutOptions.End
            };
            actionGrid.Add(new Frame { HeightRequest = 10, WidthRequest = 10, CornerRadius = 5, BackgroundColor = Color.FromArgb("#22C55E"), HasShadow = false, HorizontalOptions = LayoutOptions.End }, 0, 0);

            var tapLabel = new Label { Text = "View →", TextColor = Color.FromArgb("#2DD4BF"), FontSize = 12, FontAttributes = FontAttributes.Bold };
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                NotificationsOverlay.IsVisible = false;
                await Shell.Current.GoToAsync("//DocumentVerificationPage");
            };
            tapLabel.GestureRecognizers.Add(tapGesture);
            
            var hStack = new HorizontalStackLayout { Spacing = 14, HorizontalOptions = LayoutOptions.End };
            hStack.Add(tapLabel);
            actionGrid.Add(hStack, 0, 1);
            
            grid.Add(actionGrid, 2, 0);

            NotificationsList.Children.Add(grid);
            NotificationsList.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#314055"), Opacity = 0.75 });
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        AuthService.Logout();
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }

    private async void OnDashboardClicked(object sender, EventArgs e)             => await Shell.Current.GoToAsync("//AdminDashboardPage");
    private async void OnScholarsManagementClicked(object sender, EventArgs e)    => await Shell.Current.GoToAsync("//ScholarsManagementPage");
    private async void OnDocumentVerificationClicked(object sender, EventArgs e)  => await Shell.Current.GoToAsync("//DocumentVerificationPage");
    private async void OnRequirementsAnnouncementsClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("//RequirementsAnnouncementsManagementPage");
    private async void OnReportsAnalyticsClicked(object sender, EventArgs e)      => await Shell.Current.GoToAsync("//ReportsAnalyticsPage");
    private async void OnSettingsClicked(object sender, EventArgs e)              => await Shell.Current.GoToAsync("//SettingsPage");
}

// ═══════════════════════════════════════════════════════════════════════════════
// Donut chart (year-level breakdown)
// ═══════════════════════════════════════════════════════════════════════════════
internal sealed class DonutChartDrawable : IDrawable
{
    internal readonly record struct Segment(float Value, Color Color);

    public IReadOnlyList<Segment> Segments { get; set; } = Array.Empty<Segment>();
    public float MinSweepDegrees  { get; set; } = 3f;
    public float SegmentGapDegrees { get; set; } = 0f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        try
        {
            canvas.Antialias = true;
            var cx = dirtyRect.Center.X;
            var cy = dirtyRect.Center.Y;
            var radius    = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f;
            var thickness = Math.Max(10f, radius * 0.22f);
            var outer     = radius - 1f;
            var inner     = Math.Max(0f, outer - thickness);
            var total     = Segments.Sum(s => s.Value);

            if (total <= 0)
            {
                canvas.StrokeColor = Color.FromArgb("#E5E7EB");
                canvas.StrokeSize  = thickness;
                canvas.DrawCircle(cx, cy, inner + thickness / 2f);
                return;
            }

            var start = 270f;
            foreach (var seg in Segments)
            {
                var sweep = seg.Value / total * 360f;
                var drawSweep = sweep;
                if (Segments.Count > 1 && SegmentGapDegrees > 0) drawSweep -= SegmentGapDegrees;

                canvas.StrokeColor   = seg.Color;
                canvas.StrokeSize    = thickness;
                canvas.StrokeLineCap = LineCap.Round;
                
                var endAngle = start + drawSweep;
                
                if (drawSweep >= 359.9f)
                {
                    canvas.DrawCircle(cx, cy, inner + thickness / 2f);
                }
                else
                {
                    var path = new PathF();
                    // In MAUI, clockwise=false corresponds to 'increasing angle'. 
                    // Since start < endAngle, passing false draws the intended short arc.
                    path.AddArc(
                        cx - (inner + thickness / 2f), cy - (inner + thickness / 2f),
                        (inner + thickness / 2f) * 2f, (inner + thickness / 2f) * 2f,
                        start, endAngle, false);
                    
                    canvas.DrawPath(path);
                }
                
                start += sweep;
            }

            canvas.StrokeColor = Color.FromArgb("#E5E7EB");
            canvas.StrokeSize  = 1;
            canvas.DrawCircle(cx, cy, outer);
        }
        finally { canvas.RestoreState(); }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Bar chart (active / pending / completed)
// ═══════════════════════════════════════════════════════════════════════════════
internal sealed class BarChartDrawable : IDrawable
{
    internal readonly record struct Bar(string Label, float Value, Color Color);

    public IReadOnlyList<Bar> Bars { get; set; } = Array.Empty<Bar>();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        try
        {
            canvas.Antialias = true;
            if (Bars.Count == 0) return;

            const float padLeft   = 36f;
            const float padRight  = 12f;
            const float padTop    = 10f;
            const float padBottom = 28f;
            const float barGap    = 12f;

            var plotX = dirtyRect.X + padLeft;
            var plotY = dirtyRect.Y + padTop;
            var plotW = dirtyRect.Width  - padLeft - padRight;
            var plotH = dirtyRect.Height - padTop  - padBottom;

            var maxVal = Bars.Max(b => b.Value);
            if (maxVal < 1) maxVal = 1;

            // Horizontal grid lines + Y labels
            canvas.FontSize  = 9f;
            canvas.FontColor = Color.FromArgb("#9AA4B2");
            for (var i = 0; i <= 4; i++)
            {
                var y = plotY + plotH * (1f - i / 4f);
                canvas.StrokeColor   = Color.FromArgb("#F1F5F9");
                canvas.StrokeSize    = 1;
                canvas.DrawLine(plotX, y, plotX + plotW, y);
                var yVal = (int)Math.Round(maxVal * i / 4f);
                canvas.DrawString(yVal.ToString(), dirtyRect.X, y - 5f, padLeft - 4f, 14f, HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            var groupW  = plotW / Bars.Count;
            var barW    = Math.Max(6f, groupW - barGap);
            const float cornerR = 4f;

            for (var i = 0; i < Bars.Count; i++)
            {
                var bar    = Bars[i];
                var barH   = bar.Value <= 0 ? 2f : plotH * (bar.Value / maxVal);
                var bx     = plotX + groupW * i + (groupW - barW) / 2f;
                var by     = plotY + plotH - barH;

                // Bar fill with rounded top
                canvas.FillColor = bar.Color;
                canvas.FillRoundedRectangle(bx, by, barW, barH, cornerR);

                // Value label above bar
                canvas.FontSize  = 10f;
                canvas.FontColor = Color.FromArgb("#0B1220");
                if (bar.Value > 0)
                    canvas.DrawString(((int)bar.Value).ToString(), bx, by - 14f, barW, 14f, HorizontalAlignment.Center, VerticalAlignment.Center);

                // X-axis label
                canvas.FontSize  = 9f;
                canvas.FontColor = Color.FromArgb("#6B7280");
                canvas.DrawString(bar.Label, bx, plotY + plotH + 4f, barW, padBottom - 4f, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
        finally { canvas.RestoreState(); }
    }
}
