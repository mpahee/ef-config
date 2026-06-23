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

        try
        {
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
        catch (Exception ex)
        {
            // Without this, a startup failure (most commonly: can't reach
            // Postgres with the shipped placeholder appsettings.json
            // credentials) is an unhandled exception - WPF shows a generic
            // crash, and a published/downloaded build gives the user no clue
            // what to fix. Surface the actual cause and exit cleanly instead.
            MessageBox.Show(
                $"EF Config.Wpf failed to start.\n\n" +
                $"This usually means the app couldn't connect to Postgres using the connection string in appsettings.json.\n\n" +
                $"If you downloaded this from a Release, the shipped appsettings.json only has placeholder credentials " +
                $"(Username=CHANGE_ME). Create an appsettings.Local.json next to the .exe with your real " +
                $"\"ConnectionStrings:AppConnectionString\" value (same format as appsettings.json) and try again.\n\n" +
                $"Underlying error: {ex.Message}",
                "EF Config.Wpf - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scope?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
