using Microsoft.Maui.Storage;

namespace WifiCheckApp
{
    public partial class MainPage : ContentPage
    {
        private readonly string _targetWifiName = "THE";
        private readonly string _targetGateway = "192.168.1.1";
        private readonly IConnectivity _connectivity;
        private readonly WifiService _wifiService;
        private readonly EmailService _emailService;
        private Timer _wifiCheckTimer;

        public MainPage(IConnectivity connectivity, WifiService wifiService, EmailService emailService)
        {
            InitializeComponent();
            _connectivity = connectivity;
            _wifiService = wifiService;
            _emailService = emailService;
            RefreshPanel.IsVisible = true;

            // Start a timer to check WiFi status periodically
            _wifiCheckTimer = new Timer(CheckWifiStatus, null, 0, 5000); // Check every 5 seconds

            // Check if we have a saved email
            CheckSavedEmail();

            // Show current time
            Device.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                TimeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
                return true;
            });
        }

        private void CheckWifiStatus(object state)
        {
            // Need to run on UI thread
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                bool isConnected = await _wifiService.IsConnectedToTargetWifi(_targetWifiName, _targetGateway);

                if (isConnected)
                {
                    // If connected to target WiFi, check if we have email
                    string savedEmail = await _emailService.GetSavedEmail();

                    RefreshPanel.IsVisible = false;
                    WifiStatusLabel.Text = $"Đã kết nối đến wifi công ty";
                    WifiStatusLabel.TextColor = Colors.Green;

                    // Make sure EmailLabel is visible and displays the saved email
                    EmailLabel.IsVisible = true;
                    EmailLabel.Text = savedEmail ?? string.Empty;

                    if (string.IsNullOrEmpty(savedEmail))
                    {
                        // Show email input if no email is saved
                        EmailFrame.IsVisible = true;
                        ButtonsPanel.IsVisible = false;
                    }
                    else
                    {
                        // Show buttons if we have the email
                        EmailFrame.IsVisible = false;
                        ButtonsPanel.IsVisible = true;
                    }
                }
                else
                {
                    // Always make sure RefreshPanel is visible when not connected
                    RefreshPanel.IsVisible = true;

                    // Keep EmailLabel visible if it contains text
                    if (!string.IsNullOrEmpty(EmailLabel.Text))
                    {
                        EmailLabel.IsVisible = true;
                    }
                    else
                    {
                        EmailLabel.IsVisible = false;
                    }

                    WifiStatusLabel.Text = $"Không kết nối đến wifi công ty";
                    WifiStatusLabel.TextColor = Colors.Red;

                    // Don't hide EmailFrame if user is currently entering email
                    // But do hide ButtonsPanel since actions require WiFi
                    ButtonsPanel.IsVisible = false;

                    // Show notification
                    //await ShowWifiNotification();
                }
            });
        }

        private async void CheckSavedEmail()
        {
            string savedEmail = await _emailService.GetSavedEmail();
            if (!string.IsNullOrEmpty(savedEmail))
            {
                EmailEntry.Text = savedEmail;
                EmailLabel.Text = savedEmail;
                EmailLabel.IsVisible = true;
            }
        }

        private async Task ShowWifiNotification()
        {
            const string key = "NotificationShown";

            if (!Preferences.ContainsKey(key) || !Preferences.Get(key, false))
            {
                await App.Current.MainPage.DisplayAlert(
                    "Cảnh báo kết nối",
                    $"Vui lòng kết nối đến mạng WiFi {_targetWifiName}",
                    "OK");

                Preferences.Set(key, true);
            }
        }

        private async void OnSaveEmailClicked(object sender, EventArgs e)
        {
            string email = EmailEntry.Text?.Trim();

            if (string.IsNullOrEmpty(email) || !IsValidEmail(email))
            {
                await DisplayAlert("Lỗi", "Vui lòng nhập email hợp lệ", "OK");
                return;
            }

            await _emailService.SaveEmail(email);

            // Update UI - make sure to update EmailLabel with the new email
            EmailLabel.Text = email;
            EmailLabel.IsVisible = true;
            EmailFrame.IsVisible = false;
            ButtonsPanel.IsVisible = true;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private void OnCheckInClicked(object sender, EventArgs e)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
            DisplayAlert("Chấm công", $"Thời gian chấm công: {timestamp}", "OK");
        }

        private void OnCheckOutClicked(object sender, EventArgs e)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
            DisplayAlert("Ra về", $"Thời gian ra về: {timestamp}", "OK");
        }

        private void OnRefreshClicked(object sender, EventArgs e)
        {
            CheckWifiStatus(null);
        }

        private async Task RequestLocationPermissionAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                var result = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                //if (result == PermissionStatus.Granted)
                //{
                //    await DisplayAlert("Thành công", "Đã cấp quyền truy cập vị trí.", "OK");
                //}
                //else
                //{
                //    await DisplayAlert("Từ chối", "Bạn chưa cấp quyền truy cập vị trí.", "OK");
                //}
            }
        }

        protected override async void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                await RequestLocationPermissionAsync();
            }
            catch (Exception e)
            {
                throw; // TODO handle exception
            }
        }
    }
}