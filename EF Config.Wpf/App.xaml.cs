using System.Windows;
using EF_Config.Wpf.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EF_Config.Wpf;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _scope;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Same IConfiguration setup as the console app's Program.cs -
        // appsettings.json + an optional, gitignored appsettings.Local.json
        // layered on top for real local credentials.
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build();

        string connectionString = configuration.GetConnectionString("AppConnectionString")
            ?? throw new InvalidOperationException("Connection string 'AppConnectionString' not found in appsettings.json");

        var services = new ServiceCollection();
        services.AddDbContext<DataContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<MainViewModel>();
        services.AddScoped<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        // One scope held for the lifetime of the app session, not one per
        // operation - mirrors Program.cs's single manual CreateScope(), just
        // for as long as the window stays open instead of one console run.
        _scope = _serviceProvider.CreateScope();

        var context = _scope.ServiceProvider.GetRequiredService<DataContext>();
        context.Database.EnsureCreated();

        var mainWindow = _scope.ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scope?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
