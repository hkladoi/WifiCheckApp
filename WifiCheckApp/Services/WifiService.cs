using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WifiCheckApp.Services;

#if ANDROID
using Android.OS;
using Android.Net;
using Android.Net.Wifi;
using Android.Content;
using Android.App;
#endif

namespace WifiCheckApp
{
    public class WifiInfo
    {
        public string SSID { get; set; }
        public string BSSID { get; set; }
        public int SignalStrength { get; set; }
        public string Capabilities { get; set; }
    }

    public class WifiService
    {
        private readonly IConnectivity _connectivity;
        private List<WifiInfo> _cachedNetworks = new List<WifiInfo>();
        private DateTime _lastScanTime = DateTime.MinValue;
        private const int CACHE_VALID_TIME_MS = 10000; // 10 seconds cache
        private SemaphoreSlim _scanSemaphore = new SemaphoreSlim(1, 1);

        public WifiService(IConnectivity connectivity)
        {
            _connectivity = connectivity;
        }

        public async Task<bool> IsConnectedToTargetWifi(string targetSsid, string targetGateway)
        {
            if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                return false;
            }

            // Android-specific implementation
#if ANDROID
            return await Task.Run(() => IsConnectedToTargetWifiAndroidSync(targetSsid, targetGateway));
#elif WINDOWS
            return await IsConnectedToTargetWifiWindows(targetSsid, targetGateway);
#else
            return false;
#endif
        }

#if ANDROID
        private bool IsConnectedToTargetWifiAndroidSync(string targetSsid, string targetGateway)
        {
            try
            {
                var allowedBSSIDs = new List<string>
                {
                    "30:4f:75:39:cb:d1",
                    "30:4f:75:39:cb:d0"
                }.Select(b => b.ToLower()).ToList();

                var wifiManager = Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService) as Android.Net.Wifi.WifiManager;
                if (wifiManager != null && wifiManager.ConnectionInfo != null)
                {
                    string currentSsid = wifiManager.ConnectionInfo.SSID.Replace("\"", "");
                    string bssid = wifiManager.ConnectionInfo.BSSID?.ToLower();

                    // Check SSID
                    if (currentSsid != targetSsid)
                    {
                        return false;
                    }
                    if (!allowedBSSIDs.Contains(bssid?.ToLower()))
                    {
                        return false;
                    }

                    // No need to check gateway for optimization
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking WiFi: {ex.Message}");
            }

            return false;
        }

        private string GetGatewayIP()
        {
            try
            {
                var wifiManager = (WifiManager)Android.App.Application.Context.GetSystemService(Context.WifiService);
                var dhcpInfo = wifiManager?.DhcpInfo;

                if (dhcpInfo != null)
                {
                    int gateway = dhcpInfo.Gateway;
                    return Android.Text.Format.Formatter.FormatIpAddress(gateway);
                }

                return "Không thể lấy Gateway";
            }
            catch
            {
                return string.Empty;
            }
        }

#endif

#if WINDOWS
        private async Task<bool> IsConnectedToTargetWifiWindows(string targetSsid, string targetGateway)
        {
            try
            {
                // Check if connected to any network
                if (_connectivity.NetworkAccess != NetworkAccess.Internet)
                {
                    return false;
                }

                // For Windows, we can use command-line tools via Process
                var wifiNameInfo = await RunCommandAsync("netsh wlan show interfaces");
                if (wifiNameInfo.Contains(targetSsid))
                {
                    // Check gateway
                    var gatewayInfo = await RunCommandAsync("ipconfig");
                    return gatewayInfo.Contains(targetGateway);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking Windows WiFi: {ex.Message}");
            }

            return false;
        }

        private async Task<string> RunCommandAsync(string command)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {command}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output;
        }
#endif
    }
}