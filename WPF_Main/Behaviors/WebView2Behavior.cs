using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;

namespace AlgoTrading.Behaviors
{
    public static class WebView2Behavior
    {
        // Source (URI) attached property
        public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
            "Source",
            typeof(string),
            typeof(WebView2Behavior),
            new PropertyMetadata(null, OnSourceChanged));

        public static void SetSource(DependencyObject element, string value) => element.SetValue(SourceProperty, value);
        public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

        // ResponseReceivedCommand attached property
        public static readonly DependencyProperty ResponseReceivedCommandProperty = DependencyProperty.RegisterAttached(
            "ResponseReceivedCommand",
            typeof(ICommand),
            typeof(WebView2Behavior),
            new PropertyMetadata(null, OnResponseReceivedCommandChanged));

        public static void SetResponseReceivedCommand(DependencyObject element, ICommand value) => element.SetValue(ResponseReceivedCommandProperty, value);
        public static ICommand? GetResponseReceivedCommand(DependencyObject element) => (ICommand?)element.GetValue(ResponseReceivedCommandProperty);

        // Store the handler so we can detach later
        private static readonly DependencyProperty ResponseHandlerProperty = DependencyProperty.RegisterAttached(
            "ResponseHandler",
            typeof(EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs>),
            typeof(WebView2Behavior),
            new PropertyMetadata(null));

        private static void SetResponseHandler(DependencyObject element, EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs>? handler) => element.SetValue(ResponseHandlerProperty, handler);
        private static EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs>? GetResponseHandler(DependencyObject element) => (EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs>?)element.GetValue(ResponseHandlerProperty);

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebView2 webview)
            {
                var uri = e.NewValue as string;
                _ = EnsureAndNavigateAsync(webview, uri);
            }
        }

        private static async System.Threading.Tasks.Task EnsureAndNavigateAsync(WebView2 webview, string? uri)
        {
            try
            {
                if (webview.CoreWebView2 == null)
                {
                    await webview.EnsureCoreWebView2Async(null);
                }

                if (!string.IsNullOrEmpty(uri))
                {
                    if (webview.CoreWebView2 != null)
                        webview.CoreWebView2.Navigate(uri);
                    else
                        webview.Source = new Uri(uri);
                }
            }
            catch
            {
                // ignore initialization/navigation errors
            }
        }

        private static void OnResponseReceivedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebView2 webview)
            {
                // Detach old handler if present
                var oldHandler = GetResponseHandler(webview);
                if (oldHandler != null && webview.CoreWebView2 != null)
                {
                    try { webview.CoreWebView2.WebResourceResponseReceived -= oldHandler; } catch { }
                }

                var cmd = e.NewValue as ICommand;
                if (cmd == null)
                {
                    SetResponseHandler(webview, null);
                    return;
                }

                EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs> handler = (s, args) =>
                {
                    try
                    {
                        var command = GetResponseReceivedCommand(webview);
                        if (command != null && command.CanExecute(args))
                            command.Execute(args);
                    }
                    catch { }
                };

                SetResponseHandler(webview, handler);

                // If already initialized, attach now; otherwise attach after initialization
                if (webview.CoreWebView2 != null)
                {
                    try { webview.CoreWebView2.WebResourceResponseReceived += handler; } catch { }
                }
                else
                {
                    // ensure CoreWebView2 and attach
                    _ = EnsureAndAttachHandlerAsync(webview, handler);
                }
            }
        }

        private static async System.Threading.Tasks.Task EnsureAndAttachHandlerAsync(WebView2 webview, EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs> handler)
        {
            try
            {
                await webview.EnsureCoreWebView2Async(null);
                if (webview.CoreWebView2 != null)
                {
                    webview.CoreWebView2.WebResourceResponseReceived += handler;

                    // Enable the Network domain in Chromium DevTools Protocol so network events are available
                    try
                    {
                        // Call DevTools protocol to enable Network domain
                        _ = webview.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                    }
                    catch { }

                    // Also attach NewWindowRequested to open target=_blank links in the same WebView
                    try
                    {
                        // avoid attaching multiple times
                        if (!GetHasNewWindowHandler(webview))
                        {
                            webview.CoreWebView2.NewWindowRequested += (s, args) =>
                            {
                                try
                                {
                                    var newUri = args.Uri;
                                    // prevent external browser; navigate current view instead
                                    args.Handled = true;
                                    if (!string.IsNullOrEmpty(newUri))
                                    {
                                        webview.CoreWebView2.Navigate(newUri);
                                    }
                                }
                                catch { }
                            };
                            SetHasNewWindowHandler(webview, true);
                        }
                    }
                    catch { }
                    // If a WebSocket frame command is present, ensure DevTools receiver is attached as well
                    try
                    {
                        var cmd = GetWebSocketFrameReceivedCommand(webview);
                        if (cmd != null)
                        {
                            _ = EnsureAndAttachWebSocketHandlerAsync(webview);
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static readonly DependencyProperty HasNewWindowHandlerProperty = DependencyProperty.RegisterAttached(
            "HasNewWindowHandler",
            typeof(bool),
            typeof(WebView2Behavior),
            new PropertyMetadata(false));

        private static void SetHasNewWindowHandler(DependencyObject element, bool value) => element.SetValue(HasNewWindowHandlerProperty, value);
        private static bool GetHasNewWindowHandler(DependencyObject element) => (bool)element.GetValue(HasNewWindowHandlerProperty);

        // WebSocket DevTools event command
        public static readonly DependencyProperty WebSocketFrameReceivedCommandProperty = DependencyProperty.RegisterAttached(
            "WebSocketFrameReceivedCommand",
            typeof(ICommand),
            typeof(WebView2Behavior),
            new PropertyMetadata(null, OnWebSocketFrameReceivedCommandChanged));

        public static void SetWebSocketFrameReceivedCommand(DependencyObject element, ICommand value) => element.SetValue(WebSocketFrameReceivedCommandProperty, value);
        public static ICommand? GetWebSocketFrameReceivedCommand(DependencyObject element) => (ICommand?)element.GetValue(WebSocketFrameReceivedCommandProperty);

        private static readonly DependencyProperty WebSocketHandlerProperty = DependencyProperty.RegisterAttached(
            "WebSocketHandler",
            typeof(EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>),
            typeof(WebView2Behavior),
            new PropertyMetadata(null));

        private static void SetWebSocketHandler(DependencyObject element, EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? handler) => element.SetValue(WebSocketHandlerProperty, handler);
        private static EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? GetWebSocketHandler(DependencyObject element) => (EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>?)element.GetValue(WebSocketHandlerProperty);

        private static void OnWebSocketFrameReceivedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebView2 webview)
            {
                // Detach old handler if present
                var old = GetWebSocketHandler(webview);
                if (old != null && webview.CoreWebView2 != null)
                {
                    try
                    {
                        var receiverOld = webview.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.webSocketFrameReceived");
                        receiverOld.DevToolsProtocolEventReceived -= old;
                    }
                    catch { }
                }

                var cmd = e.NewValue as ICommand;
                if (cmd == null)
                {
                    SetWebSocketHandler(webview, null);
                    return;
                }

                EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler = (s, args) =>
                {
                    try
                    {
                        var command = GetWebSocketFrameReceivedCommand(webview);
                        if (command != null && command.CanExecute(args.ParameterObjectAsJson))
                            command.Execute(args.ParameterObjectAsJson);
                    }
                    catch { }
                };

                SetWebSocketHandler(webview, handler);

                if (webview.CoreWebView2 != null)
                {
                    try
                    {
                        var receiver = webview.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.webSocketFrameReceived");
                        receiver.DevToolsProtocolEventReceived += handler;
                    }
                    catch { }
                }
                else
                {
                    _ = EnsureAndAttachWebSocketHandlerAsync(webview);
                }
            }
        }

        private static async System.Threading.Tasks.Task EnsureAndAttachWebSocketHandlerAsync(WebView2 webview)
        {
            try
            {
                await webview.EnsureCoreWebView2Async(null);
                var handler = GetWebSocketHandler(webview);
                var cmd = GetWebSocketFrameReceivedCommand(webview);
                if (handler != null && cmd != null && webview.CoreWebView2 != null)
                {
                    try
                    {
                        var receiver = webview.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.webSocketFrameReceived");
                        receiver.DevToolsProtocolEventReceived += handler;
                    }
                    catch { }
                }
            }
            catch { }
        }
        //CookieService attached property
        #region CookieService Attached Property
        // Property to bind the ICommand from your ViewModel
        public static readonly DependencyProperty TokenReceivedCommandProperty =
            DependencyProperty.RegisterAttached(
                "TokenReceivedCommand",
                typeof(ICommand),
                typeof(WebView2Behavior),
                new PropertyMetadata(null, OnTokenReceivedCommandChanged));

        // Property to specify which cookie name to extract
        public static readonly DependencyProperty TargetCookieNameProperty =
            DependencyProperty.RegisterAttached(
                "TargetCookieName",
                typeof(string),
                typeof(WebView2Behavior),
                new PropertyMetadata("access_token"));

        public static void SetTokenReceivedCommand(DependencyObject element, ICommand value) =>
            element.SetValue(TokenReceivedCommandProperty, value);

        public static ICommand GetTokenReceivedCommand(DependencyObject element) =>
            (ICommand)element.GetValue(TokenReceivedCommandProperty);

        public static void SetTargetCookieName(DependencyObject element, string value) =>
            element.SetValue(TargetCookieNameProperty, value);

        public static string GetTargetCookieName(DependencyObject element) =>
            (string)element.GetValue(TargetCookieNameProperty);

        private static void OnTokenReceivedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebView2 webView)
            {
                webView.NavigationCompleted -= WebView_NavigationCompleted;
                if (e.NewValue != null)
                {
                    webView.NavigationCompleted += WebView_NavigationCompleted;
                }
            }
        }

        private static async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (sender is not WebView2 webView || !e.IsSuccess || webView.CoreWebView2 == null)
                return;

            string targetCookie = GetTargetCookieName(webView);
            ICommand command = GetTokenReceivedCommand(webView);

            // Access the WebView2 CookieManager directly from the initialized control
            CoreWebView2CookieManager cookieManager = webView.CoreWebView2.CookieManager;
            List<CoreWebView2Cookie> cookies = await cookieManager.GetCookiesAsync(webView.Source.AbsoluteUri);

            CoreWebView2Cookie? tokenCookie = cookies.FirstOrDefault(c => c.Name == targetCookie);

            if (tokenCookie != null && command.CanExecute(tokenCookie.Value))
            {
                command.Execute(tokenCookie.Value);
            }
        }
        #endregion
    }
}
