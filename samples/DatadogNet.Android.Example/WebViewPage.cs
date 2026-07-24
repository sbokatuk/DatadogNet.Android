using Com.Datadog.Android.Webview;

namespace DatadogNet.Android.Example;

/// <summary>
/// A page hosting an <c>Android.Webkit.WebView</c> with the Datadog bridge installed, through the
/// raw <c>DatadogNet.WebView.Android</c> binding.
/// </summary>
/// <remarks>
/// For anything to actually cross the bridge, the page inside must run the Datadog Browser SDK
/// and its host must be on the allowlist — example.com does not, so what this page demonstrates
/// is the wiring. The allowlist matters: the bridge lets page JavaScript write into your RUM
/// session, so it is opt-in per host, matched by suffix.
/// </remarks>
public sealed class WebViewPage : ContentPage
{
    private readonly WebView webView;

    public WebViewPage()
    {
        Title = "Web view";

        webView = new WebView { Source = "https://example.com/" };

        // The platform view exists once the handler has connected, which Loaded guarantees.
        // There is no disable on Android - the bridge lives as long as the web view does.
        webView.Loaded += OnWebViewLoaded;

        var layout = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            ],
        };

        layout.Add(
            new Label
            {
                Padding = 12,
                FontSize = 13,
                Text = "WebViewTracking is enabled on this WebView. A page running the Datadog "
                       + "Browser SDK on an allowlisted host would report into the surrounding "
                       + "native session; example.com does not, so the point here is the wiring.",
            },
            0,
            0);
        layout.Add(webView, 0, 1);

        Content = layout;
    }

    private void OnWebViewLoaded(object? sender, EventArgs e)
    {
        if (webView.Handler?.PlatformView is global::Android.Webkit.WebView platform)
        {
            WebViewTracking.Enable(platform, new List<string> { "example.com" });
        }
    }
}
