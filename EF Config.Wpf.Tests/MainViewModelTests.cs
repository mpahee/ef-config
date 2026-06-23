using EF_Config.Wpf.Tests.Fakes;
using EF_Config.Wpf.ViewModels;
using FluentAssertions;

namespace EF_Config.Wpf.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Constructor_PopulatesPeopleFromRepository()
    {
        var repository = new FakePersonRepository();
        repository.Seed(
            new Person { Name = "Alice", Address = "1 Main St" },
            new Person { Name = "Bob", Address = "2 Main St" });

        var viewModel = new MainViewModel(repository);

        viewModel.People.Should().HaveCount(2);
        viewModel.People.Select(p => p.Name).Should().Contain("Alice", "Bob");
    }

    [Fact]
    public void AddCommand_CanExecute_FalseWhenEditingNameEmpty()
    {
        var viewModel = new MainViewModel(new FakePersonRepository());

        viewModel.EditingName = string.Empty;

        viewModel.AddCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AddCommand_CanExecute_TrueWhenEditingNameSet()
    {
        var viewModel = new MainViewModel(new FakePersonRepository());

        viewModel.EditingName = "Grace Hopper";

        viewModel.AddCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AddCommand_Execute_AddsPersonToRepositoryAndCollection()
    {
        var repository = new FakePersonRepository();
        var viewModel = new MainViewModel(repository);
        viewModel.EditingName = "Ada Lovelace";
        viewModel.EditingAddress = "1 Analytical Engine Way";

        viewModel.AddCommand.Execute(null);

        repository.AddCallCount.Should().Be(1);
        viewModel.People.Should().ContainSingle(p => p.Name == "Ada Lovelace");
        viewModel.EditingName.Should().BeEmpty();
    }

    [Fact]
    public void UpdateCommand_CanExecute_FalseWhenNoSelection()
    {
        var viewModel = new MainViewModel(new FakePersonRepository());

        viewModel.SelectedPerson = null;

        viewModel.UpdateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void UpdateCommand_CanExecute_TrueWhenPersonSelected()
    {
        var repository = new FakePersonRepository();
        repository.Seed(new Person { Name = "Alan Turing", Address = "1 Bletchley Park" });
        var viewModel = new MainViewModel(repository);

        viewModel.SelectedPerson = viewModel.People.Single();

        viewModel.UpdateCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void UpdateCommand_Execute_UpdatesSelectedPersonAndCallsRepository()
    {
        var repository = new FakePersonRepository();
        repository.Seed(new Person { Name = "Alan Turing", Address = "1 Bletchley Park" });
        var viewModel = new MainViewModel(repository);
        viewModel.SelectedPerson = viewModel.People.Single();
        viewModel.EditingName = "Alan Mathison Turing";

        viewModel.UpdateCommand.Execute(null);

        repository.UpdateCallCount.Should().Be(1);
        viewModel.SelectedPerson.Name.Should().Be("Alan Mathison Turing");
    }

    [Fact]
    public void DeleteCommand_Execute_RemovesFromRepositoryAndCollection()
    {
        var repository = new FakePersonRepository();
        repository.Seed(new Person { Name = "Margaret Hamilton", Address = "1 Apollo Way" });
        var viewModel = new MainViewModel(repository);
        viewModel.SelectedPerson = viewModel.People.Single();

        viewModel.DeleteCommand.Execute(null);

        repository.DeleteCallCount.Should().Be(1);
        viewModel.People.Should().BeEmpty();
        viewModel.SelectedPerson.Should().BeNull();
    }

    [Fact]
    public void RefreshCommand_Execute_ReloadsFromRepository()
    {
        var repository = new FakePersonRepository();
        var viewModel = new MainViewModel(repository);
        repository.Seed(new Person { Name = "Katherine Johnson", Address = "1 Mission Control" });

        viewModel.RefreshCommand.Execute(null);

        viewModel.People.Should().ContainSingle(p => p.Name == "Katherine Johnson");
    }
}
