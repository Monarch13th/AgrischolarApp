using AgriScholarApp.Services;
using Microsoft.Maui.Storage;

namespace AgriScholarApp.ScholarPage;

[QueryProperty(nameof(SubmissionType), "type")]
[QueryProperty(nameof(From), "from")]
public partial class UploadGradeReportPage : ContentPage
{
    private const string ScholarEmailKey = "scholar_email";
    private const string FirebaseIdTokenKey = "firebase_id_token";
    private const string FirebaseUidKey = "firebase_uid";

    private readonly SupabaseStorageRestService _storage = new();
    private readonly FirestoreRestService _firestore = new();

    private FileResult? _selectedFile;

    public string? SubmissionType { get; set; }

    public string? From { get; set; }

    public UploadGradeReportPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SubmitButton.Text = GetSubmitButtonText();
    }

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        await GoBackAsync();
    }

    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        await GoBackAsync();
    }

    private async Task GoBackAsync()
    {
        var from = (From ?? string.Empty).Trim().ToLowerInvariant();
        var route = from switch
        {
            "home" => "//ScholarHomePage",
            "records" => "//AcademicRecordsPage",
            "documents" => "//DocumentsPage",
            "profile" => "//ProfilePage",
            _ => "//DocumentsPage"
        };

        await Shell.Current.GoToAsync(route);
    }

    private string GetSubmitButtonText()
    {
        var t = (SubmissionType ?? string.Empty).Trim().ToLowerInvariant();
        return t switch
        {
            "grade_1st_sem" => "Submit 1st Semester Grade",
            "grade_2nd_sem" => "Submit 2nd Semester Grade",
            _ => "Submit Grade Report"
        };
    }

    private async void OnChooseFileTapped(object sender, TappedEventArgs e)
    {
        HideError();

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Grade Report",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/pdf", "image/*" } },
                    { DevicePlatform.iOS, new[] { "com.adobe.pdf", "public.image" } },
                    { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf", "public.image" } },
                    { DevicePlatform.WinUI, new[] { ".pdf", ".png", ".jpg", ".jpeg" } }
                })
            });

            if (file is null)
                return;

            _selectedFile = file;
            SelectedFileLabel.Text = file.FileName;
            FileSizeLabel.Text = "Tap to change file";

            FileInfoFrame.IsVisible = true;
            NoFileFrame.IsVisible = false;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorFrame.IsVisible = true;
    }

    private void HideError()
    {
        ErrorFrame.IsVisible = false;
    }

    private async void OnSubmitClicked(object sender, EventArgs e)
    {
        HideError();

        if (_selectedFile is null)
        {
            ShowError("Please choose a file before submitting.");
            return;
        }

        var idToken = Preferences.Default.Get(FirebaseIdTokenKey, string.Empty);
        var uid = Preferences.Default.Get(FirebaseUidKey, string.Empty);
        var email = Preferences.Default.Get(ScholarEmailKey, string.Empty);

        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(uid))
        {
            ErrorLabel.Text = "Session expired. Please login again.";
            ErrorLabel.IsVisible = true;
            return;
        }

        try
        {
            SubmitButton.IsEnabled = false;
            SubmitButton.Text = "Submitting…";

            var docId = Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(_selectedFile.FileName);
            var safeExt = string.IsNullOrWhiteSpace(ext) ? string.Empty : ext;
            var storagePath = $"gradeReports/{uid}/{DateTime.UtcNow:yyyyMMddHHmmss}_{docId}{safeExt}";

            await using var stream = await _selectedFile.OpenReadAsync();

            var contentType = _selectedFile.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/octet-stream";
            }

            var upload = await _storage.UploadAsync(FirebaseConfig.SupabaseGradeReportBucket, storagePath, stream, contentType);

            var parentDoc = new Dictionary<string, object?>
            {
                ["uid"] = uid,
                ["email"] = email,
                ["updatedAt"] = DateTime.UtcNow
            };

            try
            {
                await _firestore.UpdateDocumentAsync($"documents/{uid}", idToken, parentDoc);
            }
            catch
            {
                // If the parent doc doesn't exist yet, create it.
                await _firestore.CreateDocumentAsync("documents", uid, idToken, parentDoc);
            }

            var data = new Dictionary<string, object?>
            {
                ["docId"] = docId,
                ["type"] = string.IsNullOrWhiteSpace(SubmissionType) ? "grade_report" : SubmissionType,
                ["uid"] = uid,
                ["email"] = email,
                ["fileName"] = _selectedFile.FileName,
                ["contentType"] = contentType,
                ["storageProvider"] = "supabase",
                ["storageBucket"] = upload.Bucket,
                ["storagePath"] = upload.ObjectPath,
                ["downloadUrl"] = upload.PublicUrl,
                ["status"] = "pending",
                ["notes"] = NotesEditor.Text,
                ["createdAt"] = DateTime.UtcNow
            };

            await _firestore.CreateDocumentAsync($"documents/{uid}/submissions", docId, idToken, data);

            Preferences.Default.Set("grade_report_submitted", true);
            Preferences.Default.Set("last_submission_name", _selectedFile.FileName);
            Preferences.Default.Set("last_submission_url", upload.PublicUrl);

            await DisplayAlert("Success", "Grade report submitted.", "OK");
            await GoBackAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SubmitButton.IsEnabled = true;
            SubmitButton.Text = GetSubmitButtonText();
        }
    }
}
