using Microsoft.Extensions.DependencyInjection;
using NN_WWD_Factory.Services;
using NN_WWD_Factory.ViewModels;
using NN_WWD_Factory.Views.Windows;

namespace NN_WWD_Factory.Extensions;

public static class AppExtensions {
    public static IServiceCollection AddServices(this IServiceCollection services) {
        services.AddSingleton<ConnectionToFactoryServerService>();

        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services) {
        services.AddSingleton<MainViewModel>();

        return services;
    }

    public static IServiceCollection AddViews(this IServiceCollection services) {
        services.AddSingleton<MainWindow>();

        return services;
    }
}
