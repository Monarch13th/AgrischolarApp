using AgriScholarApp.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AgriScholarApp.Helpers
{
    public static class AdminNotificationHelper
    {
        public static async Task BuildNotificationsOverlayAsync(
            string idToken,
            FirestoreRestService firestore,
            StackBase notificationsList,
            Label headerBadgeLabel,
            Frame headerBadgeFrame,
            Label overlayBadgeLabel,
            Frame overlayBadgeFrame,
            VisualElement overlayContainer)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                notificationsList.Children.Clear();
            });

            var submissionsList = new List<(Dictionary<string, object?> Scholar, Dictionary<string, object?> Submission)>();

            try
            {
                var scholars = await firestore.ListDocumentsAsync("scholars", idToken);
                var tasks = scholars.Select(async s =>
                {
                    var uid = (s.GetValueOrDefault("uid") ?? s.GetValueOrDefault("scholarId"))?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(uid)) return;

                    try
                    {
                        var subs = await firestore.ListDocumentsAsync($"documents/{uid}/submissions", idToken);
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
            }
            catch
            {
                // Ignore errors like no scholars
            }

            var sortedSubmissions = submissionsList
                .OrderByDescending(x => GetDateField(x.Submission))
                .ToList();

            int count = sortedSubmissions.Count;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                headerBadgeLabel.Text = count.ToString();
                overlayBadgeLabel.Text = $"{count} new";

                headerBadgeFrame.IsVisible = count > 0;
                overlayBadgeFrame.IsVisible = count > 0;

                if (count == 0)
                {
                    var emptyLabel = new Label
                    {
                        Text = "No new notifications",
                        TextColor = Color.FromArgb("#9AA4B2"),
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 40, 0, 0)
                    };
                    notificationsList.Children.Add(emptyLabel);
                    return;
                }

                foreach (var item in sortedSubmissions)
                {
                    var scholar = item.Scholar;
                    var sub = item.Submission;

                    var first = scholar.GetValueOrDefault("firstName")?.ToString() ?? "";
                    var last = scholar.GetValueOrDefault("lastName")?.ToString() ?? "";
                    var name = $"{first} {last}".Trim();
                    if (string.IsNullOrWhiteSpace(name)) name = "Unknown Scholar";

                    var reqType = sub.GetValueOrDefault("documentCategory")?.ToString() ?? sub.GetValueOrDefault("type")?.ToString() ?? "requirement";
                    var dateRaw = GetDateField(sub);
                    var dateStr = dateRaw != DateTime.MinValue ? dateRaw.ToString("MMM dd, yyyy") : "Recently";

                    var grid = new Grid
                    {
                        Padding = new Thickness(18, 16),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = GridLength.Auto },
                            new ColumnDefinition { Width = GridLength.Star },
                            new ColumnDefinition { Width = GridLength.Auto }
                        },
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
                        RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } },
                        RowSpacing = 12, VerticalOptions = LayoutOptions.Start, HorizontalOptions = LayoutOptions.End
                    };
                    actionGrid.Add(new Frame { HeightRequest = 10, WidthRequest = 10, CornerRadius = 5, BackgroundColor = Color.FromArgb("#22C55E"), HasShadow = false, HorizontalOptions = LayoutOptions.End }, 0, 0);

                    var tapLabel = new Label { Text = "View →", TextColor = Color.FromArgb("#2DD4BF"), FontSize = 12, FontAttributes = FontAttributes.Bold };
                    var tapGesture = new TapGestureRecognizer();
                    tapGesture.Tapped += async (s, e) =>
                    {
                        overlayContainer.IsVisible = false;
                        await Shell.Current.GoToAsync("//DocumentVerificationPage");
                    };
                    tapLabel.GestureRecognizers.Add(tapGesture);

                    var hStack = new HorizontalStackLayout { Spacing = 14, HorizontalOptions = LayoutOptions.End };
                    hStack.Add(tapLabel);
                    actionGrid.Add(hStack, 0, 1);

                    grid.Add(actionGrid, 2, 0);

                    notificationsList.Children.Add(grid);
                    notificationsList.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#314055"), Opacity = 0.75 });
                }
            });
        }

        public static void MarkAllRead(
            StackBase notificationsList,
            Label headerBadgeLabel,
            Frame headerBadgeFrame,
            Label overlayBadgeLabel,
            Frame overlayBadgeFrame,
            VisualElement overlayContainer)
        {
            notificationsList.Children.Clear();
            var emptyLabel = new Label
            {
                Text = "No new notifications",
                TextColor = Color.FromArgb("#9AA4B2"),
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 40, 0, 0)
            };
            notificationsList.Children.Add(emptyLabel);
            headerBadgeFrame.IsVisible = false;
            overlayBadgeFrame.IsVisible = false;
            overlayContainer.IsVisible = false;
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
    }
}
