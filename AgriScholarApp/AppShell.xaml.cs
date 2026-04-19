using AgriScholarApp.Services;

namespace AgriScholarApp
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object? sender, EventArgs e)
        {
            Loaded -= OnLoaded;

            await GoToAsync("//ScholarLoginPage");
        }
    }
}
