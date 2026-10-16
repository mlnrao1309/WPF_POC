using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace AlgoTrading.Behaviors.CookieService
{
    public interface ICookieService
    {
        Task<string?> GetAccessTokenAsync(CoreWebView2CookieManager cookieManager, string url, string cookieName);
    }

    public class CookieService : ICookieService
    {
        public async Task<string?> GetAccessTokenAsync(CoreWebView2CookieManager cookieManager, string url, string cookieName)
        {
            // Get list of cookies for the specific domain/URL
            List<CoreWebView2Cookie> cookies = await cookieManager.GetCookiesAsync(url);

            CoreWebView2Cookie? targetCookie = cookies.FirstOrDefault(c => c.Name == cookieName);
            return targetCookie?.Value;
        }
    }
}
