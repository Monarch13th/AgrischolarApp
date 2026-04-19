import os
import re

files_to_process = [
    'AdminPage/DocumentVerificationPage.xaml',
    'AdminPage/ReportsAnalyticsPage.xaml',
    'AdminPage/RequirementsAnnouncementsManagementPage.xaml',
    'AdminPage/ScholarsManagementPage.xaml',
    'AdminPage/SettingsPage.xaml'
]

for file in files_to_process:
    if not os.path.exists(file):
        print(f"Skipping {file}, not found")
        continue

    with open(file, 'r', encoding='utf-8') as f:
        content = f.read()

    # 1. HeaderBadgeFrame
    header_old = '''<Frame HeightRequest="16" MinimumWidthRequest="16" CornerRadius="8" BackgroundColor="#E11D48" HasShadow="False" Padding="0" HorizontalOptions="End" VerticalOptions="Start" Margin="0,-6,-6,0">
                            <Label Text="3" FontSize="10" TextColor="White" HorizontalOptions="Center" VerticalOptions="Center" />
                        </Frame>'''
    header_new = '''<Frame x:Name="HeaderBadgeFrame" HeightRequest="16" MinimumWidthRequest="16" CornerRadius="8" BackgroundColor="#E11D48" HasShadow="False" Padding="0" HorizontalOptions="End" VerticalOptions="Start" Margin="0,-6,-6,0">
                            <Label x:Name="HeaderBadgeLabel" Text="3" FontSize="10" TextColor="White" HorizontalOptions="Center" VerticalOptions="Center" />
                        </Frame>'''
    content = content.replace(header_old, header_new)

    # 2. OverlayBadgeFrame
    overlay_old = '''<Frame Grid.Column="2" BackgroundColor="#EF4444" CornerRadius="14" HasShadow="False" Padding="10,4" HorizontalOptions="Start" VerticalOptions="Center">
                                <Label Text="3 new" TextColor="White" FontSize="12" FontAttributes="Bold" />
                            </Frame>'''
    overlay_new = '''<Frame x:Name="OverlayBadgeFrame" Grid.Column="2" BackgroundColor="#EF4444" CornerRadius="14" HasShadow="False" Padding="10,4" HorizontalOptions="Start" VerticalOptions="Center">
                                <Label x:Name="OverlayBadgeLabel" Text="3 new" TextColor="White" FontSize="12" FontAttributes="Bold" />
                            </Frame>'''
    content = content.replace(overlay_old, overlay_new)

    # 3. Replace the static notifications list
    # Use regex to find <ScrollView Grid.Row="1" HeightRequest="420"> down to </ScrollView>
    pattern = re.compile(r'<ScrollView Grid\.Row="1" HeightRequest="420">.*?</ScrollView>', re.DOTALL)
    replacement = '''<ScrollView Grid.Row="1" HeightRequest="420">
                            <VerticalStackLayout x:Name="NotificationsList" Spacing="0">
                                <!-- Dynamic notifications will be populated from code-behind -->
                            </VerticalStackLayout>
                        </ScrollView>'''
    content = pattern.sub(replacement, content)

    with open(file, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"Updated XAML for {file}")
