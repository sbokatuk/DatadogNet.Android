namespace DatadogNet.Android.Example;

public partial class MainPage : ContentPage
{
	int count = 0;

	public MainPage()
	{
		InitializeComponent();
	}

	private void OnCounterClicked(object? sender, EventArgs e)
	{
		count++;

		if (count == 1)
			CounterBtn.Text = $"Clicked {count} time";
		else
			CounterBtn.Text = $"Clicked {count} times";

		SemanticScreenReader.Announce(CounterBtn.Text);

		// A RUM action, with an attribute worth querying on later.
		Datadog.TrackTap("counter", new Dictionary<string, object?> { ["count"] = count });
	}

	private IDisposable? viewScope;

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// One RUM view per page. MAUI renders every page into a single Activity, so without this
		// the whole app reports as one view.
		viewScope = Datadog.StartView("main", "Main Page");
	}

	protected override void OnDisappearing()
	{
		viewScope?.Dispose();
		viewScope = Datadog.StartView("main", "Main Page");
		base.OnDisappearing();
	}

	private void OnReportErrorClicked(object? sender, EventArgs e)
	{
		try
		{
			throw new InvalidOperationException("Something the user did went wrong.");
		}
		catch (Exception exception)
		{
			Datadog.TrackError(exception);
		}
	}
}
