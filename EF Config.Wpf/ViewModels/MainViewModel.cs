using System.Collections.ObjectModel;
using EF_Config.Wpf.Mvvm;

namespace EF_Config.Wpf.ViewModels;

// Depends only on IPersonRepository, not DataContext/EF Core directly -
// keeps this class unit-testable with a fake repository and unaware of
// which database provider (or even whether a real database) is behind it.
public class MainViewModel : ViewModelBase
{
    private readonly IPersonRepository _repository;

    public ObservableCollection<Person> People { get; } = new();

    private Person? _selectedPerson;
    public Person? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (SetField(ref _selectedPerson, value))
            {
                LoadEditingFieldsFrom(_selectedPerson);
                RaiseCommandsCanExecuteChanged();
            }
        }
    }

    // Separate editable fields rather than binding the form directly to
    // SelectedPerson - this way Add doesn't require an existing selection,
    // and Update/Delete act explicitly on SelectedPerson.
    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set
        {
            if (SetField(ref _editingName, value))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _editingAddress = string.Empty;
    public string EditingAddress
    {
        get => _editingAddress;
        set => SetField(ref _editingAddress, value);
    }

    public RelayCommand AddCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public MainViewModel(IPersonRepository repository)
    {
        _repository = repository;

        AddCommand = new RelayCommand(Add, () => !string.IsNullOrWhiteSpace(EditingName));
        UpdateCommand = new RelayCommand(Update, () => SelectedPerson is not null);
        DeleteCommand = new RelayCommand(Delete, () => SelectedPerson is not null);
        RefreshCommand = new RelayCommand(Refresh);

        Refresh();
    }

    private void Refresh()
    {
        People.Clear();
        foreach (var person in _repository.GetAll())
        {
            People.Add(person);
        }
    }

    private void Add()
    {
        var person = new Person { Name = EditingName, Address = EditingAddress };
        _repository.Add(person);
        People.Add(person);
        ClearEditingFields();
    }

    private void Update()
    {
        if (SelectedPerson is null)
        {
            return;
        }

        // Person implements INotifyPropertyChanged, so these in-place
        // assignments raise PropertyChanged and the DataGrid re-renders the
        // affected cells immediately - no manual collection refresh needed.
        SelectedPerson.Name = EditingName;
        SelectedPerson.Address = EditingAddress;
        _repository.Update(SelectedPerson);
    }

    private void Delete()
    {
        if (SelectedPerson is null)
        {
            return;
        }

        _repository.Delete(SelectedPerson);
        People.Remove(SelectedPerson);
        SelectedPerson = null;
        ClearEditingFields();
    }

    private void LoadEditingFieldsFrom(Person? person)
    {
        EditingName = person?.Name ?? string.Empty;
        EditingAddress = person?.Address ?? string.Empty;
    }

    private void ClearEditingFields()
    {
        EditingName = string.Empty;
        EditingAddress = string.Empty;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        UpdateCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }
}
