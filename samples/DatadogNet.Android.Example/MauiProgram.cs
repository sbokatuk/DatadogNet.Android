using Microsoft.Extensions.Logging;

namespace DatadogNet.Android.Example;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// As early as possible: crash reporting only covers what happens after it is enabled, and
		// startup crashes are the ones worth catching.
		Datadog.Initialize(global::Android.App.Application.Context);

		// As early as possible: crash reporting only covers what happens after it is enabled, and
		// startup crashes are the ones worth catching.

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
