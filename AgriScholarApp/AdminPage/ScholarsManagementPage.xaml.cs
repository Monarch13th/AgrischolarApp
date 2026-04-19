using AgriScholarApp.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgriScholarApp.Pages;

public partial class ScholarsManagementPage : ContentPage
{
    private const string FirebaseAdminTokenKey = "firebase_admin_id_token";
    public ObservableCollection<ScholarRecipient> Scholars { get; } = new();
    private readonly FirestoreRestService _firestore = new();
    private readonly FirebaseAuthRestService _auth = new();
    private ScholarRecipient? _editingScholar;

    private bool _isEditMode;
    private readonly List<ScholarRecipient> _allScholars = new();

    private bool _isGenderDropdownOpen;
    public bool IsGenderDropdownOpen
    {
        get => _isGenderDropdownOpen;
        set
        {
            if (_isGenderDropdownOpen == value) return;
            _isGenderDropdownOpen = value;
            OnPropertyChanged(nameof(IsGenderDropdownOpen));
        }
    }

    private bool _isYearDropdownOpen;
    public bool IsYearDropdownOpen
    {
        get => _isYearDropdownOpen;
        set
        {
            if (_isYearDropdownOpen == value) return;
            _isYearDropdownOpen = value;
            OnPropertyChanged(nameof(IsYearDropdownOpen));
        }
    }

    private bool _isDegreeProgramDropdownOpen;
    public bool IsDegreeProgramDropdownOpen
    {
        get => _isDegreeProgramDropdownOpen;
        set
        {
            if (_isDegreeProgramDropdownOpen == value) return;
            _isDegreeProgramDropdownOpen = value;
            OnPropertyChanged(nameof(IsDegreeProgramDropdownOpen));
        }
    }

    private bool _isScholarshipTypeDropdownOpen;
    public bool IsScholarshipTypeDropdownOpen
    {
        get => _isScholarshipTypeDropdownOpen;
        set
        {
            if (_isScholarshipTypeDropdownOpen == value) return;
            _isScholarshipTypeDropdownOpen = value;
            OnPropertyChanged(nameof(IsScholarshipTypeDropdownOpen));
        }
    }

    private bool _isInitialStatusDropdownOpen;
    public bool IsInitialStatusDropdownOpen
    {
        get => _isInitialStatusDropdownOpen;
        set
        {
            if (_isInitialStatusDropdownOpen == value) return;
            _isInitialStatusDropdownOpen = value;
            OnPropertyChanged(nameof(IsInitialStatusDropdownOpen));
        }
    }

    public ScholarsManagementPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    private async Task OnUpdateScholarAsync()
    {
        if (_editingScholar is null)
        {
            AddScholarErrorLabel.Text = "No scholar selected for editing.";
            AddScholarErrorLabel.IsVisible = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(FirstNameEntry.Text) ||
            string.IsNullOrWhiteSpace(LastNameEntry.Text) ||
            string.IsNullOrWhiteSpace(SchoolEntry.Text) ||
            DegreeProgramPicker.SelectedIndex < 0 ||
            string.IsNullOrWhiteSpace(YearLevelEntry.Text) ||
            string.IsNullOrWhiteSpace(GpaEntry.Text))
        {
            AddScholarErrorLabel.Text = "Please fill out required fields: First Name, Last Name, University/School, Degree Program, Year Level, Current GPA.";
            AddScholarErrorLabel.IsVisible = true;
            return;
        }

        if (!double.TryParse(GpaEntry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var gpa))
        {
            AddScholarErrorLabel.Text = "Current GPA must be a number.";
            AddScholarErrorLabel.IsVisible = true;
            return;
        }

        try
        {
            var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            await EnsureAdminTokenAndLoadAsync(token);
            token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);

            var firestore = new FirestoreRestService();
            var updateData = new Dictionary<string, object?>
            {
                ["studentId"] = IdNumberEntry.Text?.Trim(),
                ["firstName"] = FirstNameEntry.Text?.Trim(),
                ["middleName"] = MiddleNameEntry.Text?.Trim(),
                ["lastName"] = LastNameEntry.Text?.Trim(),
                ["email"] = EmailEntry.Text?.Trim(),
                ["contactNumber"] = ContactNumberEntry.Text?.Trim(),
                ["homeAddress"] = HomeAddressEntry.Text?.Trim(),
                ["gender"] = string.IsNullOrWhiteSpace(GenderEntry.Text) ? null : GenderEntry.Text.Trim(),
                ["school"] = SchoolEntry.Text?.Trim(),
                ["degreeProgram"] = DegreeProgramPicker.SelectedIndex >= 0 ? DegreeProgramPicker.Items[DegreeProgramPicker.SelectedIndex] : null,
                ["yearLevel"] = YearLevelEntry.Text?.Trim(),
                ["gpa"] = gpa,
                ["scholarshipType"] = ScholarshipTypePicker.SelectedIndex >= 0 ? ScholarshipTypePicker.Items[ScholarshipTypePicker.SelectedIndex] : null,
                ["initialStatus"] = InitialStatusPicker.SelectedIndex >= 0 ? InitialStatusPicker.Items[InitialStatusPicker.SelectedIndex] : null,
                ["additionalNotes"] = AdditionalNotesEditor.Text
            };

            await firestore.UpdateScholarAsync(_editingScholar.ScholarId, token, updateData);

            await DisplayAlert("Success", "Scholar updated successfully.", "OK");

            await LoadScholarsAsync(token);

            _isEditMode = false;
            _editingScholar = null;

            AddScholarModal.IsVisible = false;
            AddScholarModalContent.IsVisible = false;
        }
        catch (Exception ex)
        {
            AddScholarErrorLabel.Text = ex.Message;
            AddScholarErrorLabel.IsVisible = true;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            await EnsureAdminTokenAndLoadAsync(token);
        }
        catch (Exception ex)
        {
            AddScholarErrorLabel.Text = ex.Message;
            AddScholarErrorLabel.IsVisible = true;

            await DisplayAlert("Load Scholars Failed", ex.Message, "OK");
        }
    }

    private async Task EnsureAdminTokenAndLoadAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            token = await SignInAdminAndCacheAsync();
        }

        try
        {
            await LoadScholarsAsync(token);
        }
        catch
        {
            // Token might be expired/invalid. Clear and retry once.
            Preferences.Default.Remove(FirebaseAdminTokenKey);
            token = await SignInAdminAndCacheAsync();
            await LoadScholarsAsync(token);
        }
    }

    private static async Task<string> SignInAdminAndCacheAsync()
    {
        var auth = new FirebaseAuthRestService();
        var signIn = await auth.SignInAsync("admin@gmail.com", "admin123");
        Preferences.Default.Set(FirebaseAdminTokenKey, signIn.IdToken);
        return signIn.IdToken;
    }

    private async Task LoadScholarsAsync(string idToken)
    {
        var firestore = new FirestoreRestService();
        var docs = await firestore.GetScholarsAsync(idToken);

        _allScholars.Clear();

        foreach (var d in docs)
        {
            var firstName = GetString(d, "firstName") ?? string.Empty;
            var middleName = GetString(d, "middleName") ?? string.Empty;
            var lastName = GetString(d, "lastName") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
            {
                var full = GetString(d, "fullName") ?? GetString(d, "name") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(full))
                {
                    var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1)
                    {
                        firstName = parts[0];
                    }
                    else if (parts.Length > 1)
                    {
                        firstName = parts[0];
                        lastName = parts[^1];
                        if (parts.Length > 2)
                        {
                            middleName = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));
                        }
                    }
                }
            }

            _allScholars.Add(new ScholarRecipient
            {
                ScholarId = GetString(d, "scholarId") ?? GetString(d, "uid") ?? string.Empty,
                StudentId = GetString(d, "studentId") ?? GetString(d, "scholarId") ?? GetString(d, "id") ?? string.Empty,
                FirstName = firstName,
                MiddleName = middleName,
                LastName = lastName,
                Email = GetString(d, "email") ?? string.Empty,
                ContactNumber = GetString(d, "contactNumber") ?? string.Empty,
                HomeAddress = GetString(d, "homeAddress") ?? string.Empty,
                School = GetString(d, "school") ?? GetString(d, "university") ?? string.Empty,
                DegreeProgram = GetString(d, "degreeProgram") ?? string.Empty,
                YearLevel = GetString(d, "yearLevel") ?? string.Empty,
                ScholarshipType = GetString(d, "scholarshipType") ?? string.Empty,
                InitialStatus = GetString(d, "initialStatus") ?? string.Empty,
                Gpa = GetDouble(d, "gpa")
            });
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var search = SearchEntry?.Text?.Trim() ?? string.Empty;
        var selectedStatus = StatusFilterPicker?.SelectedIndex >= 0
            ? StatusFilterPicker.Items[StatusFilterPicker.SelectedIndex]
            : "All Status";

        IEnumerable<ScholarRecipient> query = _allScholars;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s =>
                (!string.IsNullOrWhiteSpace(s.FullName) && s.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(s.ScholarId) && s.ScholarId.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(selectedStatus) && selectedStatus != "All Status")
        {
            query = query.Where(s => string.Equals(s.InitialStatus, selectedStatus, StringComparison.OrdinalIgnoreCase));
        }

        Scholars.Clear();
        foreach (var s in query)
        {
            Scholars.Add(s);
        }

        AllScholarsCountLabel.Text = $"All Scholars ({Scholars.Count})";
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val) || val is null) return null;
        return val.ToString();
    }

    private static double? GetDouble(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val) || val is null) return null;
        if (val is double d) return d;
        if (val is float f) return f;
        if (val is long l) return l;
        if (double.TryParse(val.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void OnStatusFilterChanged(object sender, EventArgs e)
    {
        ApplyFilters();
    }

    private async void OnViewDetailsClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not ScholarRecipient s)
        {
            return;
        }

        OpenViewScholarModal(s);
    }

    private async void OnScholarMoreOptionsClicked(object sender, EventArgs e)
    {
        ScholarRecipient? s = null;
        if (sender is BindableObject bo)
        {
            s = bo.BindingContext as ScholarRecipient;
        }

        if (s is null)
        {
            return;
        }

        foreach (var row in Scholars)
        {
            if (!ReferenceEquals(row, s) && row.IsOptionsOpen)
            {
                row.IsOptionsOpen = false;
            }
        }

        s.IsOptionsOpen = !s.IsOptionsOpen;
    }

    private void OnScholarEditMenuClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not ScholarRecipient s)
        {
            return;
        }

        s.IsOptionsOpen = false;
        OpenEditScholarModal(s);
    }

    private async void OnScholarDeleteMenuClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not ScholarRecipient s)
        {
            return;
        }

        s.IsOptionsOpen = false;

        var confirm = await DisplayAlert("Delete Scholar", $"Delete {s.FullName}?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        try
        {
            var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            await EnsureAdminTokenAndLoadAsync(token);
            token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);

            if (string.IsNullOrWhiteSpace(s.ScholarId))
            {
                await DisplayAlert("Delete", "Scholar ID is missing.", "OK");
                return;
            }

            await _firestore.DeleteDocumentAsync($"scholars/{s.ScholarId}", token);

            _allScholars.RemoveAll(x => string.Equals(x.ScholarId, s.ScholarId, StringComparison.Ordinal));
            var toRemove = Scholars.FirstOrDefault(x => string.Equals(x.ScholarId, s.ScholarId, StringComparison.Ordinal));
            if (toRemove is not null)
            {
                Scholars.Remove(toRemove);
            }
            AllScholarsCountLabel.Text = $"All Scholars ({Scholars.Count})";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Delete Failed", ex.Message, "OK");
        }
    }

    private void OpenViewScholarModal(ScholarRecipient scholar)
    {
        _isEditMode = false;
        _editingScholar = null;

        ScholarModalTitleLabel.Text = string.IsNullOrWhiteSpace(scholar.FullName) ? "Scholar Details" : scholar.FullName;
        ScholarModalSubtitleLabel.Text = "View the scholar's details below.";
        ScholarModalPrimaryButton.IsVisible = false;
        EditSaveButton.IsVisible = false;
        PasswordEntry.Text = string.Empty;
        PasswordEntry.IsEnabled = false;

        FirstNameEntry.Text = scholar.FirstName;
        MiddleNameEntry.Text = scholar.MiddleName;
        LastNameEntry.Text = scholar.LastName;
        EmailEntry.Text = scholar.Email;
        ContactNumberEntry.Text = scholar.ContactNumber;
        HomeAddressEntry.Text = scholar.HomeAddress;
        IdNumberEntry.Text = scholar.StudentId;
        GenderEntry.Text = scholar.Gender;
        SchoolEntry.Text = scholar.School;

        if (!string.IsNullOrWhiteSpace(scholar.DegreeProgram))
        {
            DegreeProgramPicker.SelectedIndex = DegreeProgramPicker.Items.IndexOf(scholar.DegreeProgram);
        }

        DegreeProgramEntry.Text = scholar.DegreeProgram;

        YearLevelEntry.Text = scholar.YearLevel;

        if (!string.IsNullOrWhiteSpace(scholar.ScholarshipType))
        {
            ScholarshipTypePicker.SelectedIndex = ScholarshipTypePicker.Items.IndexOf(scholar.ScholarshipType);
        }

        ScholarshipTypeEntry.Text = scholar.ScholarshipType;

        if (!string.IsNullOrWhiteSpace(scholar.InitialStatus))
        {
            InitialStatusPicker.SelectedIndex = InitialStatusPicker.Items.IndexOf(scholar.InitialStatus);
        }

        InitialStatusEntry.Text = scholar.InitialStatus;

        if (scholar.Gpa.HasValue)
        {
            GpaEntry.Text = scholar.Gpa.Value.ToString(CultureInfo.InvariantCulture);
        }

        SetScholarFormEnabled(false);

        AddScholarErrorLabel.IsVisible = false;
        AddScholarModal.IsVisible = true;
        AddScholarModalContent.IsVisible = true;
        ShowTab("personal");

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(scholar.ScholarId))
                {
                    await LoadScholarIntoFormAsync(scholar.ScholarId);
                    SetScholarFormEnabled(false);
                }
            }
            catch
            {
                // Ignore load errors in view mode; best-effort only.
            }
        });
    }

    private void SetScholarFormEnabled(bool enabled)
    {
        FirstNameEntry.IsEnabled    = enabled;
        MiddleNameEntry.IsEnabled   = enabled;
        LastNameEntry.IsEnabled     = enabled;
        EmailEntry.IsEnabled        = enabled;
        ContactNumberEntry.IsEnabled = enabled;
        HomeAddressEntry.IsEnabled  = enabled;
        GenderEntry.IsEnabled       = enabled;   // hidden entry; drives chip IsEnabled logic
        DateOfBirthPicker.IsEnabled = enabled;
        SchoolEntry.IsEnabled       = enabled;
        DegreeProgramEntry.IsEnabled = enabled;
        DegreeProgramPicker.IsEnabled = enabled;
        YearLevelEntry.IsEnabled    = enabled;   // hidden entry; drives chip IsEnabled logic
        GpaEntry.IsEnabled          = enabled;
        ScholarshipTypeEntry.IsEnabled = enabled;
        ScholarshipTypePicker.IsEnabled = enabled;
        InitialStatusEntry.IsEnabled = enabled;
        InitialStatusPicker.IsEnabled   = enabled;
        AdditionalNotesEditor.IsEnabled = enabled;
    }


    private async void OnExportClicked(object sender, EventArgs e)
    {
        if (Scholars.Count == 0)
        {
            await DisplayAlert("Export", "No scholars to export.", "OK");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("ScholarId,FullName,DegreeProgram,YearLevel,InitialStatus,ScholarshipType,Gpa");

        foreach (var s in Scholars)
        {
            sb.AppendLine(string.Join(",",
                Csv(s.ScholarId),
                Csv(s.FullName),
                Csv(s.DegreeProgram),
                Csv(s.YearLevel),
                Csv(s.InitialStatus),
                Csv(s.ScholarshipType),
                Csv(s.GpaDisplay)));
        }

        var fileName = $"scholars_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export Scholars",
            File = new ShareFile(path)
        });
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }


    private void OnAddNewScholarClicked(object sender, EventArgs e)
    {
        _isEditMode = false;
        _editingScholar = null;

        ScholarModalTitleLabel.Text = "Add New Scholar";
        ScholarModalSubtitleLabel.Text = "Fill in the scholar's details below. All required fields must be completed before saving.";
        ScholarModalPrimaryButton.Text = "＋  Add Scholar";
        ScholarModalPrimaryButton.IsVisible = true;
        EditSaveButton.IsVisible = false;
        PasswordEntry.IsEnabled = true;
        SetScholarFormEnabled(true);

        AddScholarErrorLabel.IsVisible = false;
        AddScholarModal.IsVisible = true;
        AddScholarModalContent.IsVisible = true;
        GenderEntry.Text = string.Empty;
        YearLevelEntry.Text = string.Empty;
        DegreeProgramEntry.Text = string.Empty;
        ScholarshipTypeEntry.Text = string.Empty;
        InitialStatusEntry.Text = string.Empty;

        IsGenderDropdownOpen = false;
        IsYearDropdownOpen = false;
        IsDegreeProgramDropdownOpen = false;
        IsScholarshipTypeDropdownOpen = false;
        IsInitialStatusDropdownOpen = false;
        ShowTab("personal");
    }

    private void OpenEditScholarModal(ScholarRecipient scholar)
    {
        _isEditMode = true;
        _editingScholar = scholar;

        ScholarModalTitleLabel.Text = string.IsNullOrWhiteSpace(scholar.FullName) ? "Edit Scholar" : $"Edit {scholar.FullName}";
        ScholarModalSubtitleLabel.Text = "Update the scholar's details below. All required fields must be completed before saving.";
        ScholarModalPrimaryButton.Text = "Update Scholar";
        ScholarModalPrimaryButton.IsVisible = false;
        EditSaveButton.IsVisible = true;
        PasswordEntry.Text = string.Empty;
        PasswordEntry.IsEnabled = false;
        SetScholarFormEnabled(true);

        FirstNameEntry.Text = scholar.FirstName;
        MiddleNameEntry.Text = scholar.MiddleName;
        LastNameEntry.Text = scholar.LastName;

        EmailEntry.Text = scholar.Email;
        ContactNumberEntry.Text = scholar.ContactNumber;
        HomeAddressEntry.Text = scholar.HomeAddress;
        IdNumberEntry.Text = scholar.StudentId;
        GenderEntry.Text = scholar.Gender;
        SchoolEntry.Text = scholar.School;

        if (!string.IsNullOrWhiteSpace(scholar.DegreeProgram))
        {
            DegreeProgramPicker.SelectedIndex = DegreeProgramPicker.Items.IndexOf(scholar.DegreeProgram);
        }

        DegreeProgramEntry.Text = scholar.DegreeProgram;

        YearLevelEntry.Text = scholar.YearLevel;

        if (!string.IsNullOrWhiteSpace(scholar.ScholarshipType))
        {
            ScholarshipTypePicker.SelectedIndex = ScholarshipTypePicker.Items.IndexOf(scholar.ScholarshipType);
        }

        if (!string.IsNullOrWhiteSpace(scholar.InitialStatus))
        {
            InitialStatusPicker.SelectedIndex = InitialStatusPicker.Items.IndexOf(scholar.InitialStatus);
        }

        if (scholar.Gpa.HasValue)
        {
            GpaEntry.Text = scholar.Gpa.Value.ToString(CultureInfo.InvariantCulture);
        }

        AddScholarErrorLabel.IsVisible = false;
        AddScholarModal.IsVisible = true;
        AddScholarModalContent.IsVisible = true;

        ShowTab("personal");

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(scholar.ScholarId))
                {
                    await LoadScholarIntoFormAsync(scholar.ScholarId);
                }
            }
            catch (Exception ex)
            {
                AddScholarErrorLabel.Text = ex.Message;
                AddScholarErrorLabel.IsVisible = true;
            }
        });
    }

    private async Task LoadScholarIntoFormAsync(string uid)
    {
        var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
        await EnsureAdminTokenAndLoadAsync(token);
        token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);

        var firestore = new FirestoreRestService();
        var doc = await firestore.GetDocumentAsync($"scholars/{uid}", token);

        static string? GetStringLocal(Dictionary<string, object?> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (dict.TryGetValue(k, out var v) && v is not null)
                {
                    var s = v.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }

            return null;
        }

        static double? GetDoubleLocal(Dictionary<string, object?> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                var d = GetDouble(dict, k);
                if (d.HasValue) return d;
            }

            return null;
        }

        DateTime? GetDate(Dictionary<string, object?> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (!dict.TryGetValue(k, out var v) || v is null) continue;
                if (v is DateTime dt) return dt;
                if (v is DateTimeOffset dto) return dto.DateTime;
                if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
            }

            return null;
        }

        FirstNameEntry.Text = GetStringLocal(doc, "firstName") ?? string.Empty;
        MiddleNameEntry.Text = GetStringLocal(doc, "middleName") ?? string.Empty;
        LastNameEntry.Text = GetStringLocal(doc, "lastName") ?? string.Empty;
        EmailEntry.Text = GetStringLocal(doc, "email") ?? string.Empty;
        ContactNumberEntry.Text = GetStringLocal(doc, "contactNumber") ?? string.Empty;
        HomeAddressEntry.Text = GetStringLocal(doc, "homeAddress") ?? string.Empty;
        IdNumberEntry.Text = GetStringLocal(doc, "studentId", "scholarId", "id") ?? string.Empty;
        GenderEntry.Text = GetStringLocal(doc, "gender", "sex") ?? string.Empty;
        SchoolEntry.Text = GetStringLocal(doc, "school", "university") ?? string.Empty;

        var dob = GetDate(doc, "dateOfBirth", "dob", "birthDate");
        if (dob.HasValue)
        {
            DateOfBirthPicker.Date = dob.Value.Date;
        }

        var program = GetStringLocal(doc, "degreeProgram") ?? string.Empty;
        DegreeProgramEntry.Text = program;
        DegreeProgramPicker.SelectedIndex = DegreeProgramPicker.Items.IndexOf(program);

        var year = GetStringLocal(doc, "yearLevel") ?? string.Empty;
        YearLevelEntry.Text = year;

        var gpa = GetDoubleLocal(doc, "gpa");
        if (gpa.HasValue)
        {
            GpaEntry.Text = gpa.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            GpaEntry.Text = string.Empty;
        }

        var scholarshipType = GetStringLocal(doc, "scholarshipType") ?? string.Empty;
        ScholarshipTypeEntry.Text = scholarshipType;
        ScholarshipTypePicker.SelectedIndex = ScholarshipTypePicker.Items.IndexOf(scholarshipType);

        var initialStatus = GetStringLocal(doc, "initialStatus") ?? string.Empty;
        InitialStatusEntry.Text = initialStatus;
        InitialStatusPicker.SelectedIndex = InitialStatusPicker.Items.IndexOf(initialStatus);

        AdditionalNotesEditor.Text = GetStringLocal(doc, "additionalNotes") ?? string.Empty;
    }

    private void OnCancelAddScholarClicked(object sender, EventArgs e)
    {
        AddScholarErrorLabel.IsVisible = false;
        AddScholarModal.IsVisible = false;
        AddScholarModalContent.IsVisible = false;

        IsGenderDropdownOpen = false;
        IsYearDropdownOpen = false;
        IsDegreeProgramDropdownOpen = false;
        IsScholarshipTypeDropdownOpen = false;
        IsInitialStatusDropdownOpen = false;
    }

    private void OnCloseAddScholarModal(object sender, EventArgs e)
    {
        OnCancelAddScholarClicked(sender, e);
    }

    private void OnPersonalTabTapped(object sender, EventArgs e)
    {
        ShowTab("personal");
    }

    private void OnAcademicTabTapped(object sender, EventArgs e)
    {
        ShowTab("academic");
    }

    private void OnScholarshipTabTapped(object sender, EventArgs e)
    {
        ShowTab("scholarship");
    }

    private void ShowTab(string tab)
    {
        PersonalInfoSection.IsVisible = tab == "personal";
        AcademicInfoSection.IsVisible = tab == "academic";
        ScholarshipSection.IsVisible = tab == "scholarship";

        PersonalTab.BackgroundColor = tab == "personal" ? Colors.White : Colors.Transparent;
        AcademicTab.BackgroundColor = tab == "academic" ? Colors.White : Colors.Transparent;
        ScholarshipTab.BackgroundColor = tab == "scholarship" ? Colors.White : Colors.Transparent;
    }

    private void OnDegreeProgramArrowTapped(object sender, EventArgs e)
    {
        DegreeProgramPicker.Focus();
    }

    private void OnDegreeProgramTapped(object sender, EventArgs e)
    {
        if (!DegreeProgramEntry.IsEnabled)
        {
            return;
        }

        IsGenderDropdownOpen = false;
        IsYearDropdownOpen = false;
        IsScholarshipTypeDropdownOpen = false;
        IsInitialStatusDropdownOpen = false;
        IsDegreeProgramDropdownOpen = !IsDegreeProgramDropdownOpen;
    }

    private void OnDegreeProgramOptionClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not string value || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        DegreeProgramEntry.Text = value;
        DegreeProgramPicker.SelectedIndex = DegreeProgramPicker.Items.IndexOf(value);
        IsDegreeProgramDropdownOpen = false;
    }

    private void OnScholarshipTypeArrowTapped(object sender, EventArgs e)
    {
        ScholarshipTypePicker.Focus();
    }

    private void OnScholarshipTypeTapped(object sender, EventArgs e)
    {
        if (!ScholarshipTypeEntry.IsEnabled)
        {
            return;
        }

        IsGenderDropdownOpen = false;
        IsYearDropdownOpen = false;
        IsDegreeProgramDropdownOpen = false;
        IsInitialStatusDropdownOpen = false;
        IsScholarshipTypeDropdownOpen = !IsScholarshipTypeDropdownOpen;
    }

    private void OnScholarshipTypeOptionClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not string value || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ScholarshipTypeEntry.Text = value;
        ScholarshipTypePicker.SelectedIndex = ScholarshipTypePicker.Items.IndexOf(value);
        IsScholarshipTypeDropdownOpen = false;
    }

    private void OnGenderTapped(object sender, EventArgs e)
    {
        if (!GenderEntry.IsEnabled)
        {
            return;
        }

        IsYearDropdownOpen = false;
        IsDegreeProgramDropdownOpen = false;
        IsScholarshipTypeDropdownOpen = false;
        IsInitialStatusDropdownOpen = false;
        IsGenderDropdownOpen = !IsGenderDropdownOpen;
    }

    private void OnYearLevelTapped(object sender, EventArgs e)
    {
        if (!YearLevelEntry.IsEnabled)
        {
            return;
        }

        IsGenderDropdownOpen = false;
        IsDegreeProgramDropdownOpen = false;
        IsScholarshipTypeDropdownOpen = false;
        IsInitialStatusDropdownOpen = false;
        IsYearDropdownOpen = !IsYearDropdownOpen;
    }

    private void OnGenderMaleOptionClicked(object sender, EventArgs e)
    {
        GenderEntry.Text = "Male";
        IsGenderDropdownOpen = false;
    }

    private void OnGenderFemaleOptionClicked(object sender, EventArgs e)
    {
        GenderEntry.Text = "Female";
        IsGenderDropdownOpen = false;
    }

    private void OnYearOptionClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not string value)
        {
            return;
        }

        YearLevelEntry.Text = value;
        IsYearDropdownOpen = false;
    }

    private void OnInitialStatusArrowTapped(object sender, EventArgs e)
    {
        InitialStatusPicker.Focus();
    }

    private void OnInitialStatusTapped(object sender, EventArgs e)
    {
        if (!InitialStatusEntry.IsEnabled)
        {
            return;
        }

        IsGenderDropdownOpen = false;
        IsYearDropdownOpen = false;
        IsDegreeProgramDropdownOpen = false;
        IsScholarshipTypeDropdownOpen = false;
        IsInitialStatusDropdownOpen = !IsInitialStatusDropdownOpen;
    }

    private void OnInitialStatusOptionClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not string value || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        InitialStatusEntry.Text = value;
        InitialStatusPicker.SelectedIndex = InitialStatusPicker.Items.IndexOf(value);
        IsInitialStatusDropdownOpen = false;
    }

    private async void OnSaveScholarClicked(object sender, EventArgs e)
    {
        AddScholarErrorLabel.IsVisible = false;

        if (_isEditMode)
        {
            await OnUpdateScholarAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(FirstNameEntry.Text) ||
            string.IsNullOrWhiteSpace(LastNameEntry.Text) ||
            string.IsNullOrWhiteSpace(IdNumberEntry.Text) ||
            string.IsNullOrWhiteSpace(EmailEntry.Text) ||
            string.IsNullOrWhiteSpace(PasswordEntry.Text) ||
            string.IsNullOrWhiteSpace(SchoolEntry.Text) ||
            DegreeProgramPicker.SelectedIndex < 0 ||
            string.IsNullOrWhiteSpace(YearLevelEntry.Text) ||
            string.IsNullOrWhiteSpace(GpaEntry.Text))
        {
            AddScholarErrorLabel.Text = "Please fill out required fields: First Name, Last Name, ID Number, Email, Password, University/School, Degree Program, Year Level, Current GPA.";
            AddScholarErrorLabel.IsVisible = true;
            return;
        }

        if (!double.TryParse(GpaEntry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var gpa))
        {
            AddScholarErrorLabel.Text = "Current GPA must be a number.";
            AddScholarErrorLabel.IsVisible = true;
            return;
        }

        try
        {
            var token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);
            await EnsureAdminTokenAndLoadAsync(token);
            token = Preferences.Default.Get(FirebaseAdminTokenKey, string.Empty);

            var auth = new FirebaseAuthRestService();
            var signUp = await auth.SignUpAsync(EmailEntry.Text.Trim(), PasswordEntry.Text);

            var firestore = new FirestoreRestService();

            var scholarData = new Dictionary<string, object?>
            {
                ["uid"] = signUp.Uid,
                ["studentId"] = IdNumberEntry.Text?.Trim(),
                ["firstName"] = FirstNameEntry.Text?.Trim(),
                ["middleName"] = MiddleNameEntry.Text?.Trim(),
                ["lastName"] = LastNameEntry.Text?.Trim(),
                ["email"] = EmailEntry.Text?.Trim(),
                ["contactNumber"] = ContactNumberEntry.Text?.Trim(),
                ["homeAddress"] = HomeAddressEntry.Text?.Trim(),
                ["gender"] = string.IsNullOrWhiteSpace(GenderEntry.Text) ? null : GenderEntry.Text.Trim(),
                ["dateOfBirth"] = DateOfBirthPicker.Date,

                ["school"] = SchoolEntry.Text?.Trim(),
                ["degreeProgram"] = DegreeProgramPicker.SelectedIndex >= 0 ? DegreeProgramPicker.Items[DegreeProgramPicker.SelectedIndex] : null,
                ["yearLevel"] = YearLevelEntry.Text?.Trim(),
                ["gpa"] = gpa,

                ["scholarshipType"] = ScholarshipTypePicker.SelectedIndex >= 0 ? ScholarshipTypePicker.Items[ScholarshipTypePicker.SelectedIndex] : null,
                ["initialStatus"] = InitialStatusPicker.SelectedIndex >= 0 ? InitialStatusPicker.Items[InitialStatusPicker.SelectedIndex] : null,
                ["additionalNotes"] = AdditionalNotesEditor.Text,
                ["createdAt"] = DateTime.UtcNow
            };

            await firestore.CreateScholarAsync(signUp.Uid, token, scholarData);

            await DisplayAlert("Success", "Student successfully added.", "OK");

            await LoadScholarsAsync(token);

            FirstNameEntry.Text = string.Empty;
            MiddleNameEntry.Text = string.Empty;
            LastNameEntry.Text = string.Empty;
            IdNumberEntry.Text = string.Empty;
            GenderEntry.Text = string.Empty;
            DateOfBirthPicker.Date = DateTime.Today;
            ContactNumberEntry.Text = string.Empty;
            EmailEntry.Text = string.Empty;
            PasswordEntry.Text = string.Empty;
            HomeAddressEntry.Text = string.Empty;

            SchoolEntry.Text = string.Empty;
            YearLevelEntry.Text = string.Empty;
            GpaEntry.Text = string.Empty;

            DegreeProgramPicker.SelectedIndex = -1;
            ScholarshipTypePicker.SelectedIndex = -1;
            InitialStatusPicker.SelectedIndex = -1;
            AdditionalNotesEditor.Text = string.Empty;

            AddScholarModal.IsVisible = false;
            AddScholarModalContent.IsVisible = false;
        }
        catch (Exception ex)
        {
            AddScholarErrorLabel.Text = ex.Message;
            AddScholarErrorLabel.IsVisible = true;
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        AuthService.Logout();
        await Shell.Current.GoToAsync("//ScholarLoginPage");
    }

    private async void OnDashboardClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//AdminDashboardPage");
    }

    private async void OnScholarsManagementClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//ScholarsManagementPage");
    }

    private async void OnDocumentVerificationClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//DocumentVerificationPage");
    }

    private async void OnRequirementsAnnouncementsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//RequirementsAnnouncementsManagementPage");
    }

    private async void OnReportsAnalyticsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//ReportsAnalyticsPage");
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPage");
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
}
