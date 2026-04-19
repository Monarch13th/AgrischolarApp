using AgriScholarApp.Services;

namespace AgriScholarApp.ScholarPage;

public partial class ScholarLoginPage : ContentPage
{
    private const string ScholarRememberMeKey = "scholar_remember_me";
    private const string ScholarEmailKey = "scholar_email";
    private const string AdminEmail = "admin@gmail.com";

    public ScholarLoginPage()
    {
        InitializeComponent();

        RememberMeCheckBox.IsChecked = Preferences.Default.Get(ScholarRememberMeKey, false);
        if (RememberMeCheckBox.IsChecked)
        {
            EmailEntry.Text = Preferences.Default.Get(ScholarEmailKey, string.Empty);
        }
    }

    private async void OnScholarLoginClicked(object sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ErrorLabel.Text = "Please enter email and password.";
            ErrorLabel.IsVisible = true;
            return;
        }

        try
        {
            var auth = new FirebaseAuthRestService();
            var signIn = await auth.SignInAsync(email, password);

            Preferences.Default.Set(ScholarRememberMeKey, RememberMeCheckBox.IsChecked);
            Preferences.Default.Set(ScholarEmailKey, RememberMeCheckBox.IsChecked ? email : string.Empty);

            if (string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                Preferences.Default.Set("is_logged_in", true);
                await Shell.Current.GoToAsync("//AdminDashboardPage");
                return;
            }

            Preferences.Default.Set(ScholarEmailKey, email);
            Preferences.Default.Set("firebase_id_token", signIn.IdToken);
            Preferences.Default.Set("firebase_uid", signIn.Uid);
            await Shell.Current.GoToAsync("//ScholarHomePage");
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = ex.Message;
            ErrorLabel.IsVisible = true;
        }
    }

    private async void OnForgotPasswordClicked(object sender, EventArgs e)
    {
        try
        {
            var preset = EmailEntry.Text?.Trim() ?? Preferences.Default.Get(ScholarEmailKey, string.Empty);
            var email = await DisplayPromptAsync("Forgot Password", "Enter your email to receive a reset code/link:",
                accept: "Send", cancel: "Cancel", placeholder: "name@email.com", initialValue: preset, keyboard: Keyboard.Email);

            email = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            var auth = new FirebaseAuthRestService();
            await auth.SendPasswordResetEmailAsync(email);

            await DisplayAlert("Forgot Password", "We sent a password reset message to your email. Please check your inbox (and spam).", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Forgot Password", ex.Message, "OK");
        }
    }
}
