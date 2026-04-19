using AgriScholarApp.Services;
using Microsoft.Maui.Storage;

namespace AgriScholarApp.ScholarPage;

[QueryProperty(nameof(From), "from")]
public partial class UploadDocumentPage : ContentPage
{
    private const string ScholarEmailKey = "scholar_email";
    private const string FirebaseIdTokenKey = "firebase_id_token";
    private const string FirebaseUidKey = "firebase_uid";

    private readonly SupabaseStorageRestService _storage = new();
    private readonly FirestoreRestService _firestore = new();

    private FileResult? _selectedFile;

    public string? From { get; set; }

    public UploadDocumentPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
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

    private async void OnChooseFileTapped(object sender, TappedEventArgs e)
    {
        HideError();

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Document",
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

        if (DocumentTypePicker.SelectedIndex == -1)
        {
            ShowError("Please select a document type category.");
            return;
        }

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
            var storagePath = $"documents/{uid}/{DateTime.UtcNow:yyyyMMddHHmmss}_{docId}{safeExt}";

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

            var documentType = DocumentTypePicker.Items[DocumentTypePicker.SelectedIndex];

            var data = new Dictionary<string, object?>
            {
                ["docId"] = docId,
                ["type"] = "document",
                ["documentCategory"] = documentType,
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

            Preferences.Default.Set("last_document_submitted", true);
            Preferences.Default.Set("last_submission_name", _selectedFile.FileName);
            Preferences.Default.Set("last_submission_url", upload.PublicUrl);

            await DisplayAlert("Success", "Document submitted successfully.", "OK");
            await GoBackAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Upload failed: {ex.Message}");
            SubmitButton.IsEnabled = true;
            SubmitButton.Text = "Submit Document";
        }
    }
}
