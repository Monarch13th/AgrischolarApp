namespace AgriScholarApp.Services;

public sealed class ScholarRecipient : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string ScholarId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string HomeAddress { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string School { get; set; } = string.Empty;
    public string DegreeProgram { get; set; } = string.Empty;
    public string YearLevel { get; set; } = string.Empty;
    public string ScholarshipType { get; set; } = string.Empty;
    public string InitialStatus { get; set; } = string.Empty;
    public double? Gpa { get; set; }
    private bool _isOptionsOpen;
    public bool IsOptionsOpen
    {
        get => _isOptionsOpen;
        set
        {
            if (_isOptionsOpen == value) return;
            _isOptionsOpen = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsOptionsOpen)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(OptionsMinHeight)));
        }
    }

    public double OptionsMinHeight => IsOptionsOpen ? 200 : 140;

    public string FullName
    {
        get
        {
            var mid = string.IsNullOrWhiteSpace(MiddleName) ? string.Empty : $" {MiddleName}";
            return $"{FirstName}{mid} {LastName}".Trim();
        }
    }

    public string FullNameFormatted
    {
        get
        {
            var first = (FirstName ?? string.Empty).Trim();
            var last = (LastName ?? string.Empty).Trim();
            var mid = (MiddleName ?? string.Empty).Trim();

            var middleInitial = string.Empty;
            if (!string.IsNullOrWhiteSpace(mid))
            {
                middleInitial = $" {mid[0]}.";
            }

            return $"{first}{middleInitial} {last}".Trim();
        }
    }

    public string NameDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FullNameFormatted)) return FullNameFormatted;
            if (!string.IsNullOrWhiteSpace(StudentId)) return StudentId;
            if (!string.IsNullOrWhiteSpace(ScholarId)) return ScholarId;
            return "Scholar";
        }
    }

    public string GpaDisplay => Gpa.HasValue ? Gpa.Value.ToString("0.00") : string.Empty;

    public string AvatarInitials
    {
        get
        {
            var f = !string.IsNullOrWhiteSpace(FirstName) ? FirstName[0].ToString().ToUpper() : string.Empty;
            var l = !string.IsNullOrWhiteSpace(LastName) ? LastName[0].ToString().ToUpper() : string.Empty;
            var initials = f + l;
            return string.IsNullOrEmpty(initials) ? "??" : initials;
        }
    }

    public string AvatarBgColor
    {
        get
        {
            var palette = new[] { "#FEF3C7", "#DCFCE7", "#DBEAFE", "#FCE7F3", "#EDE9FE", "#FFEDD5", "#ECFDF5", "#FFF1F2" };
            var hash = Math.Abs(NameDisplay?.GetHashCode() ?? 0);
            return palette[hash % palette.Length];
        }
    }

    public string AvatarTextColor
    {
        get
        {
            var palette = new[] { "#B45309", "#15803D", "#1D4ED8", "#9D174D", "#6D28D9", "#C2410C", "#047857", "#BE123C" };
            var hash = Math.Abs(NameDisplay?.GetHashCode() ?? 0);
            return palette[hash % palette.Length];
        }
    }
}
